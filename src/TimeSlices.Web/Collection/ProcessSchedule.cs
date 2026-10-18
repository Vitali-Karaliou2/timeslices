using System.Globalization;

namespace TimeSlices.Web.Collection;

/// <summary>
/// Расписание проигрывания процесса (brief 7.3): кадры по порядку, реальный промежуток до следующего
/// кадра по <c>at</c> и экранная задержка, в которую он сжат. Считает сервер; проигрыватель на странице
/// (wwwroot/js/process-player.js) только идёт по шагам и делит задержку на выбранную скорость.
/// </summary>
public static class ProcessSchedule
{
    /// <summary>Самый короткий шаг на экране при скорости ×1: кадры без времени или с одинаковым временем.</summary>
    public const int MinDelayMs = 600;

    /// <summary>Самый длинный шаг на экране при скорости ×1, сколько бы ни длился промежуток в сюжете.</summary>
    public const int MaxDelayMs = 3000;

    /// <summary>Промежутки длиннее этого получают подпись «прошло …».</summary>
    public static readonly TimeSpan LabelThreshold = TimeSpan.FromMinutes(1);

    public static List<ProcessStep> Build(Process process)
    {
        var frames = process.Frames.OrderBy(f => f.Order).ToList();
        var steps = new List<ProcessStep>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var at = ParseAt(frame.At);
            // Промежуток считается только между соседними кадрами со временем: кадр без at «разрывает» отсчёт,
            // и оба шага вокруг него идут с минимальной задержкой.
            TimeSpan? gap = null;
            if (i + 1 < frames.Count && at is { } from && ParseAt(frames[i + 1].At) is { } to)
                gap = to > from ? to - from : TimeSpan.Zero;

            steps.Add(new ProcessStep(
                Order: frame.Order,
                At: frame.At,
                AtDisplay: at is { } a ? FormatAt(a) : null,
                // Префикс «ЧЕРНОВИК:» убирается из текстов кадра; вместо него у кадра один значок (DraftMark).
                Text: DraftMark.Strip(frame.Text),
                DurationNote: DraftMark.StripOrNull(frame.DurationNote),
                Draft: DraftMark.AnyDraft(frame.Text, frame.DurationNote, frame.Stop ? frame.Detail?.Explanation : null),
                Stop: frame.Stop,
                GapSeconds: gap?.TotalSeconds,
                // У последнего кадра нет следующего — ждать нечего.
                DelayMs: i + 1 < frames.Count ? DelayFor(gap) : 0,
                GapLabel: gap is { } g && g > LabelThreshold ? GapLabel(g) : null,
                Detail: frame.Stop && frame.Detail is { } d ? StopDetail(d) : null));
        }
        return steps;
    }

    /// <summary>
    /// Экранная задержка при скорости ×1. Сжатие логарифмическое: 0 с → 0,6 с, 1 с → 1,2 с, 3 с → 1,8 с,
    /// 7 с → 2,4 с, от 15 с и дольше (в том числе часы ожидания) → 3 с. Порядок промежутков сохраняется,
    /// а многочасовое ожидание проигрывается за те же 3 с.
    /// </summary>
    public static int DelayFor(TimeSpan? gap)
    {
        if (gap is not { } g || g <= TimeSpan.Zero)
            return MinDelayMs;
        var ms = MinDelayMs * (1 + Math.Log2(1 + g.TotalSeconds));
        return (int)Math.Round(Math.Min(ms, MaxDelayMs));
    }

    /// <summary>«прошло 3 ч 20 мин»; секунды — только у промежутков не длиннее часа.</summary>
    public static string GapLabel(TimeSpan gap)
    {
        var parts = new List<string>();
        if (gap.Days > 0) parts.Add($"{gap.Days} сут");
        if (gap.Hours > 0) parts.Add($"{gap.Hours} ч");
        if (gap.Minutes > 0) parts.Add($"{gap.Minutes} мин");
        if (gap <= TimeSpan.FromHours(1) && gap.Seconds > 0) parts.Add($"{gap.Seconds} с");
        return "прошло " + string.Join(' ', parts);
    }

    /// <summary>Строки стоп-кадра: «12-20» или «32»; что-то другое — без диапазона.</summary>
    public static (int From, int To)? ParseLines(string? lines)
    {
        if (string.IsNullOrWhiteSpace(lines))
            return null;
        var parts = lines.Split('-');
        if (parts.Length > 2 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return null;
        var from = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var to = int.Parse(parts[^1], CultureInfo.InvariantCulture);
        return from >= 1 && to >= from ? (from, to) : null;
    }

    private static StopDetail StopDetail(FrameDetail d)
    {
        var range = ParseLines(d.Lines);
        return new StopDetail(d.CodeFile, d.Lines, range?.From, range?.To, DraftMark.StripOrNull(d.Explanation));
    }

    // RoundtripKind: время с Z или смещением не переводится в часовой пояс сервера (сейчас все at — без зоны).
    private static DateTime? ParseAt(string? at) =>
        DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    // Названия месяцев заданы здесь, а не взяты из культуры ru-RU: оболочка не зависит от ICU на сервере.
    private static readonly string[] MonthsGenitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    /// <summary>«5 октября 1992, 14:00:03».</summary>
    public static string FormatAt(DateTime at) =>
        $"{at.Day} {MonthsGenitive[at.Month - 1]} {at.Year}, {at.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}";
}

/// <summary>Шаг проигрывания: кадр и задержка до следующего кадра.</summary>
/// <param name="GapSeconds">Реальный промежуток до следующего кадра в сюжете; null — у кадра или у следующего нет времени, либо кадр последний.</param>
/// <param name="DelayMs">Экранная задержка до следующего кадра при скорости ×1.</param>
/// <param name="GapLabel">«прошло 3 ч 20 мин» для промежутков длиннее минуты.</param>
/// <param name="Draft">Хотя бы один текст кадра помечен «ЧЕРНОВИК:»; сами тексты — уже без префикса.</param>
public sealed record ProcessStep(
    int Order, string? At, string? AtDisplay, string Text, string? DurationNote, bool Draft, bool Stop,
    double? GapSeconds, int DelayMs, string? GapLabel, StopDetail? Detail);

/// <summary>Что показать на стоп-кадре: пояснение и строки файла кода для показа.</summary>
public sealed record StopDetail(string? CodeFile, string? Lines, int? LineFrom, int? LineTo, string? Explanation);
