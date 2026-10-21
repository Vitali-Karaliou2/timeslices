namespace TimeSlices.Web.Collection;

/// <summary>
/// Коллекция в памяти: треки, срезы, примеры и DAG тематических срезов.
/// Строится один раз ContentLoader'ом; после построения не меняется.
/// </summary>
public sealed class ContentCatalog
{
    private readonly Dictionary<ThematicSliceKey, List<ThematicSliceKey>> _prerequisites;
    private readonly Dictionary<ThematicSliceKey, List<ThematicSliceKey>> _successors;
    private readonly Dictionary<string, List<string>> _sliceSuccessors;
    private readonly List<(string Directory, Example Example, Implementation Implementation)> _implementationDirectories;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Question>> _questions;
    private readonly Dictionary<string, List<ReferenceMention>> _mentions;

    // Linux различает регистр в путях, Windows и macOS по умолчанию — нет.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    internal ContentCatalog(
        string root,
        IReadOnlyList<Track> tracks,
        IReadOnlyDictionary<string, Slice> slices,
        IReadOnlyDictionary<string, Example> examples,
        IReadOnlyDictionary<string, IReadOnlyList<Question>> questions,
        IReadOnlyList<string> sliceOrder,
        IReadOnlyDictionary<string, Article>? articles = null)
    {
        _questions = questions;
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

        // Граф срезов без треков: единица учёта прохождения — срез (ADR 0014, п. В).
        SliceOrder = sliceOrder;
        _sliceSuccessors = sliceOrder.ToDictionary(id => id, _ => new List<string>());
        foreach (var id in sliceOrder)
            foreach (var p in PrerequisiteSlicesOf(id))
                _sliceSuccessors[p].Add(id);

        Articles = articles ?? new Dictionary<string, Article>(StringComparer.Ordinal);
        _mentions = BuildMentions(slices, examples, Articles);

        _implementationDirectories = examples.Values
            .SelectMany(e => e.Implementations.Select(i => (EnsureTrailingSeparator(Path.GetFullPath(i.Directory)), e, i)))
            .ToList();
    }

    /// <summary>Корневая папка содержания (Content/).</summary>
    public string Root { get; }

    public IReadOnlyList<Track> Tracks { get; }
    public IReadOnlyDictionary<string, Slice> Slices { get; }
    public IReadOnlyDictionary<string, Example> Examples { get; }

    /// <summary>Справочные статьи по id (ADR 0013).</summary>
    public IReadOnlyDictionary<string, Article> Articles { get; }

    /// <summary>Справка по алфавиту заголовков.</summary>
    public IEnumerable<Article> ArticlesByTitle =>
        Articles.Values.OrderBy(a => DraftMark.Strip(a.Title), StringComparer.CurrentCulture).ThenBy(a => a.Id, StringComparer.Ordinal);

    /// <summary>Ссылка «статья» или «статья#раздел»; null — статьи или раздела нет (на загруженном каталоге не бывает).</summary>
    public ReferenceTarget? ResolveRef(string reference) => ResolveRef(Articles, reference, out _);

    /// <summary>Где упоминается статья: сначала срезы, потом кадры.</summary>
    public IReadOnlyList<ReferenceMention> MentionsOf(string articleId) =>
        _mentions.TryGetValue(articleId, out var m) ? m : [];

    internal static ReferenceTarget? ResolveRef(IReadOnlyDictionary<string, Article> articles, string reference, out string why)
    {
        var parts = (reference ?? "").Split('#');
        if (parts.Length > 2 || parts[0].Length == 0 || parts.Length == 2 && parts[1].Length == 0)
        {
            why = "ожидается 'статья' или 'статья#раздел'";
            return null;
        }
        if (!articles.TryGetValue(parts[0], out var article))
        {
            why = $"статья '{parts[0]}' не найдена (нет reference/{parts[0]}/article.json)";
            return null;
        }
        why = "";
        if (parts.Length == 1)
            return new ReferenceTarget(article, null);
        if (article.Sections.FirstOrDefault(s => s.Id == parts[1]) is { } section)
            return new ReferenceTarget(article, section);
        why = $"в статье '{article.Id}' нет раздела '{parts[1]}'";
        return null;
    }

    private static Dictionary<string, List<ReferenceMention>> BuildMentions(
        IReadOnlyDictionary<string, Slice> slices, IReadOnlyDictionary<string, Example> examples,
        IReadOnlyDictionary<string, Article> articles)
    {
        var mentions = new Dictionary<string, List<ReferenceMention>>(StringComparer.Ordinal);
        void Add(string reference, ReferenceMention mention)
        {
            if (ResolveRef(articles, reference, out _) is not { } target)
                return;
            var list = mentions.TryGetValue(target.Article.Id, out var l) ? l : mentions[target.Article.Id] = [];
            if (!list.Contains(mention))
                list.Add(mention);
        }

        foreach (var slice in slices.Values.OrderBy(s => s.YearFrom).ThenBy(s => s.Id, StringComparer.Ordinal))
            foreach (var r in slice.Refs)
                Add(r, new ReferenceMention(slice.Id));
        foreach (var example in examples.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
            foreach (var impl in example.Implementations)
                foreach (var pr in impl.Processes.Where(p => p.Process is not null))
                    foreach (var frame in pr.Process!.Frames.Where(f => f.Detail is not null).OrderBy(f => f.Order))
                        foreach (var r in frame.Detail!.Refs)
                            Add(r, new ReferenceMention(impl.Slice, example.Id, impl.Id, pr.Id, frame.Order));
        return mentions;
    }

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

    /// <summary>Все срезы в топологическом порядке: предшественники раньше последующих.</summary>
    public IReadOnlyList<string> SliceOrder { get; }

    /// <summary>Срезы-предшественники без учёта трека ребра: трек в prerequisites — только подпись (ADR 0014, п. В.2).</summary>
    public IReadOnlyList<string> PrerequisiteSlicesOf(string sliceId) =>
        Slices[sliceId].Prerequisites.Select(p => p.Slice).Distinct().ToList();

    /// <summary>Непосредственные срезы-последующие.</summary>
    public IReadOnlyList<string> SuccessorSlicesOf(string sliceId) => _sliceSuccessors[sliceId];

    /// <summary>Все последующие (транзитивно) срезы.</summary>
    public IReadOnlyCollection<string> AllSuccessorSlicesOf(string sliceId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(_sliceSuccessors[sliceId]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (seen.Add(id))
                foreach (var s in _sliceSuccessors[id])
                    stack.Push(s);
        }
        return seen;
    }

    /// <summary>Примеры среза по возрастанию сложности (brief 7.1).</summary>
    public IEnumerable<Example> ExamplesOf(Slice slice) =>
        slice.Examples.Select(id => Examples[id]).OrderBy(e => e.Complexity).ThenBy(e => e.Id, StringComparer.Ordinal);

    /// <summary>Вопросы среза в порядке questions.json; у среза без файла — пусто.</summary>
    public IReadOnlyList<Question> QuestionsOf(string sliceId) =>
        _questions.TryGetValue(sliceId, out var q) ? q : [];

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
