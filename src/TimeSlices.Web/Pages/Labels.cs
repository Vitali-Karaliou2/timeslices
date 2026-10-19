using System.Globalization;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages;

/// <summary>Подписи состояний и пометок актуальности (brief 5.3, 7.1).</summary>
public static class Labels
{
    public static string State(SliceStatus status) => status.State switch
    {
        SliceState.Unavailable => "Недоступен",
        SliceState.Available => "Доступен",
        SliceState.InProgress => status.IsRepeat ? "В процессе (повторно)" : "В процессе",
        SliceState.Done => "Пройден",
        _ => status.State.ToString(),
    };

    /// <summary>Состояние сдачи по последней попытке (ADR 0011, п. 4).</summary>
    public static string Submission(AttemptStatus status) => status switch
    {
        AttemptStatus.InProgress => "в процессе сдачи",
        AttemptStatus.Submitted => "ожидает подтверждения",
        AttemptStatus.Confirmed => "подтверждён экзаменатором",
        AttemptStatus.Returned => "возвращён",
        _ => status.ToString(),
    };

    public static string Rating(SelfRating? rating) => rating switch
    {
        SelfRating.Match => "совпал",
        SelfRating.Partial => "частично",
        SelfRating.NoMatch => "не совпал",
        null => "—",
        _ => rating.ToString()!,
    };

    /// <summary>Длительность для экзаменатора: «48,2 с», «3 мин 05 с».</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero)
            d = TimeSpan.Zero;
        return d.TotalMinutes < 1
            ? (d.TotalMilliseconds / 1000).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " с"
            : $"{(int)d.TotalMinutes} мин {d.Seconds:00} с";
    }

    /// <summary>Дата и время в UTC — так они хранятся: «19 октября 2026, 14:05:09 UTC».</summary>
    public static string When(DateTime? utc) => utc is { } t ? ProcessSchedule.FormatAt(t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond))) + " UTC" : "—";

    public static string Relevance(string code) => code switch
    {
        "a" => "(а) используется и в коде современных моделей",
        "b" => "(б) вытеснено, но без него непонятны последующие",
        "c" => "(в) исторично, для контекста",
        _ => code,
    };

    // Подписи разделов страницы среза по умолчанию (ADR 0013, п. 2); срез может заменить их полем headings.
    public const string LinkBackHeading = "Что было до";
    public const string IntroducesHeading = "Что меняется";
    public const string LinkForwardHeading = "Что будет после";
    public const string RelevanceHeading = "Как это связано с сегодняшним днём?";
    public const string ExamplesHeading = "Примеры";

    /// <summary>Пометка раздела справочной статьи: «к 1992 году» или «сейчас» (ADR 0013, п. 6).</summary>
    public static string AsOf(int? year) => year is { } y ? $"к {y} году" : "сейчас";

    public static string SliceStatusMark(string status) => status switch
    {
        "draft" => "черновик",
        "reviewed" => "проверен на анахронизмы, не утверждён",
        "approved" => "утверждён",
        _ => status,
    };
}
