using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>
/// Оболочка целиком по HTTP на настоящем Content/ (docs/acceptance.md, п. 2 и 3).
/// БД — временный файл SQLite; среда не Development, поэтому dev-пользователи не сидируются.
/// </summary>
public sealed class ShellFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "timeslices-tests", $"{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"DataSource={_db}");
    }

    /// <summary>Обучаемый со случайным паролем в тестовой БД; возвращает клиента, уже вошедшего под ним.</summary>
    public async Task<HttpClient> SignedInLearnerAsync()
    {
        var email = $"learner-{Guid.NewGuid():N}@test.local";
        var password = $"Tt1-{Guid.NewGuid():N}";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, Roles.Learner)).Succeeded);
        }

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var form = await client.GetStringAsync("/Identity/Account/Login");
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }
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

    // Эталон берётся из каталога, а не литералом: тексты срезов — черновики и будут меняться.
    private string IntroducesOf(string sliceId) =>
        factory.Services.GetRequiredService<ContentCatalog>().Slices[sliceId].Introduces[..40];

    private const string FtpEntry = "slices/net-before-web-1992/examples/directory/implementations/ftp-1992/index.html";

    [Fact]
    public async Task Learner_SliceWithUnfinishedPrerequisite_IsForbiddenWithoutContent()
    {
        var client = await factory.SignedInLearnerAsync();

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
        var client = await factory.SignedInLearnerAsync();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_WithToken_IsServed()
    {
        // Положительный контроль к тесту выше: маршрут жив, и 404 там — именно из-за токена.
        var catalog = factory.Services.GetRequiredService<ContentCatalog>();
        var (example, implementation) = catalog.FindServableFile(Path.Combine(catalog.Root, FtpEntry))!.Value;
        var token = Uri.EscapeDataString(factory.Services.GetRequiredService<ContentAccess>().IssueToken(example, implementation));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/content/{token}/{FtpEntry}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
    }
}
