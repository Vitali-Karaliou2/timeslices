using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>
/// Оболочка целиком по HTTP на настоящем Content/ (docs/acceptance.md, п. 2 и 3).
/// БД — временный файл SQLite; среда не Development, поэтому dev-пользователи не сидируются.
/// </summary>
public class ShellFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "timeslices-tests", $"{Guid.NewGuid():N}.db");

    protected virtual string EnvironmentName => "Testing";

    /// <summary>Часы оболочки: системное время плюс сдвиг, который тест может увеличить («подумал две минуты»).</summary>
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"DataSource={_db}");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    /// <summary>
    /// Начало «Общего старта» до сети (ADR 0014): после него открыт net-before-web-1992 — прежняя точка входа
    /// HTTP-тестов, написанных до сессии 08.
    /// </summary>
    public static readonly string[] HardwareStart = ["eniac-1946", "edsac-1949", "register-machine-1965"];

    /// <summary>Обучаемый со случайным паролем в тестовой БД; возвращает клиента, уже вошедшего под ним.</summary>
    public async Task<HttpClient> SignedInLearnerAsync(params string[] doneSlices) => (await SignedInAsync(Roles.Learner, doneSlices)).Client;

    /// <summary>Пользователь с ролью и случайным паролем; клиент уже вошёл под ним. doneSlices — пройденные срезы.</summary>
    public async Task<(HttpClient Client, string Email)> SignedInAsync(string role, params string[] doneSlices)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var password = $"Tt1-{Guid.NewGuid():N}";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var slice in doneSlices)
                db.UserSliceStates.Add(new UserSliceState { UserId = user.Id, SliceId = slice, State = SliceState.Done, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var form = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = AntiforgeryToken(form),
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (client, email);
    }

    public static string AntiforgeryToken(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }
}

public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}

/// <summary>То же в среде Development: регистрация без подтверждения почты, сидируются dev-пользователи.</summary>
public sealed class DevelopmentShellFactory : ShellFactory
{
    protected override string EnvironmentName => "Development";
}

