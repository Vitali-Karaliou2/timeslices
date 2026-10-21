namespace TimeSlices.Web.Data;

/// <summary>
/// Состояние среза (brief 5.3), сессия 01 — первые четыре.
/// Unavailable и Available вычисляются по графу и в БД не хранятся.
/// </summary>
public enum SliceState
{
    Unavailable = 0,
    Available = 1,
    InProgress = 2,
    Done = 3,
}

/// <summary>
/// Состояние прохождения: пользователь × срез (ADR 0014, п. В). Срез, пройденный в одном треке,
/// пройден во всех своих треках; тематический срез — единица навигации, а не учёта.
/// </summary>
public class UserSliceState
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string SliceId { get; set; } = "";
    public SliceState State { get; set; }

    /// <summary>Повторное прохождение без сброса: для последующих срез считается пройденным.</summary>
    public bool IsRepeat { get; set; }

    public DateTime UpdatedAt { get; set; }
}
