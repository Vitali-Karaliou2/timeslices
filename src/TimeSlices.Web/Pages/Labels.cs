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

    public static string Relevance(string code) => code switch
    {
        "a" => "(а) используется и в коде современных моделей",
        "b" => "(б) вытеснено, но без него непонятны последующие",
        "c" => "(в) исторично, для контекста",
        _ => code,
    };

    public static string SliceStatusMark(string status) => status switch
    {
        "draft" => "черновик",
        "reviewed" => "проверен на анахронизмы, не утверждён",
        "approved" => "утверждён",
        _ => status,
    };
}
