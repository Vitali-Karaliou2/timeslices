using System.Text.Json;
using System.Text.RegularExpressions;

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
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*$");

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
        var questions = new Dictionary<string, IReadOnlyList<Question>>(StringComparer.Ordinal);
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
            if (LoadQuestions(root, dir, problems) is { } q)
                questions[slice.Id] = q;
        }

        foreach (var slice in slices.Values)
            CheckReferences(slice, trackIds, slices, examples, problems);
        foreach (var example in examples.Values)
            foreach (var impl in example.Implementations.Where(i => !slices.ContainsKey(i.Slice ?? "")))
                problems.Add($"пример '{example.Id}': срез реализации '{impl.Id}' — '{impl.Slice}' — не найден");

        var articles = LoadArticles(root, problems);
        CheckReferenceLinks(root, slices, examples, articles, problems);

        var order = problems.Count == 0 ? TopologicalOrder(slices, problems) : [];

        if (problems.Count > 0)
            throw new ContentLoadException(problems);

        return new ContentCatalog(root, tracks, slices, examples, questions, order, articles);
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
                // id реализации — имя папки; пути из манифеста не должны выходить за неё
                // (иначе наружу через /content/ ушли бы манифесты и questions.json).
                if (!IdPattern.IsMatch(impl.Id))
                {
                    problems.Add($"{Rel(root, file)}: id реализации '{impl.Id}' должен быть kebab-case (a-z, 0-9, -)");
                    continue;
                }
                impl.Directory = Path.Combine(dir, "implementations", impl.Id);
                foreach (var (what, path) in new[] { ("entry", impl.Entry ?? "") }
                             .Concat(impl.Processes.Select(pr => ("процесс", pr.File ?? "")))
                             .Concat(impl.CodeFiles.Select(c => ("код", c.Path ?? ""))))
                    if (!ContentCatalog.IsInside(impl.Directory, Path.Combine(impl.Directory, path)))
                        problems.Add($"{Rel(root, file)}: у реализации '{impl.Id}' путь ({what}) '{path}' выходит за папку реализации");

                if (string.IsNullOrEmpty(impl.Entry) || !File.Exists(Path.Combine(impl.Directory, impl.Entry)))
                    problems.Add($"{Rel(root, file)}: у реализации '{impl.Id}' не найден entry '{impl.Entry}'");

                foreach (var pr in impl.Processes)
                {
                    var pf = Path.Combine(impl.Directory, pr.File ?? "");
                    if (!ContentCatalog.IsInside(impl.Directory, pf))
                        continue;
                    if (!File.Exists(pf))
                        problems.Add($"{Rel(root, file)}: у реализации '{impl.Id}' не найден процесс '{pr.File}'");
                    else
                        pr.Process = ReadJson<Process>(pf, problems);
                }
            }
        }
    }

    /// <summary>
    /// Справка: Content/reference/&lt;article-id&gt;/article.json (ADR 0013). Поля подробно проверяет
    /// validate_manifest.py; здесь — id статьи и разделов, на которые опираются ссылки, и список «См. также».
    /// </summary>
    private static Dictionary<string, Article> LoadArticles(string root, List<string> problems)
    {
        var articles = new Dictionary<string, Article>(StringComparer.Ordinal);
        var referenceDir = Path.Combine(root, "reference");
        if (!Directory.Exists(referenceDir))
            return articles;

        foreach (var dir in Directory.GetDirectories(referenceDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var file = Path.Combine(dir, "article.json");
            if (!File.Exists(file))
            {
                problems.Add($"{Rel(root, dir)}: нет article.json");
                continue;
            }
            var article = ReadJson<Article>(file, problems);
            if (article is null)
                continue;
            if (article.Id != Path.GetFileName(dir))
                problems.Add($"{Rel(root, file)}: id '{article.Id}' не совпадает с именем папки");
            if (!articles.TryAdd(article.Id, article))
                problems.Add($"{Rel(root, file)}: статья '{article.Id}' объявлена дважды");
            var sectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in article.Sections.Where(s => !sectionIds.Add(s.Id)))
                problems.Add($"{Rel(root, file)}: раздел '{section.Id}' объявлен дважды");
        }

        foreach (var article in articles.Values)
            foreach (var see in article.See.Where(s => !articles.ContainsKey(s)))
                problems.Add($"reference/{article.Id}/article.json: в «См. также» статья '{see}' не найдена");
        return articles;
    }

    /// <summary>Ссылки на справку из срезов (refs) и стоп-кадров (detail.refs) ведут на существующие статьи и разделы.</summary>
    private static void CheckReferenceLinks(
        string root, Dictionary<string, Slice> slices, Dictionary<string, Example> examples,
        Dictionary<string, Article> articles, List<string> problems)
    {
        foreach (var slice in slices.Values)
            foreach (var r in slice.Refs)
                if (ContentCatalog.ResolveRef(articles, r, out var why) is null)
                    problems.Add($"slices/{slice.Id}/slice.json: ссылка на справку '{r}' — {why}");

        foreach (var example in examples.Values)
            foreach (var impl in example.Implementations)
                foreach (var pr in impl.Processes.Where(p => p.Process is not null))
                    foreach (var frame in pr.Process!.Frames.Where(f => f.Detail is not null))
                        foreach (var r in frame.Detail!.Refs)
                            if (ContentCatalog.ResolveRef(articles, r, out var why) is null)
                                problems.Add($"{Rel(root, Path.Combine(impl.Directory, pr.File))}: кадр {frame.Order}: ссылка на справку '{r}' — {why}");
    }

    /// <summary>
    /// questions.json среза (необязателен). Поля и минимум в пять вопросов проверяет validate_manifest.py;
    /// здесь — только то, на что опираются ответы в БД: непустой и уникальный id вопроса.
    /// </summary>
    private static List<Question>? LoadQuestions(string root, string sliceDir, List<string> problems)
    {
        var file = Path.Combine(sliceDir, "questions.json");
        if (!File.Exists(file))
            return null;
        var set = ReadJson<QuestionSet>(file, problems);
        if (set is null)
            return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in set.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Id) || q.Id.Length > 100)
                problems.Add($"{Rel(root, file)}: у вопроса пустой или слишком длинный id");
            else if (!ids.Add(q.Id))
                problems.Add($"{Rel(root, file)}: вопрос '{q.Id}' объявлен дважды");
        }
        return set.Questions;
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
