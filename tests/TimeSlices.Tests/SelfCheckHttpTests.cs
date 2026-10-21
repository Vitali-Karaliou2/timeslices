using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>Контроль знаний по HTTP на настоящем Content/ (ADR 0011, план сессии 06, п. 8).</summary>
public class SelfCheckHttpTests(ShellFactory factory) : IClassFixture<ShellFactory>
{
    private const string Slice = "net-before-web-1992";
    private const string Check = "/slices/" + Slice + "/check";

    private IReadOnlyList<Question> Questions =>
        factory.Services.GetRequiredService<ContentCatalog>().QuestionsOf(Slice);

    // Эталон на странице — без префикса «ЧЕРНОВИК:» и HTML-экранированный; берётся из каталога, а не литералом.
    private string ReferenceOf(Question q) => WebUtility.HtmlEncode(DraftMark.Strip(q.ReferenceReasoning)[..40]);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string formPage, string action, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(formPage);
        fields["__RequestVerificationToken"] = ShellFactory.AntiforgeryToken(html);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    private async Task<HttpResponseMessage> AnswerAsync(HttpClient client, string questionId, string text, int activeMs = 0, int pasteCount = 0)
    {
        var html = await client.GetStringAsync(Check);
        factory.Clock.Offset += TimeSpan.FromMilliseconds(activeMs + 1000); // обучаемый думал и писал
        var shownAt = Regex.Match(html, "name=\"shownAt\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(shownAt);
        return await client.PostAsync(Check + "?handler=Answer", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["questionId"] = questionId, ["text"] = text, ["shownAt"] = shownAt,
            ["activeMs"] = activeMs.ToString(), ["pasteCount"] = pasteCount.ToString(),
            ["__RequestVerificationToken"] = ShellFactory.AntiforgeryToken(html),
        }));
    }

    private async Task StartAsync(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/slices/" + Slice)).StatusCode); // открыть срез
        var response = await PostAsync(client, Check, Check + "?handler=Start", []);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Все вопросы: ответ, продолжение, самооценка; первый ответ — с заданными признаками. Затем отправка.</summary>
    private async Task SubmitAllAsync(HttpClient client, int activeMs = 0, int pasteCount = 0)
    {
        await StartAsync(client);
        for (var i = 0; i < Questions.Count; i++)
        {
            var id = Questions[i].Id;
            Assert.Equal(HttpStatusCode.Redirect, (await AnswerAsync(client, id, $"ответ {i}", i == 0 ? activeMs : 0, i == 0 ? pasteCount : 0)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, Check, Check + "?handler=FollowUp",
                new() { ["questionId"] = id, ["text"] = $"продолжение {i}", ["activeMs"] = "0", ["pasteCount"] = "0" })).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, Check, Check + "?handler=Rate",
                new() { ["questionId"] = id, ["rating"] = "Partial" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, Check, Check + "?handler=Submit", [])).StatusCode);
    }

    /// <summary>Id попытки обучаемого в списке экзаменатора.</summary>
    private static async Task<string> AttemptIdAsync(HttpClient examiner, string learnerEmail)
    {
        var list = await examiner.GetStringAsync("/examiner");
        var row = Regex.Match(list, Regex.Escape(learnerEmail) + ".*?href=\"/Examiner/Attempt/(\\d+)\"", RegexOptions.Singleline);
        Assert.True(row.Success, "попытки нет в списке экзаменатора");
        return row.Groups[1].Value;
    }

    [Fact]
    public async Task Reference_IsNotSent_BeforeAnswerIsSaved()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);
        await StartAsync(client);

        var before = await client.GetStringAsync(Check);
        var foreign = await client.GetStringAsync(Check + "?q=" + Questions[1].Id); // вперёд текущего — показывается текущий

        foreach (var q in Questions)
        {
            Assert.DoesNotContain(ReferenceOf(q), before);
            Assert.DoesNotContain(ReferenceOf(q), foreign);
        }
        Assert.Contains(WebUtility.HtmlEncode(DraftMark.Strip(Questions[0].Prompt)[..40]), before);
        Assert.Contains("class=\"draft-badge\"", before);
        Assert.DoesNotContain(DraftMark.Prefix, before);

        Assert.Equal(HttpStatusCode.Redirect, (await AnswerAsync(client, Questions[0].Id, "мой ответ")).StatusCode);
        var after = await client.GetStringAsync(Check);

        Assert.Contains(ReferenceOf(Questions[0]), after);
        foreach (var q in Questions.Skip(1))
            Assert.DoesNotContain(ReferenceOf(q), after);
    }

    [Fact]
    public async Task Answer_IsHtmlEncoded_InAnswerAndFollowUp()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);
        await StartAsync(client);

        await AnswerAsync(client, Questions[0].Id, "<script>alert(1)</script>");
        var body = await client.GetStringAsync(Check);

        Assert.DoesNotContain("<script>alert(1)", body);
        Assert.Equal(2, Regex.Matches(body, Regex.Escape("&lt;script&gt;alert(1)&lt;/script&gt;")).Count); // ответ и продолжение
    }

    [Fact]
    public async Task Signs_HiddenFromLearner_VisibleToExaminer()
    {
        var (learner, email) = await factory.SignedInAsync(Roles.Learner, ShellFactory.HardwareStart);
        await SubmitAllAsync(learner, activeMs: 48213, pasteCount: 3);

        foreach (var url in new[] { Check, Check + "?q=" + Questions[0].Id, "/slices/" + Slice, "/" })
        {
            var body = await learner.GetStringAsync(url);
            Assert.DoesNotContain("48,2", body);
            Assert.DoesNotContain("Вставок", body);
            Assert.DoesNotContain("Активное время", body);
        }
        Assert.Contains("ожидает подтверждения", await learner.GetStringAsync("/"));

        var (examiner, _) = await factory.SignedInAsync(Roles.Examiner);
        var attempt = await examiner.GetStringAsync("/examiner/attempt/" + await AttemptIdAsync(examiner, email));

        Assert.Contains("<dt>Активное время ввода</dt><dd>48,2 с</dd>", attempt);
        Assert.Contains("<dt>Вставок из буфера</dt><dd>3</dd>", attempt);
        foreach (var q in Questions)
            Assert.Contains(ReferenceOf(q), attempt);
    }

    [Fact]
    public async Task Examiner_ForLearner_IsForbidden()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/examiner")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/examiner/attempt/1")).StatusCode);
        var post = await PostAsync(client, "/", "/examiner/attempt/1?handler=Confirm", []);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Examiner_ReturnsWithComment_ThenLearnerRetries_ThenConfirms()
    {
        var (learner, email) = await factory.SignedInAsync(Roles.Learner, ShellFactory.HardwareStart);
        await SubmitAllAsync(learner);
        var (examiner, _) = await factory.SignedInAsync(Roles.Admin); // Admin тоже экзаменатор
        var id = await AttemptIdAsync(examiner, email);
        var page = "/examiner/attempt/" + id;

        var noComment = await PostAsync(examiner, page, page + "?handler=Return", new() { ["comment"] = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noComment.StatusCode);
        var returned = await PostAsync(examiner, page, page + "?handler=Return", new() { ["comment"] = "Раскройте <b>вопрос 2</b>" });
        Assert.Equal(HttpStatusCode.Redirect, returned.StatusCode);

        var slicePage = await learner.GetStringAsync("/slices/" + Slice);
        Assert.Contains("возвращён", slicePage);
        Assert.Contains("Раскройте &lt;b&gt;вопрос 2&lt;/b&gt;", slicePage);
        Assert.Contains("возвращён", await learner.GetStringAsync("/"));

        await SubmitAllAsync(learner); // новая попытка
        var secondId = await AttemptIdAsync(examiner, email);
        Assert.NotEqual(id, secondId);
        var second = "/examiner/attempt/" + secondId;
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(examiner, second, second + "?handler=Confirm", [])).StatusCode);

        Assert.Contains("подтверждён экзаменатором", await learner.GetStringAsync("/slices/" + Slice));
        Assert.DoesNotContain("handler=Confirm", await examiner.GetStringAsync(second)); // решение уже принято
    }

    [Fact]
    public async Task Check_ForUnavailableSlice_IsForbidden()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/slices/static-page-1995/check")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/slices/ajax-2005/check")).StatusCode); // вопросов нет
    }

    [Fact]
    public async Task Admin_AssignsRole()
    {
        var (_, email) = await factory.SignedInAsync(Roles.Learner, ShellFactory.HardwareStart);
        var (admin, _) = await factory.SignedInAsync(Roles.Admin);
        string userId;
        using (var scope = factory.Services.CreateScope())
            userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(email))!.Id;

        var response = await PostAsync(admin, "/admin", "/admin?handler=SetRole", new() { ["userId"] = userId, ["role"] = Roles.Examiner });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var check = factory.Services.CreateScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal([Roles.Examiner], await users.GetRolesAsync((await users.FindByIdAsync(userId))!));
    }
}

