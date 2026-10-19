namespace TimeSlices.Web.Data;

/// <summary>Статус попытки сдачи (ADR 0011, п. 4). Состояния сдачи тематического среза выводятся из него.</summary>
public enum AttemptStatus
{
    InProgress,
    Submitted,
    Confirmed,
    Returned,
}

/// <summary>Самооценка ответа по эталону (ADR 0011, п. 1).</summary>
public enum SelfRating
{
    Match,
    Partial,
    NoMatch,
}

/// <summary>Попытка сдачи тематического среза: пользователь × срез × трек.</summary>
public class Attempt
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string SliceId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; }
    public string? ExaminerId { get; set; }
    public string? ExaminerComment { get; set; }
    public DateTime? DecidedAt { get; set; }

    public List<Answer> Answers { get; set; } = [];
}

/// <summary>
/// Ответ на вопрос из questions.json (по QuestionId; сам вопрос и эталон в БД не копируются).
/// ShownAt, AnsweredAt, ActiveMs, PasteCount и их пары для продолжения — признаки (brief 8.2):
/// обучаемому не показываются, видны экзаменатору.
/// </summary>
public class Answer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public string QuestionId { get; set; } = "";
    public string Text { get; set; } = "";
    public string? FollowUpText { get; set; }
    public SelfRating? SelfRating { get; set; }
    public DateTime ShownAt { get; set; }
    public DateTime AnsweredAt { get; set; }
    public int ActiveMs { get; set; }
    public int PasteCount { get; set; }
    public int? FollowUpActiveMs { get; set; }
    public int? FollowUpPasteCount { get; set; }
}
