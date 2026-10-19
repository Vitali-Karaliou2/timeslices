using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Web.Pages.Examiner;

/// <summary>
/// Попытка для экзаменатора: ответы, продолжения, самооценки, признаки и эталоны;
/// «Подтвердить» и «Вернуть» (комментарий обязателен). Признаки видны только здесь.
/// </summary>
public class AttemptModel(ContentCatalog catalog, SelfCheckService selfCheck, UserManager<ApplicationUser> users) : PageModel
{
    public Attempt Attempt { get; private set; } = null!;
    public string LearnerName { get; private set; } = "";
    public string? ExaminerName { get; private set; }
    public string SliceTitle { get; private set; } = "";
    public string? Error { get; private set; }

    /// <summary>Вопросы в порядке questions.json, затем ответы на вопросы, которых в файле больше нет.</summary>
    public List<(Question? Question, string QuestionId, Answer? Answer)> Rows { get; } = [];

    public async Task<IActionResult> OnGetAsync(int attemptId) =>
        await LoadAsync(attemptId) ? Page() : NotFound();

    public async Task<IActionResult> OnPostConfirmAsync(int attemptId) =>
        Result(await selfCheck.ConfirmAsync(attemptId, users.GetUserId(User)!), attemptId);

    public async Task<IActionResult> OnPostReturnAsync(int attemptId, string? comment)
    {
        var outcome = await selfCheck.ReturnAsync(attemptId, users.GetUserId(User)!, comment);
        if (outcome != SelfCheckOutcome.Invalid)
            return Result(outcome, attemptId);
        if (!await LoadAsync(attemptId))
            return NotFound();
        Error = "При возврате нужен комментарий для обучаемого.";
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private IActionResult Result(SelfCheckOutcome outcome, int attemptId) =>
        outcome == SelfCheckOutcome.NotFound ? NotFound() : RedirectToPage("/Examiner/Attempt", new { attemptId });

    private async Task<bool> LoadAsync(int attemptId)
    {
        if (await selfCheck.SubmittedAttemptAsync(attemptId) is not { } attempt)
            return false;
        Attempt = attempt;
        LearnerName = (await users.FindByIdAsync(attempt.UserId))?.UserName ?? attempt.UserId;
        if (attempt.ExaminerId is { } examinerId)
            ExaminerName = (await users.FindByIdAsync(examinerId))?.UserName ?? examinerId;
        SliceTitle = catalog.Slices.TryGetValue(attempt.SliceId, out var s) ? s.Title : attempt.SliceId;

        var questions = catalog.QuestionsOf(attempt.SliceId);
        foreach (var q in questions)
            Rows.Add((q, q.Id, attempt.Answers.FirstOrDefault(a => a.QuestionId == q.Id)));
        // ADR 0011, «Последствия»: вопрос удалён из questions.json после сдачи — ответ всё равно виден.
        foreach (var a in attempt.Answers.Where(a => questions.All(q => q.Id != a.QuestionId)))
            Rows.Add((null, a.QuestionId, a));
        return true;
    }
}