public class HttpTests(ShellFactory factory) : IClassFixture<ShellFactory>
{
    [Fact]
    public async Task Anonymous_Root_RedirectsToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location?.PathAndQuery);
    }

    [Theory]
    [InlineData("/js/site.js")]
    [InlineData("/js/process-player.js")]
    [InlineData("/css/site.css")]
    public async Task Anonymous_ShellStaticFile_IsServedWithoutLogin(string url)
    {
        // Сессия 03: на странице входа скрипты оболочки перенаправлялись на вход (SyntaxError в консоли).
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_LoginPage_LoadsNoValidationScriptsWithoutJQuery()
    {
        // Identity UI подключает jquery.validate, а jQuery в оболочке нет: скрипты падали бы с ошибкой.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var body = await client.GetStringAsync("/Identity/Account/Login");

        Assert.DoesNotContain("jquery", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Learner_ExamplePage_HasProcessPlayerWithScheduleAndFrameList()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        var body = await client.GetStringAsync("/slices/net-before-web-1992/examples/directory?impl=ftp-1992");

        Assert.Matches("<script src=\"/js/process-player[.a-z0-9]*js\">", body);
        var json = Regex.Match(body, "<script type=\"application/json\" class=\"process-schedule\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(json.Success, "нет расписания процесса в странице");
        using var schedule = System.Text.Json.JsonDocument.Parse(json.Groups[1].Value);
        Assert.Equal(9, schedule.RootElement.GetArrayLength());
        // Первый стоп-кадр — письмо коллеги (откуда пользователь знает адрес), второй — набранная команда.
        Assert.Equal("code/letter.txt", schedule.RootElement[0].GetProperty("detail").GetProperty("codeFile").GetString());
        Assert.Equal("code/session.txt", schedule.RootElement[1].GetProperty("detail").GetProperty("codeFile").GetString());
        // Без JS список кадров виден целиком, проигрыватель скрыт; кнопки — настоящие <button>.
        Assert.Equal(9, Regex.Matches(body, "<li class=\"[^\"]*\" data-order=").Count);
        Assert.Contains("<div class=\"player\" hidden>", body);
        Assert.Contains("<button type=\"button\" data-action=\"play\">Проиграть</button>", body);
        Assert.Contains("aria-live=\"polite\"", body);
    }

    [Theory]
    [InlineData("/slices/net-before-web-1992")]
    [InlineData("/slices/net-before-web-1992/examples/directory?impl=ftp-1992")]
    [InlineData("/slices/eniac-1946")]
    [InlineData("/slices/eniac-1946/examples/squares?impl=eniac-1946")]
    [InlineData("/slices/edsac-1949/examples/squares?impl=stored-program-1949-overview")]
    [InlineData("/")]
    public async Task Learner_Pages_ShowDraftBadgeInsteadOfPrefix(string url)
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        var body = await client.GetStringAsync(url);

        Assert.DoesNotContain(DraftMark.Prefix, body);
        Assert.Contains("class=\"draft-badge\"", body);
    }

    [Fact]
    public async Task Learner_ExamplePage_StopFrameExplanation_InListOnlyForNoJs()
    {
        // Пояснение стоп-кадра в разметке — один раз, в списке кадров (для показа без JS); с проигрывателем
        // его прячет класс player-on, а панель стоп-кадра берёт текст из расписания.
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        var body = await client.GetStringAsync("/slices/net-before-web-1992/examples/directory?impl=ftp-1992");

        Assert.Equal(7, Regex.Matches(body, "<span class=\"frame-explanation\">").Count);
        Assert.Contains("<p class=\"player-stop-explanation\" id=\"stop-explanation-ftp-session\"></p>", body);
        Assert.Contains("aria-describedby=\"stop-explanation-ftp-session\"", body);
    }

    [Theory]
    [InlineData("/slices/eniac-1946", "eniac-1946")]
    [InlineData("/slices/net-before-web-1992", "net-before-web-1992")]
    public async Task Learner_SlicePage_ShowsRelevanceNoteWithDraftBadge(string url, string sliceId)
    {
        // ADR 0010: пояснение — рядом с пометкой актуальности, без префикса, со значком «черновик» (ADR 0009).
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);
        var note = factory.Services.GetRequiredService<ContentCatalog>().Slices[sliceId].RelevanceNote!;

        var body = await client.GetStringAsync(url);

        var block = Regex.Match(body, "<h2 data-section=\"relevance\">[^<]*</h2>\\s*<p>[^<]+</p>\\s*<p class=\"relevance-note\">(.*?)</p>", RegexOptions.Singleline);
        Assert.True(block.Success, "нет пояснения сразу после пометки актуальности");
        Assert.StartsWith("<span class=\"draft-badge\"", block.Groups[1].Value);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(DraftMark.Strip(note)[..40]), block.Groups[1].Value);
    }

    [Fact]
    public async Task Learner_SlicePage_SectionsGoBeforeDecisionAfterThenRelevance()
    {
        // ADR 0013, п. 1: «до → решение → после», затем актуальность и примеры; подписи по умолчанию (п. 2).
        var client = await factory.SignedInLearnerAsync([.. ShellFactory.HardwareStart, "net-before-web-1992"]);

        var body = await client.GetStringAsync("/slices/static-page-1995"); // срез без headings

        string[] order =
        [
            "<h2 data-section=\"linkBack\">Что было до</h2>", "<h2 data-section=\"introduces\">Что меняется</h2>",
            "<h2 data-section=\"linkForward\">Что будет после</h2>",
            "<h2 data-section=\"relevance\">Как это связано с сегодняшним днём?</h2>", "<h2 data-section=\"examples\">Примеры</h2>",
        ];
        var positions = order.Select(h => body.IndexOf(h, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    // Эталон берётся из каталога, а не литералом: тексты срезов — черновики и будут меняться.
    // На странице текст показан без префикса «ЧЕРНОВИК:» (DraftMark).
    private string IntroducesOf(string sliceId) =>
        DraftMark.Strip(factory.Services.GetRequiredService<ContentCatalog>().Slices[sliceId].Introduces)[..40];

    private const string FtpEntry = "slices/net-before-web-1992/examples/directory/implementations/ftp-1992/index.html";

    [Fact]
    public async Task Learner_HomePage_FoundationsFirst_EniacEdsacRegisterMachineNet()
    {
        var client = await factory.SignedInLearnerAsync();
        var catalog = factory.Services.GetRequiredService<ContentCatalog>();

        var body = await client.GetStringAsync("/");

        var sections = Regex.Matches(body, "<section>(.*?)</section>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("<h2>Общий старт</h2>", sections[0]);
        string[] order = ["eniac-1946", "edsac-1949", "register-machine-1965", "net-before-web-1992"];
        var positions = order.Select(id => sections[0].IndexOf(System.Net.WebUtility.HtmlEncode(catalog.Slices[id].Title), StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Equal(4, Regex.Matches(sections[0], "<li>").Count);
    }

    [Fact]
    public async Task Learner_New_StartsWithEniac_NetBeforeWebLocked()
    {
        // ADR 0014: начало коллекции — «Общий старт»; сеть открывается после машины с регистрами.
        var client = await factory.SignedInLearnerAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/slices/eniac-1946")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/slices/net-before-web-1992")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/slices/edsac-1949-deep")).StatusCode);
    }

    [Fact]
    public async Task Learner_SliceWithUnfinishedPrerequisite_IsForbiddenWithoutContent()
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        var available = await client.GetAsync("/slices/net-before-web-1992");
        var locked = await client.GetAsync("/slices/forms-cgi-1995");

        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        Assert.Contains(IntroducesOf("net-before-web-1992"), await available.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        var body = await locked.Content.ReadAsStringAsync();
        Assert.Contains("Срез недоступен", body);
        Assert.DoesNotContain(IntroducesOf("forms-cgi-1995"), body);
    }

    [Theory]
    [InlineData("/content/" + FtpEntry)]
    [InlineData("/content/forged/" + FtpEntry)]
    [InlineData("/content/forged/slices/static-page-1995/slice.json")]
    public async Task Content_WithoutToken_IsNotFound(string url)
    {
        var client = await factory.SignedInLearnerAsync(ShellFactory.HardwareStart);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/content/" + FtpEntry)]
    [InlineData("/content/forged/" + FtpEntry)]
    public async Task Anonymous_ContentWithoutToken_IsNotFound(string url)
    {
        // Статика оболочки открыта без входа, а содержание примеров — по-прежнему только по токену.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private const string Directory = "slices/net-before-web-1992/examples/directory/implementations/";

    [Theory]
    [InlineData(FtpEntry, "text/html", "utf-8")]
    [InlineData(Directory + "ftp-1992/code/session.txt", "text/plain", "utf-8")]
    [InlineData(Directory + "ftp-1992/code/letter.txt", "text/plain", "utf-8")]
    [InlineData(Directory + "ftp-1992/terminal.js", "text/javascript", "utf-8")]
    [InlineData(Directory + "web-1995/code/dept.gif", "image/gif", null)]
    [InlineData(Directory + "web-1995/code/chem/index.html", "text/html", "utf-8")]
    public async Task Content_WithToken_IsServed(string path, string mediaType, string? charset)
    {
        // Положительный контроль к тесту выше: маршрут жив, и 404 там — именно из-за токена.
        // Токен выдан на реализацию, и относительные ссылки внутри сборки (картинка, вторая страница) им пользуются.
        var catalog = factory.Services.GetRequiredService<ContentCatalog>();
        var (example, implementation) = catalog.FindServableFile(Path.Combine(catalog.Root, path))!.Value;
        var token = Uri.EscapeDataString(factory.Services.GetRequiredService<ContentAccess>().IssueToken(example, implementation));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/content/{token}/{path}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(charset, response.Content.Headers.ContentType?.CharSet);
    }
}
