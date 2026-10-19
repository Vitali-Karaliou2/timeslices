using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Slices;

/// <summary>
/// Контроль знаний по срезу (ADR 0011): вопросы по одному, ответ → эталон и продолжение → самооценка,
/// затем «Отправить на подтверждение». Эталон показывается только по вопросам, на которые в этой
/// попытке уже сохранён ответ. Признаки (время, вставки) на этой странице не показываются никогда.
/// </summary>
public class CheckModel(ContentCatalog catalog, SliceProgressService progress, SelfCheckService selfCheck, UserManager<ApplicationUser> users) : PageModel
{
    public Slice Slice { get; private set; } = null!;
    public ThematicSliceKey Key { get; private set; }
    public IReadOnlyList<Question> Questions { get; private set; } = [];
    public SliceStatus Status { get; private set; }

    /// <summary>Сдача закрыта: срез не «в процессе» и не «пройден».</summary>
    public bool Locked { get; private set; }

    /// <summary>Последняя попытка (или null — ещё не начинали).</summary>
    public Attempt? Attempt { get; private set; }

    /// <summary>Индекс первого незавершённого вопроса; Questions.Count — все пройдены.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Вопрос, показанный в незавершённой попытке.</summary>
    public int SelectedIndex { get; private set; }

    public string? ShownAtToken { get; private set; }
    public string? Error { get; private set; }
    public string? DraftText { get; private set; }

    public Question? Selected => Attempt?.Status == AttemptStatus.InProgress && SelectedIndex < Questions.Count ? Questions[SelectedIndex] : null;
    public Answer? AnswerTo(Question q) => Attempt?.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
    public bool CanStart => Attempt is null || Attempt.Status == AttemptStatus.Returned;

    private string UserId => users.GetUserId(User)!;

    public async Task<IActionResult> OnGetAsync(string id, string? track, string? q) =>
        await LoadAsync(id, track, q) ?? Page();

    public async Task<IActionResult> OnPostStartAsync(string id, string? track) =>
        Result(await ResolvedAsync(id, track, () => selfCheck.StartAsync(UserId, Key)), id, null);

    public async Task<IActionResult> OnPostAnswerAsync(string id, string? track, string questionId, string? text, string? shownAt, int activeMs, int pasteCount)
    {
        var outcome = await ResolvedAsync(id, track, () => selfCheck.AnswerAsync(UserId, Key, questionId, text, shownAt, activeMs, pasteCount));
        if (outcome != SelfCheckOutcome.Invalid)
            return Result(outcome, id, questionId);
        // Пустой ответ: та же форма с сообщением; отметка показа прежняя, если она подлинная.
        var page = await LoadAsync(id, track, questionId);
        if (page is not null)
            return page;
        if (Attempt is not null && Selected?.Id == questionId && selfCheck.IsValidShownAt(shownAt, Attempt, questionId))
            ShownAtToken = shownAt;
        return Invalid(text);
    }

    public async Task<IActionResult> OnPostFollowUpAsync(string id, string? track, string questionId, string? text, int activeMs, int pasteCount)
    {
        var outcome = await ResolvedAsync(id, track, () => selfCheck.FollowUpAsync(UserId, Key, questionId, text, activeMs, pasteCount));
        if (outcome != SelfCheckOutcome.Invalid)
            return Result(outcome, id, questionId);
        return await LoadAsync(id, track, questionId) ?? Invalid(text);
    }

    public async Task<IActionResult> OnPostRateAsync(string id, string? track, string questionId, SelfRating rating) =>
        Result(await ResolvedAsync(id, track, () => selfCheck.RateAsync(UserId, Key, questionId, rating)), id, null);

    public async Task<IActionResult> OnPostSubmitAsync(string id, string? track) =>
        Result(await ResolvedAsync(id, track, () => selfCheck.SubmitAsync(UserId, Key)), id, null);

    private async Task<SelfCheckOutcome> ResolvedAsync(string id, string? track, Func<Task<SelfCheckOutcome>> action) =>
        Resolve(id, track) ? await action() : SelfCheckOutcome.NotFound;

    private IActionResult Result(SelfCheckOutcome outcome, string id, string? questionId) => outcome switch
    {
        SelfCheckOutcome.NotFound => NotFound(),
        SelfCheckOutcome.Invalid => BadRequest(),
        // Ok и Conflict (двойное нажатие, устаревшая вкладка) — к текущему состоянию попытки.
        _ => RedirectToPage("/Slices/Check", new { id, track = Key.TrackId, q = questionId }),
    };

    private IActionResult Invalid(string? text)
    {
        Error = text?.Length > SelfCheckService.MaxAnswerLength
            ? $"Ответ длиннее {SelfCheckService.MaxAnswerLength} символов."
            : "Напишите ответ своими словами — пустой ответ не сохраняется.";
        DraftText = text;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    /// <summary>Заполняет модель; не null — ответ вместо страницы (404).</summary>
    private async Task<IActionResult?> LoadAsync(string id, string? track, string? questionId)
    {
        if (!Resolve(id, track) || Questions.Count == 0)
            return NotFound();
        Status = await progress.GetStatusAsync(UserId, Key);
        if (!await selfCheck.CanCheckAsync(UserId, Key))
        {
            Locked = true;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return null;
        }
        Attempt = await selfCheck.LatestAsync(UserId, Key);
        if (Attempt?.Status != AttemptStatus.InProgress)
            return null;

        var current = selfCheck.CurrentQuestion(Attempt);
        CurrentIndex = current is null ? Questions.Count : Questions.ToList().IndexOf(current);
        // Вернуться можно к любому уже начатому вопросу; вперёд текущего — нельзя (вопросы по одному).
        var requested = Questions.ToList().FindIndex(x => x.Id == questionId);
        SelectedIndex = requested >= 0 && requested <= CurrentIndex ? requested : CurrentIndex;
        if (Selected is { } selected && AnswerTo(selected) is null)
            ShownAtToken = selfCheck.IssueShownAt(Attempt, selected);
        return null;
    }

    private bool Resolve(string id, string? track)
    {
        if (!catalog.Slices.TryGetValue(id, out var slice))
            return false;
        track ??= slice.Tracks[0];
        if (!slice.Tracks.Contains(track))
            return false;
        Slice = slice;
        Key = new ThematicSliceKey(id, track);
        Questions = catalog.QuestionsOf(id);
        return true;
    }
}
