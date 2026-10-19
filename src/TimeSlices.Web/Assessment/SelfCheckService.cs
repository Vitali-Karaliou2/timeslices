using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Assessment;

/// <summary>Чем закончилось действие с попыткой; страница переводит это в HTTP-ответ.</summary>
public enum SelfCheckOutcome
{
    Ok,
    /// <summary>Нет попытки или вопроса.</summary>
    NotFound,
    /// <summary>Срез закрыт для сдачи: не «в процессе» и не «пройден», или у него нет вопросов.</summary>
    Forbidden,
    /// <summary>Действие не подходит к текущему шагу: ответ уже сохранён, попытка уже отправлена и т. п.</summary>
    Conflict,
    /// <summary>Пустой или слишком длинный текст, подделанная отметка показа, нет комментария при возврате.</summary>
    Invalid,
}

/// <summary>Шаг вопроса в попытке: ответ → эталон и продолжение → самооценка (ADR 0011, п. 1).</summary>
public enum QuestionStep
{
    Answer,
    FollowUp,
    Rating,
    Complete,
}

/// <summary>
/// Самопроверка по эталону и демо-экзаменатор (ADR 0011). Вопросы и эталоны читаются из каталога
/// по QuestionId; в БД — только попытки и ответы. Признаки (время, активное время, вставки)
/// сохраняются, но на доступ и состояния не влияют.
/// </summary>
public class SelfCheckService(
    ApplicationDbContext db, ContentCatalog catalog, SliceProgressService progress,
    IDataProtectionProvider protection, TimeProvider clock)
{
    public const int MaxAnswerLength = 10_000;
    public const int MaxCommentLength = 4_000;

    // Отметка времени показа вопроса едет в форме зашифрованной: сервер ставит ShownAt, клиент не может
    // её ни прочесть (признаки обучаемому не показываются), ни подделать.
    private readonly IDataProtector _shownAt = protection.CreateProtector("TimeSlices.SelfCheck.ShownAt");

    /// <summary>Сдавать можно срез «в процессе» или «пройден» (план сессии 06, п. 3), если у него есть вопросы.</summary>
    public async Task<bool> CanCheckAsync(string userId, ThematicSliceKey key) =>
        catalog.QuestionsOf(key.SliceId).Count > 0
        && (await progress.GetStatusAsync(userId, key)).State is SliceState.InProgress or SliceState.Done;

    /// <summary>
    /// Последняя попытка по тематическому срезу (с ответами) или null. Без отслеживания:
    /// шаги пишутся условными UPDATE, и отслеживаемая копия устарела бы.
    /// </summary>
    public Task<Attempt?> LatestAsync(string userId, ThematicSliceKey key) =>
        db.Attempts.AsNoTracking().Include(a => a.Answers)
            .Where(a => a.UserId == userId && a.SliceId == key.SliceId && a.TrackId == key.TrackId)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

    /// <summary>Последние попытки пользователя по всем тематическим срезам (для главной).</summary>
    public async Task<IReadOnlyDictionary<ThematicSliceKey, Attempt>> LatestByKeyAsync(string userId)
    {
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        return attempts.GroupBy(a => new ThematicSliceKey(a.SliceId, a.TrackId))
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Id)!);
    }

    public static QuestionStep StepOf(Answer? answer) => answer switch
    {
        null => QuestionStep.Answer,
        { FollowUpText: null } => QuestionStep.FollowUp,
        { SelfRating: null } => QuestionStep.Rating,
        _ => QuestionStep.Complete,
    };

    /// <summary>Первый в порядке questions.json вопрос, по которому не пройдены все шаги; null — все пройдены.</summary>
    public Question? CurrentQuestion(Attempt attempt) =>
        catalog.QuestionsOf(attempt.SliceId)
            .FirstOrDefault(q => StepOf(attempt.Answers.FirstOrDefault(a => a.QuestionId == q.Id)) != QuestionStep.Complete);

    /// <summary>Начать попытку: если попыток нет или последняя возвращена экзаменатором.</summary>
    public async Task<SelfCheckOutcome> StartAsync(string userId, ThematicSliceKey key)
    {
        if (!await CanCheckAsync(userId, key))
            return SelfCheckOutcome.Forbidden;
        var latest = await LatestAsync(userId, key);
        if (latest is not null && latest.Status != AttemptStatus.Returned)
            return SelfCheckOutcome.Conflict;
        db.Attempts.Add(new Attempt
        {
            UserId = userId, SliceId = key.SliceId, TrackId = key.TrackId,
            StartedAt = Now, Status = AttemptStatus.InProgress,
        });
        return await SaveAsync();
    }

    /// <summary>Отметка показа вопроса для скрытого поля формы ответа.</summary>
    public string IssueShownAt(Attempt attempt, Question question) =>
        _shownAt.Protect(string.Create(CultureInfo.InvariantCulture, $"{attempt.Id}|{question.Id}|{Now.Ticks}"));

    /// <summary>Отметка показа выдана этим сервером на этот вопрос этой попытки.</summary>
    public bool IsValidShownAt(string? token, Attempt attempt, string questionId) =>
        ReadShownAt(token, attempt.Id, questionId) is not null;

    /// <summary>
    /// Вопрос-продолжение по ответу: в followUpTemplate вместо {answer} — первые 300 символов ответа.
    /// Результат — простой текст; Razor экранирует его при выводе (ответ обучаемого — недоверенный ввод).
    /// </summary>
    public static string FollowUpText(Question question, string answer)
    {
        const int Max = 300;
        var excerpt = answer;
        if (excerpt.Length > Max)
        {
            var cut = char.IsHighSurrogate(excerpt[Max - 1]) ? Max - 1 : Max; // не разрывать суррогатную пару
            excerpt = excerpt[..cut] + "…";
        }
        return DraftMark.Strip(question.FollowUpTemplate).Replace("{answer}", excerpt, StringComparison.Ordinal);
    }

    /// <summary>Основной ответ на текущий вопрос. Сохранённый ответ не меняется.</summary>
    public async Task<SelfCheckOutcome> AnswerAsync(
        string userId, ThematicSliceKey key, string questionId, string? text, string? shownAtToken, int activeMs, int pasteCount)
    {
        var (outcome, attempt) = await OpenAttemptAsync(userId, key);
        if (attempt is null)
            return outcome;
        if (CurrentQuestion(attempt)?.Id != questionId || attempt.Answers.Any(a => a.QuestionId == questionId))
            return SelfCheckOutcome.Conflict;
        if (Clean(text) is not { } answer || ReadShownAt(shownAtToken, attempt.Id, questionId) is not { } shownAt)
            return SelfCheckOutcome.Invalid;

        var now = Now;
        db.Answers.Add(new Answer
        {
            AttemptId = attempt.Id, QuestionId = questionId, Text = answer, ShownAt = shownAt, AnsweredAt = now,
            ActiveMs = ClampActive(activeMs, now - shownAt), PasteCount = ClampCount(pasteCount),
        });
        return await SaveAsync();
    }

    /// <summary>Ответ на вопрос-продолжение (после показа эталона).</summary>
    public async Task<SelfCheckOutcome> FollowUpAsync(
        string userId, ThematicSliceKey key, string questionId, string? text, int activeMs, int pasteCount)
    {
        var (outcome, attempt) = await OpenAttemptAsync(userId, key);
        if (attempt is null)
            return outcome;
        var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (StepOf(answer) != QuestionStep.FollowUp)
            return SelfCheckOutcome.Conflict;
        if (Clean(text) is not { } followUp)
            return SelfCheckOutcome.Invalid;

        // Время показа продолжения схема ADR 0011 не хранит; верхняя граница активного времени — от сохранения ответа.
        var active = ClampActive(activeMs, Now - answer!.AnsweredAt);
        var pastes = ClampCount(pasteCount);
        // Условие в самом UPDATE: параллельный запрос не перезапишет уже сохранённое продолжение.
        return Affected(await db.Answers.Where(a => a.Id == answer.Id && a.FollowUpText == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.FollowUpText, followUp)
                .SetProperty(a => a.FollowUpActiveMs, active)
                .SetProperty(a => a.FollowUpPasteCount, pastes)));
    }

    /// <summary>Самооценка по эталону — последний шаг вопроса.</summary>
    public async Task<SelfCheckOutcome> RateAsync(string userId, ThematicSliceKey key, string questionId, SelfRating rating)
    {
        var (outcome, attempt) = await OpenAttemptAsync(userId, key);
        if (attempt is null)
            return outcome;
        if (!Enum.IsDefined(rating))
            return SelfCheckOutcome.Invalid;
        var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (StepOf(answer) != QuestionStep.Rating)
            return SelfCheckOutcome.Conflict;
        return Affected(await db.Answers.Where(a => a.Id == answer!.Id && a.FollowUpText != null && a.SelfRating == null)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SelfRating, rating)));
    }

    /// <summary>«Отправить на подтверждение» — когда по всем вопросам пройдены все шаги.</summary>
    public async Task<SelfCheckOutcome> SubmitAsync(string userId, ThematicSliceKey key)
    {
        var (outcome, attempt) = await OpenAttemptAsync(userId, key);
        if (attempt is null)
            return outcome;
        if (CurrentQuestion(attempt) is not null)
            return SelfCheckOutcome.Conflict;
        var now = Now;
        return Affected(await db.Attempts.Where(a => a.Id == attempt.Id && a.Status == AttemptStatus.InProgress)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Status, AttemptStatus.Submitted)
                .SetProperty(a => a.SubmittedAt, now)));
    }

    /// <summary>Отправленные и решённые попытки для экзаменатора: сначала ожидающие, затем по дате отправки.</summary>
    public async Task<IReadOnlyList<Attempt>> SubmittedAttemptsAsync()
    {
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.Status != AttemptStatus.InProgress).ToListAsync();
        return attempts.OrderBy(a => a.Status == AttemptStatus.Submitted ? 0 : 1)
            .ThenByDescending(a => a.SubmittedAt).ToList();
    }

    /// <summary>Отправленная (или уже решённая) попытка с ответами; незавершённая экзаменатору не видна.</summary>
    public Task<Attempt?> SubmittedAttemptAsync(int id) =>
        db.Attempts.AsNoTracking().Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == id && a.Status != AttemptStatus.InProgress);

    public Task<SelfCheckOutcome> ConfirmAsync(int attemptId, string examinerId) =>
        DecideAsync(attemptId, examinerId, AttemptStatus.Confirmed, comment: null);

    /// <summary>Вернуть попытку; комментарий обязателен (ADR 0011, п. 5).</summary>
    public Task<SelfCheckOutcome> ReturnAsync(int attemptId, string examinerId, string? comment) =>
        DecideAsync(attemptId, examinerId, AttemptStatus.Returned, comment);

    private async Task<SelfCheckOutcome> DecideAsync(int attemptId, string examinerId, AttemptStatus status, string? comment)
    {
        var attempt = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId);
        if (attempt is null || attempt.Status == AttemptStatus.InProgress)
            return SelfCheckOutcome.NotFound;
        if (attempt.Status != AttemptStatus.Submitted)
            return SelfCheckOutcome.Conflict;
        comment = comment?.Trim();
        if (status == AttemptStatus.Returned && (string.IsNullOrEmpty(comment) || comment.Length > MaxCommentLength))
            return SelfCheckOutcome.Invalid;
        var stored = string.IsNullOrEmpty(comment) ? null : comment;
        var now = Now;
        // Два экзаменатора одновременно: решение принимает первый, второй получает Conflict.
        return Affected(await db.Attempts.Where(a => a.Id == attemptId && a.Status == AttemptStatus.Submitted)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.ExaminerId, examinerId)
                .SetProperty(a => a.ExaminerComment, stored)
                .SetProperty(a => a.DecidedAt, now)));
    }

    /// <summary>Незавершённая попытка пользователя по срезу, открытому для сдачи.</summary>
    private async Task<(SelfCheckOutcome, Attempt?)> OpenAttemptAsync(string userId, ThematicSliceKey key)
    {
        if (!await CanCheckAsync(userId, key))
            return (SelfCheckOutcome.Forbidden, null);
        var attempt = await LatestAsync(userId, key);
        if (attempt is null)
            return (SelfCheckOutcome.NotFound, null);
        return attempt.Status == AttemptStatus.InProgress ? (SelfCheckOutcome.Ok, attempt) : (SelfCheckOutcome.Conflict, null);
    }

    private DateTime? ReadShownAt(string? token, int attemptId, string questionId)
    {
        if (string.IsNullOrEmpty(token))
            return null;
        string payload;
        try
        {
            payload = _shownAt.Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        var parts = payload.Split('|');
        if (parts.Length != 3 || parts[0] != attemptId.ToString(CultureInfo.InvariantCulture) || parts[1] != questionId
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            return null;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static string? Clean(string? text)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxAnswerLength ? null : text;
    }

    // Признаки считает браузер; сервер только не даёт им выйти за правдоподобные пределы.
    private static int ClampActive(int activeMs, TimeSpan elapsed) =>
        (int)Math.Clamp(activeMs, 0, Math.Min(int.MaxValue, Math.Max(0, elapsed.TotalMilliseconds)));

    private static int ClampCount(int count) => Math.Clamp(count, 0, 100_000);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static SelfCheckOutcome Affected(int rows) => rows == 1 ? SelfCheckOutcome.Ok : SelfCheckOutcome.Conflict;

    private async Task<SelfCheckOutcome> SaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return SelfCheckOutcome.Ok;
        }
        catch (DbUpdateException)
        {
            // Двойное нажатие: параллельный запрос уже создал попытку или сохранил этот ответ (уникальные индексы).
            db.ChangeTracker.Clear();
            return SelfCheckOutcome.Conflict;
        }
    }
}
