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
    public List<Prerequisite> Prerequisites { get; init; } = [];
    public string LinkBack { get; init; } = "";
    public string LinkForward { get; init; } = "";
    public string WhyNeededToReviewModelCode { get; init; } = "";
    public List<string> Examples { get; init; } = [];
    public string Status { get; init; } = "";
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
}

/// <summary>Тематический срез: срез × трек (brief 5.2). Единица состояния прохождения.</summary>
public readonly record struct ThematicSliceKey(string SliceId, string TrackId)
{
    public override string ToString() => $"{SliceId}@{TrackId}";
}
