namespace TimeSlices.Web.Collection;

// Модель содержания коллекции. Поля повторяют docs/spec/manifest-schema.md;
// оболочка читает их из файлов при старте и не хранит в БД.

public sealed class Track
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public int Order { get; init; }
    public string? Description { get; init; }
}

public sealed class Prerequisite
{
    public string Slice { get; init; } = "";
    public string Track { get; init; } = "";
    public string? Why { get; init; }

    public ThematicSliceKey Key => new(Slice, Track);
}

public sealed class Slice
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public int YearFrom { get; init; }
    public int YearTo { get; init; }
    public List<string> Tracks { get; init; } = [];
    public string Introduces { get; init; } = "";
    public string Relevance { get; init; } = "";
    /// <summary>Пояснение к пометке актуальности (необязательное, ADR 0010).</summary>
    public string? RelevanceNote { get; init; }
    public List<Prerequisite> Prerequisites { get; init; } = [];
    public string LinkBack { get; init; } = "";
    public string LinkForward { get; init; } = "";
    public string WhyNeededToReviewModelCode { get; init; } = "";
    public List<string> Examples { get; init; } = [];
    public string Status { get; init; } = "";

    /// <summary>Замена подписей разделов страницы среза (необязательное, ADR 0013, п. 3).</summary>
    public SliceHeadings? Headings { get; init; }

    /// <summary>Ссылки на справку: «статья» или «статья#раздел» (необязательное, ADR 0013, п. 7).</summary>
    public List<string> Refs { get; init; } = [];
}

/// <summary>Подписи разделов среза; null — подпись по умолчанию (Pages/Labels).</summary>
public sealed class SliceHeadings
{
    public string? LinkBack { get; init; }
    public string? Introduces { get; init; }
    public string? LinkForward { get; init; }
    public string? Relevance { get; init; }
}

public sealed class Example
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Task { get; init; } = "";
    public string IntroducedInSlice { get; init; } = "";
    public int Complexity { get; init; }
    public List<Implementation> Implementations { get; init; } = [];

    /// <summary>Папка примера на диске (заполняет ContentLoader).</summary>
    public string Directory { get; set; } = "";
}

public sealed class Implementation
{
    public string Id { get; init; } = "";
    public string Slice { get; init; } = "";
    public string Stack { get; init; } = "";
    public string Entry { get; init; } = "";
    public string CodeMode { get; init; } = "";
    public List<CodeFile> CodeFiles { get; init; } = [];
    public List<ProcessRef> Processes { get; init; } = [];
    public AnachronismCheck? AnachronismCheck { get; init; }

    /// <summary>Папка реализации на диске (заполняет ContentLoader).</summary>
    public string Directory { get; set; } = "";
}

public sealed class CodeFile
{
    public string Path { get; init; } = "";
    public string? Note { get; init; }
}

public sealed class ProcessRef
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string File { get; init; } = "";

    /// <summary>Содержимое файла процесса (заполняет ContentLoader).</summary>
    public Process? Process { get; set; }
}

public sealed class AnachronismCheck
{
    public string By { get; init; } = "";
    public string? Date { get; init; }
    public string? Notes { get; init; }
}

public sealed class Process
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Variant { get; init; }
    public string? StartAt { get; init; }
    public List<Frame> Frames { get; init; } = [];
}

public sealed class Frame
{
    public int Order { get; init; }
    public string? At { get; init; }
    public string? DurationNote { get; init; }
    public string Text { get; init; } = "";
    public bool Stop { get; init; }
    public FrameDetail? Detail { get; init; }
}

public sealed class FrameDetail
{
    public string? CodeFile { get; init; }
    public string? Lines { get; init; }
    public string? Explanation { get; init; }

    /// <summary>Ссылки на справку из стоп-кадра (необязательное, ADR 0013, п. 7).</summary>
    public List<string> Refs { get; init; } = [];
}

/// <summary>Справочная статья: Content/reference/&lt;article-id&gt;/article.json (ADR 0013, п. 6).</summary>
public sealed class Article
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<ArticleSection> Sections { get; init; } = [];
    public List<string> See { get; init; } = [];
    public List<ArticleSource> Sources { get; init; } = [];
    public string Status { get; init; } = "";
}

public sealed class ArticleSection
{
    public string Id { get; init; } = "";
    public string Heading { get; init; } = "";
    /// <summary>Год, к которому относится раздел; null — «сейчас».</summary>
    public int? AsOf { get; init; }
    public List<string> Paragraphs { get; init; } = [];
}

public sealed class ArticleSource
{
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>Ссылка на справку, разобранная каталогом: статья, раздел (или null) и подпись ссылки.</summary>
public sealed record ReferenceTarget(Article Article, ArticleSection? Section)
{
    /// <summary>«Заголовок статьи» или «заголовок статьи: заголовок раздела».</summary>
    public string Label => DraftMark.Strip(Article.Title) + (Section is null ? "" : ": " + DraftMark.Strip(Section.Heading));
}

/// <summary>
/// Где упоминается статья: срез (поле refs) или стоп-кадр процесса (detail.refs). У кадра — пример,
/// реализация, процесс и номер кадра; срез — тот, к которому привязана реализация.
/// </summary>
public sealed record ReferenceMention(
    string SliceId, string? ExampleId = null, string? ImplementationId = null, string? ProcessId = null, int? FrameOrder = null)
{
    public bool IsFrame => FrameOrder is not null;
}

/// <summary>
/// Вопрос для контроля знаний (Content/slices/&lt;slice-id&gt;/questions.json). Эталонное обоснование
/// наружу не отдаётся, пока в попытке нет ответа на этот вопрос (ADR 0011, п. 3).
/// </summary>
public sealed class Question
{
    public string Id { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string ReferenceReasoning { get; init; } = "";
    public string FollowUpTemplate { get; init; } = "";
}

public sealed class QuestionSet
{
    public List<Question> Questions { get; init; } = [];
}

/// <summary>Тематический срез: срез × трек (brief 5.2). Единица состояния прохождения.</summary>
public readonly record struct ThematicSliceKey(string SliceId, string TrackId)
{
    public override string ToString() => $"{SliceId}@{TrackId}";
}
