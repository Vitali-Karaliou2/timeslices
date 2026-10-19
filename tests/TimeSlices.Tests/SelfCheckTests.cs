using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>Самопроверка и демо-экзаменатор (ADR 0011): шаги вопроса, статусы попытки, признаки.</summary>
public class SelfCheckTests : IDisposable
{
    // a ──► b; вопросы только у a.
    private readonly TestContent _content = new TestContent().Track("t")
        .Slice("a", "t")
        .Slice("b", "t", "a@t")
        .Questions("a", "q1", "q2");

    private readonly ProgressFixture _fx;
    private static readonly ThematicSliceKey A = new("a", "t");
    private const string User = ProgressFixture.UserId;

    public SelfCheckTests()
    {
        _fx = new ProgressFixture(_content.Load());
    }

    public void Dispose()
    {
        _fx.Dispose();
        _content.Dispose();
    }

    private SelfCheckService S => _fx.SelfCheck;

    private async Task<Attempt> Latest()
    {
        _fx.Db.ChangeTracker.Clear();
        return (await S.LatestAsync(User, A))!;
    }

    private async Task<SelfCheckOutcome> Answer(string questionId, string text = "мой ответ", int activeMs = 1000, int pastes = 0)
    {
        var attempt = await Latest();
        return await S.AnswerAsync(User, A, questionId, text, S.IssueShownAt(attempt, new Question { Id = questionId }), activeMs, pastes);
    }

    private async Task CompleteQuestion(string questionId)
    {
        Assert.Equal(SelfCheckOutcome.Ok, await Answer(questionId));
        Assert.Equal(SelfCheckOutcome.Ok, await S.FollowUpAsync(User, A, questionId, "продолжение", 500, 0));
        Assert.Equal(SelfCheckOutcome.Ok, await S.RateAsync(User, A, questionId, SelfRating.Partial));
    }

    private async Task StartedAndSubmitted()
    {
        await _fx.Service.OpenAsync(User, A);
        Assert.Equal(SelfCheckOutcome.Ok, await S.StartAsync(User, A));
        await CompleteQuestion("q1");
        await CompleteQuestion("q2");
        Assert.Equal(SelfCheckOutcome.Ok, await S.SubmitAsync(User, A));
    }

    [Fact]
    public async Task Check_OnlyForSliceInProgressOrDone_WithQuestions()
    {
        Assert.Equal(SelfCheckOutcome.Forbidden, await S.StartAsync(User, A)); // «доступен», ещё не открыт
        await _fx.Service.MarkDoneAsync(User, A);
        Assert.False(await S.CanCheckAsync(User, new ThematicSliceKey("b", "t"))); // вопросов нет
        Assert.True(await S.CanCheckAsync(User, A));
        Assert.Equal(SelfCheckOutcome.Ok, await S.StartAsync(User, A));
    }

    [Fact]
    public async Task Questions_GoInOrder_AndSavedAnswerIsImmutable()
    {
        await _fx.Service.OpenAsync(User, A);
        await S.StartAsync(User, A);

        Assert.Equal(SelfCheckOutcome.Conflict, await Answer("q2"));       // вперёд текущего нельзя
        Assert.Equal(SelfCheckOutcome.Invalid, await Answer("q1", "   ")); // пустой ответ
        Assert.Equal(SelfCheckOutcome.Ok, await Answer("q1", "первый"));
        Assert.Equal(SelfCheckOutcome.Conflict, await Answer("q1", "второй"));
        Assert.Equal(SelfCheckOutcome.Conflict, await S.RateAsync(User, A, "q1", SelfRating.Match)); // сначала продолжение
        Assert.Equal(SelfCheckOutcome.Ok, await S.FollowUpAsync(User, A, "q1", "продолжение", 0, 0));
        Assert.Equal(SelfCheckOutcome.Conflict, await S.FollowUpAsync(User, A, "q1", "другое", 0, 0));
        Assert.Equal(SelfCheckOutcome.Ok, await S.RateAsync(User, A, "q1", SelfRating.Match));
        Assert.Equal(SelfCheckOutcome.Conflict, await S.RateAsync(User, A, "q1", SelfRating.NoMatch));

        var answer = (await Latest()).Answers.Single();
        Assert.Equal(("первый", "продолжение", SelfRating.Match), (answer.Text, answer.FollowUpText, answer.SelfRating));
        Assert.Equal("q2", S.CurrentQuestion(await Latest())?.Id);
    }

    [Fact]
    public async Task ShownAt_ForgedOrForAnotherQuestion_IsRejected()
    {
        await _fx.Service.OpenAsync(User, A);
        await S.StartAsync(User, A);
        var attempt = await Latest();

        Assert.Equal(SelfCheckOutcome.Invalid, await S.AnswerAsync(User, A, "q1", "ответ", "forged", 0, 0));
        Assert.Equal(SelfCheckOutcome.Invalid, await S.AnswerAsync(User, A, "q1", "ответ", S.IssueShownAt(attempt, new Question { Id = "q2" }), 0, 0));
    }

    [Fact]
    public async Task Signs_AreClampedToPlausibleRange()
    {
        await _fx.Service.OpenAsync(User, A);
        await S.StartAsync(User, A);

        await Answer("q1", activeMs: int.MaxValue, pastes: -5);

        var answer = (await Latest()).Answers.Single();
        Assert.True(answer.ActiveMs <= (answer.AnsweredAt - answer.ShownAt).TotalMilliseconds + 1);
        Assert.Equal(0, answer.PasteCount);
        Assert.True(answer.ShownAt <= answer.AnsweredAt);
    }