/// <summary>Регистрация: в Development — сразу вход и роль Learner; в остальных средах — как раньше (подтверждение почты).</summary>
public class RegistrationTests
{
    private static async Task<(HttpResponseMessage Response, HttpClient Client, string Email)> RegisterAsync(ShellFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"new-{Guid.NewGuid():N}@test.local";
        var password = $"Tt1-{Guid.NewGuid():N}";
        var form = await client.GetStringAsync("/Identity/Account/Register");
        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email, ["Input.Password"] = password, ["Input.ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = ShellFactory.AntiforgeryToken(form),
        }));
        return (response, client, email);
    }

    private static async Task<IList<string>> RolesOf(ShellFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByNameAsync(email))!);
    }

    [Fact]
    public async Task Development_Registration_SignsInAsLearner()
    {
        using var factory = new DevelopmentShellFactory();

        var (response, client, email) = await RegisterAsync(factory);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal([Roles.Learner], await RolesOf(factory, email));
    }

    [Fact]
    public async Task OtherEnvironments_Registration_RequiresConfirmedEmail()
    {
        using var factory = new ShellFactory();

        var (response, client, email) = await RegisterAsync(factory);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/RegisterConfirmation", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode); // не вошёл
        Assert.Equal([Roles.Learner], await RolesOf(factory, email));
    }
}

