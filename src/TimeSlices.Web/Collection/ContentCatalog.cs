namespace TimeSlices.Web.Collection;

/// <summary>
/// Коллекция в памяти: треки, срезы, примеры и DAG тематических срезов.
/// Строится один раз ContentLoader'ом; после построения не меняется.
/// </summary>
public sealed class ContentCatalog
{
    private readonly Dictionary<ThematicSliceKey, List<ThematicSliceKey>> _prerequisites;
    private readonly Dictionary<ThematicSliceKey, List<ThematicSliceKey>> _successors;
    private readonly List<(string Directory, Example Example, Implementation Implementation)> _implementationDirectories;

    // Linux различает регистр в путях, Windows и macOS по умолчанию — нет.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    internal ContentCatalog(
        string root,
        IReadOnlyList<Track> tracks,
        IReadOnlyDictionary<string, Slice> slices,
        IReadOnlyDictionary<string, Example> examples,
        IReadOnlyList<string> sliceOrder)
    {
        Root = root;
        Tracks = tracks.OrderBy(t => t.Order).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        Slices = slices;
        Examples = examples;

        // Тематические срезы в топологическом порядке: срезы по порядку DAG, внутри — треки среза.
        ThematicSlices = sliceOrder
            .SelectMany(id => slices[id].Tracks.Select(t => new ThematicSliceKey(id, t)))
            .ToList();

        _prerequisites = ThematicSlices.ToDictionary(
            k => k,
            k => slices[k.SliceId].Prerequisites.Select(p => p.Key).ToList());

        _successors = ThematicSlices.ToDictionary(k => k, _ => new List<ThematicSliceKey>());
        foreach (var (key, prereqs) in _prerequisites)
            foreach (var p in prereqs)
                _successors[p].Add(key);

        _implementationDirectories = examples.Values
            .SelectMany(e => e.Implementations.Select(i => (EnsureTrailingSeparator(Path.GetFullPath(i.Directory)), e, i)))
            .ToList();
    }

    /// <summary>Корневая папка содержания (Content/).</summary>
    public string Root { get; }

    public IReadOnlyList<Track> Tracks { get; }
    public IReadOnlyDictionary<string, Slice> Slices { get; }
    public IReadOnlyDictionary<string, Example> Examples { get; }

    /// <summary>Все тематические срезы; предшественники всегда раньше последующих.</summary>
    public IReadOnlyList<ThematicSliceKey> ThematicSlices { get; }

    public IReadOnlyList<ThematicSliceKey> PrerequisitesOf(ThematicSliceKey key) => _prerequisites[key];

    public IReadOnlyList<ThematicSliceKey> SuccessorsOf(ThematicSliceKey key) => _successors[key];

    public bool Contains(ThematicSliceKey key) => _prerequisites.ContainsKey(key);

    /// <summary>Подграф трека: его тематические срезы в топологическом порядке.</summary>
    public IEnumerable<ThematicSliceKey> SlicesInTrack(string trackId) =>
        ThematicSlices.Where(k => k.TrackId == trackId);

    /// <summary>Все последующие (транзитивно) тематические срезы.</summary>
    public IReadOnlyCollection<ThematicSliceKey> AllSuccessorsOf(ThematicSliceKey key)
    {
        var seen = new HashSet<ThematicSliceKey>();
        var stack = new Stack<ThematicSliceKey>(_successors[key]);
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (seen.Add(k))
                foreach (var s in _successors[k])
                    stack.Push(s);
        }
        return seen;
    }

    /// <summary>Примеры среза по возрастанию сложности (brief 7.1).</summary>
    public IEnumerable<Example> ExamplesOf(Slice slice) =>
        slice.Examples.Select(id => Examples[id]).OrderBy(e => e.Complexity).ThenBy(e => e.Id, StringComparer.Ordinal);

    /// <summary>
    /// Реализация (с её примером), статической сборке которой принадлежит файл, или null. Наружу
    /// отдаётся только то, что лежит внутри папок реализаций: questions.json и манифесты — нет.
    /// </summary>
    public (Example Example, Implementation Implementation)? FindServableFile(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        if (!File.Exists(path))
            return null;
        foreach (var (dir, example, implementation) in _implementationDirectories)
            if (path.StartsWith(dir, PathComparison))
                return (example, implementation);
        return null;
    }

    public bool IsServable(string fullPath) => FindServableFile(fullPath) is not null;

    /// <summary>Лежит ли путь внутри папки (после нормализации «..»).</summary>
    public static bool IsInside(string directory, string path) =>
        Path.GetFullPath(path).StartsWith(EnsureTrailingSeparator(Path.GetFullPath(directory)), PathComparison);

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