    [Fact]
    public async Task Attempt_InProgress_Submitted_Returned_NewAttempt_Confirmed()
    {
        await _fx.Service.OpenAsync(User, A);
        await S.StartAsync(User, A);
        Assert.Equal(AttemptStatus.InProgress, (await Latest()).Status);
        Assert.Equal(SelfCheckOutcome.Conflict, await S.SubmitAsync(User, A)); // не все вопросы пройдены
        Assert.Equal(SelfCheckOutcome.Conflict, await S.StartAsync(User, A)); // вторая попытка параллельно — нет

        await CompleteQuestion("q1");
        await CompleteQuestion("q2");
        Assert.Equal(SelfCheckOutcome.Ok, await S.SubmitAsync(User, A));
        var submitted = await Latest();
        Assert.Equal(AttemptStatus.Submitted, submitted.Status);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Equal(SelfCheckOutcome.Conflict, await Answer("q1")); // отправленную попытку не изменить
        Assert.Equal(SelfCheckOutcome.Conflict, await S.StartAsync(User, A));

        Assert.Equal(SelfCheckOutcome.Invalid, await S.ReturnAsync(submitted.Id, ProgressFixture.ExaminerId, "  "));
        Assert.Equal(SelfCheckOutcome.Ok, await S.ReturnAsync(submitted.Id, ProgressFixture.ExaminerId, "Раскройте вопрос 2"));
        var returned = await Latest();
        Assert.Equal((AttemptStatus.Returned, "Раскройте вопрос 2", ProgressFixture.ExaminerId), (returned.Status, returned.ExaminerComment, returned.ExaminerId));
        Assert.Equal(SelfCheckOutcome.Conflict, await S.ConfirmAsync(submitted.Id, ProgressFixture.ExaminerId)); // решение уже принято

        // Повторная сдача после возврата — новая попытка; старая остаётся как была.
        Assert.Equal(SelfCheckOutcome.Ok, await S.StartAsync(User, A));
        var second = await Latest();
        Assert.NotEqual(submitted.Id, second.Id);
        Assert.Empty(second.Answers);
        await CompleteQuestion("q1");
        await CompleteQuestion("q2");
        await S.SubmitAsync(User, A);
        Assert.Equal(SelfCheckOutcome.Ok, await S.ConfirmAsync(second.Id, ProgressFixture.ExaminerId));
        Assert.Equal(AttemptStatus.Confirmed, (await Latest()).Status);
        Assert.Equal(SelfCheckOutcome.Conflict, await S.StartAsync(User, A)); // подтверждённую не пересдают
        Assert.Equal(2, await _fx.Db.Attempts.CountAsync());
    }

    [Fact]
    public async Task ParallelStartAndDecide_OnlyOneWins()
    {
        // Отдельные контексты на одной БД — как параллельные запросы.
        await _fx.Service.OpenAsync(User, A);
        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _fx.WithNewContext(s => s.StartAsync(User, A))));
        Assert.Single(starts, SelfCheckOutcome.Ok);
        Assert.Equal(1, await _fx.Db.Attempts.CountAsync());

        await CompleteQuestion("q1");
        await CompleteQuestion("q2");
        await S.SubmitAsync(User, A);
        var id = (await Latest()).Id;
        var decisions = await Task.WhenAll(
            _fx.WithNewContext(s => s.ConfirmAsync(id, ProgressFixture.ExaminerId)),
            _fx.WithNewContext(s => s.ReturnAsync(id, ProgressFixture.ExaminerId, "нет")));
        Assert.Single(decisions, SelfCheckOutcome.Ok);
        Assert.Single(decisions, SelfCheckOutcome.Conflict);
    }

    [Fact]
    public async Task Examiner_DoesNotSeeAttemptInProgress()
    {
        await _fx.Service.OpenAsync(User, A);
        await S.StartAsync(User, A);
        var attempt = await Latest();

        Assert.Empty(await S.SubmittedAttemptsAsync());
        Assert.Null(await S.SubmittedAttemptAsync(attempt.Id));
        Assert.Equal(SelfCheckOutcome.NotFound, await S.ConfirmAsync(attempt.Id, ProgressFixture.ExaminerId));
    }

    [Fact]
    public async Task Submission_DoesNotChangeSliceStates()
    {
        await StartedAndSubmitted();

        Assert.Equal(SliceState.InProgress, (await _fx.StatusOf("a@t")).State);
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("b@t")).State);
    }

    [Fact]
    public void FollowUp_InsertsFirst300CharactersOfAnswer()
    {
        var question = new Question { Id = "q", FollowUpTemplate = "ЧЕРНОВИК: Вы написали: «{answer}». Почему?" };

        Assert.Equal("Вы написали: «<b>x</b>». Почему?", SelfCheckService.FollowUpText(question, "<b>x</b>"));
        var longText = SelfCheckService.FollowUpText(question, new string('я', 400));
        Assert.Contains(new string('я', 300) + "…»", longText);
        Assert.DoesNotContain(new string('я', 301), longText);
        // Суррогатная пара на границе не разрывается.
        var emoji = SelfCheckService.FollowUpText(question, new string('a', 299) + "😀" + "zz");
        Assert.Contains(new string('a', 299) + "…»", emoji);
    }
}