/// <summary>Коды ошибок POST-обработчиков доходят до клиента как есть (страница Status при повторном выполнении).</summary>
public class StatusCodeTests(ShellFactory factory) : IClassFixture<ShellFactory>
{
    [Fact]
    public async Task Post_NotFoundAndBadRequest_KeepTheirCodes()
    {
        var (learner, _) = await factory.SignedInAsync(Roles.Learner, ShellFactory.HardwareStart);
        var token = ShellFactory.AntiforgeryToken(await learner.GetStringAsync("/slices/net-before-web-1992"));
        Assert.NotEmpty(token);
        FormUrlEncodedContent Form(params (string, string)[] f) =>
            new(f.Select(x => KeyValuePair.Create(x.Item1, x.Item2)).Append(KeyValuePair.Create("__RequestVerificationToken", token)));

        var missing = await learner.PostAsync("/slices/no-such-slice/check?handler=Start", Form());
        var locked = await learner.PostAsync("/slices/static-page-1995/check?handler=Start", Form());
        token = "forged";
        var badToken = await learner.PostAsync("/slices/net-before-web-1992/check?handler=Start", Form());

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("Страница не найдена", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        Assert.Contains("Неверный запрос", await badToken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_CannotChangeOwnRole()
    {
        var (admin, email) = await factory.SignedInAsync(Roles.Admin);
        string id;
        using (var scope = factory.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(email))!.Id;
        var token = ShellFactory.AntiforgeryToken(await admin.GetStringAsync("/admin"));

        var response = await admin.PostAsync("/admin?handler=SetRole", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["userId"] = id, ["role"] = Roles.Learner, ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
