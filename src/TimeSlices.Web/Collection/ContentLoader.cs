using System.Text.Json;

namespace TimeSlices.Web.Collection;

/// <summary>Ошибка содержания коллекции: приложение не стартует, пока она не исправлена.</summary>
public sealed class ContentLoadException(IReadOnlyList<string> problems)
    : Exception("Содержание коллекции не загружено:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => " - " + p)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Читает Content/tracks.json, Content/slices/*/slice.json и манифесты примеров,
/// проверяет ссылки и отсутствие циклов в графе срезов. Поля манифестов подробно
/// проверяет scripts/validate_manifest.py; здесь — только то, без чего оболочка не работает.
/// </summary>
public static class ContentLoader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ContentCatalog Load(string root)
    {
        root = Path.GetFullPath(root);
        var problems = new List<string>();

        var tracks = ReadJson<List<Track>>(Path.Combine(root, "tracks.json"), problems) ?? [];
        var trackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tracks)
            if (!trackIds.Add(t.Id))
                problems.Add($"tracks.json: трек '{t.Id}' объявлен дважды");

        var slices = new Dictionary<string, Slice>(StringComparer.Ordinal);
        var examples = new Dictionary<string, Example>(StringComparer.Ordinal);
        var slicesDir = Path.Combine(root, "slices");
        var sliceDirs = Directory.Exists(slicesDir)
            ? Directory.GetDirectories(slicesDir).OrderBy(d => d, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        foreach (var dir in sliceDirs)
        {
            var file = Path.Combine(dir, "slice.json");
            if (!File.Exists(file))
            {
                problems.Add($"{Rel(root, dir)}: нет slice.json");
                continue;
            }
            var slice = ReadJson<Slice>(file, problems);
            if (slice is null)
                continue;
            if (slice.Id != Path.GetFileName(dir))
                problems.Add($"{Rel(root, file)}: id '{slice.Id}' не совпадает с именем папки");
            if (!slices.TryAdd(slice.Id, slice))
                problems.Add($"{Rel(root, file)}: срез '{slice.Id}' объявлен дважды");

            LoadExamples(root, dir, examples, problems);
        }

        foreach (var slice in slices.Values)
            CheckReferences(slice, trackIds, slices, examples, problems);

        var order = problems.Count == 0 ? TopologicalOrder(slices, problems) : [];

        if (problems.Count > 0)
            throw new ContentLoadException(problems);

        return new ContentCatalog(root, tracks, slices, examples, order);
    }

    private static void LoadExamples(string root, string sliceDir, Dictionary<string, Example> examples, List<string> problems)
    {
        var examplesDir = Path.Combine(sliceDir, "examples");
        if (!Directory.Exists(examplesDir))
            return;

        foreach (var dir in Directory.GetDirectories(examplesDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var file = Path.Combine(dir, "manifest.json");
            if (!File.Exists(file))
            {
                problems.Add($"{Rel(root, dir)}: нет manifest.json");
                continue;
            }
            var example = ReadJson<Example>(file, problems);
            if (example is null)
                continue;
            example.Directory = dir;
            if (!examples.TryAdd(example.Id, example))
                problems.Add($"{Rel(root, file)}: пример '{example.Id}' объявлен дважды");

            foreach (var impl in example.Implementations)
            {
                impl.Directory = Path.Combine(dir, "implementations", impl.Id);
                var entry = Path.Combine(impl.Directory, impl.Entry);
                if (string.IsNullOrEmpty(impl.Entry) || !File.Exists(entry))
                    problems.Add($"{Rel(root, file)}: у реализации '{impl.Id}' не найден entry '{impl.Entry}'");

                foreach (var pr in impl.Processes)
                {
                    var pf = Path.Combine(impl.Directory, pr.File);
                    if (!File.Exists(pf))
                        problems.Add($"{Rel(root, file)}: у реализации '{impl.Id}' не найден процесс '{pr.File}'");
                    else
                        pr.Process = ReadJson<Process>(pf, problems);
                }
            }
        }
    }

    private static void CheckReferences(
        Slice slice, HashSet<string> trackIds, Dictionary<string, Slice> slices,
        Dictionary<string, Example> examples, List<string> problems)
    {
        var ctx = $"slices/{slice.Id}/slice.json";
        if (slice.Tracks.Count == 0)
            problems.Add($"{ctx}: срез не входит ни в один трек");
        foreach (var t in slice.Tracks.Where(t => !trackIds.Contains(t)))
            problems.Add($"{ctx}: трек '{t}' не объявлен в tracks.json");

        foreach (var p in slice.Prerequisites)
        {
            if (p.Slice == slice.Id)
                problems.Add($"{ctx}: срез не может быть предшественником самого себя");
            else if (!slices.TryGetValue(p.Slice, out var prev))
                problems.Add($"{ctx}: предшественник '{p.Slice}' не найден");
            else if (!prev.Tracks.Contains(p.Track))
                problems.Add($"{ctx}: предшественник '{p.Slice}' не входит в трек '{p.Track}'");
        }

        foreach (var e in slice.Examples.Where(e => !examples.ContainsKey(e)))
            problems.Add($"{ctx}: пример '{e}' не найден (нет examples/{e}/manifest.json ни в одном срезе)");
    }

    /// <summary>
    /// Порядок срезов, в котором предшественники идут раньше последующих (DFS).
    /// При цикле — ошибка с перечислением срезов цикла.
    /// </summary>
    private static List<string> TopologicalOrder(Dictionary<string, Slice> slices, List<string> problems)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 — в обходе, 2 — готов
        var path = new List<string>();

        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var s))
            {
                if (s == 2)
                    return true;
                var cycle = path.Skip(path.IndexOf(id)).Append(id);
                problems.Add("цикл в графе срезов: " + string.Join(" → ", cycle));
                return false;
            }
            state[id] = 1;
            path.Add(id);
            foreach (var p in slices[id].Prerequisites.Select(p => p.Slice).Distinct().Order(StringComparer.Ordinal))
                if (!Visit(p))
                    return false;
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
            order.Add(id);
            return true;
        }

        foreach (var slice in slices.Values.OrderBy(s => s.YearFrom).ThenBy(s => s.Id, StringComparer.Ordinal))
            if (!Visit(slice.Id))
                break;

        return order;
    }

    private static T? ReadJson<T>(string file, List<string> problems) where T : class
    {
        try
        {
            using var stream = File.OpenRead(file);
            var value = JsonSerializer.Deserialize<T>(stream, Json);
            if (value is null)
                problems.Add($"{file}: пустой файл");
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            problems.Add($"{file}: не читается: {ex.Message}");
            return null;
        }
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
}
