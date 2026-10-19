using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Заголовки разделов среза и «Справка» (ADR 0013, план сессии 07).</summary>
public class ReferenceCatalogTests
{
    private readonly ContentCatalog _catalog = ContentLoader.Load(TestContent.RealContentRoot());

    [Fact]
    public void RealContent_HasFourArticles()
    {
        Assert.Equal(["dns", "ftp", "internet-before-web", "top-level-domains"], _catalog.Articles.Keys.Order(StringComparer.Ordinal));
        Assert.All(_catalog.Articles.Values, a => Assert.Equal("draft", a.Status));
    }

    [Fact]
    public void RealContent_NetBeforeWeb_HasApprovedHeadingsWithoutDraftPrefix()
    {
        var h = _catalog.Slices["net-before-web-1992"].Headings!;

        Assert.Equal("С чего начинаем", h.LinkBack);
        Assert.Equal("Что представлял собой Интернет к началу 1990-х", h.Introduces);
        Assert.Equal("Что изменится с появлением HTTP", h.LinkForward);
        Assert.Equal("Что осталось актуальным и в 2020-е", h.Relevance);
        Assert.Null(_catalog.Slices["cpu-cycle"].Headings);
    }

    [Fact]
    public void Mentions_Dns_AreFramesOfNetBeforeWebAndStaticPage()
    {
        var mentions = _catalog.MentionsOf("dns");

        Assert.All(mentions, m => Assert.True(m.IsFrame));
        Assert.Equal(
            [("net-before-web-1992", "ftp-1992", "ftp-session", 3), ("static-page-1995", "web-1995", "request-response", 1)],
            mentions.Select(m => (m.SliceId, m.ImplementationId!, m.ProcessId!, m.FrameOrder!.Value)).Order());
    }

    [Fact]
    public void Mentions_InternetBeforeWeb_IsNetBeforeWebSlice()
    {
        Assert.Equal([new ReferenceMention("net-before-web-1992")], _catalog.MentionsOf("internet-before-web"));
    }

    [Theory]
    [InlineData("top-level-domains#1992", "Домены верхнего уровня: Как было к 1992 году")]
    [InlineData("internet-before-web", "Интернет до веба")]
    public void ResolveRef_LabelIsArticleTitleAndSectionHeading(string reference, string label)
    {
        Assert.Equal(label, _catalog.ResolveRef(reference)!.Label);
    }

    [Theory]
    [InlineData("missing", "статья 'missing' не найдена")]
    [InlineData("dns#1993", "в статье 'dns' нет раздела '1993'")]
    public void Load_RefToMissingArticleOrSection_Fails(string reference, string message)
    {
        using var content = new TestContent().Track("t").Slice("a", "t").Article("dns", "1992", "now").SliceRefs("a", reference);

        var ex = Assert.Throws<ContentLoadException>(content.Load);

        var problem = Assert.Single(ex.Problems);
        Assert.Contains("slices/a/slice.json", problem);
        Assert.Contains($"'{reference}'", problem);
        Assert.Contains(message, problem);
    }

    [Fact]
    public void Load_NullRefsOrNonHttpsSource_FailsWithFileName()
    {
        using var content = new TestContent().Track("t").Slice("a", "t").Article("dns", "1992");
        File.WriteAllText(Path.Combine(content.Root, "reference", "dns", "article.json"),
            File.ReadAllText(Path.Combine(content.Root, "reference", "dns", "article.json"))
                .Replace("\"sources\":[]", "\"sources\":[{\"title\":\"x\",\"url\":\"javascript:alert(1)\"}]"));
        var slice = Path.Combine(content.Root, "slices", "a", "slice.json");
        File.WriteAllText(slice, File.ReadAllText(slice).Replace("\"status\":", "\"refs\":null,\"status\":"));

        var ex = Assert.Throws<ContentLoadException>(content.Load);

        Assert.Contains(ex.Problems, p => p.Contains("article.json") && p.Contains("https://"));
        Assert.Contains(ex.Problems, p => p.Contains("slices/a/slice.json") && p.Contains("refs"));
    }

    [Fact]
    public void Load_RefToExistingSection_Succeeds()
    {
        using var content = new TestContent().Track("t").Slice("a", "t").Article("dns", "1992", "now").SliceRefs("a", "dns#1992", "dns");

        var catalog = content.Load();

        Assert.Equal([new ReferenceMention("a")], catalog.MentionsOf("dns"));
    }

    [Fact]
    public void Schedule_FtpSession_StopFrames2And3_HaveRefs()
    {
        var process = _catalog.Examples["directory"].Implementations.Single(i => i.Id == "ftp-1992").Processes[0].Process!;

        var steps = ProcessSchedule.Build(process, r => _catalog.ResolveRef(r) is { } t ? new RefLink(t.Label, "#" + r) : null);

        Assert.Equal(["#top-level-domains#1992", "#ftp#1992"], steps.Single(s => s.Order == 2).Detail!.Refs!.Select(r => r.Href));
        Assert.Equal(["#dns#1992"], steps.Single(s => s.Order == 3).Detail!.Refs!.Select(r => r.Href));
        Assert.Null(steps.Single(s => s.Order == 4).Detail!.Refs);
    }
}

public class ReferenceHttpTests(ShellFactory factory) : IClassFixture<ShellFactory>
{
    private HttpClient Anonymous() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/reference")]
    [InlineData("/reference/dns")]
    public async Task Anonymous_Reference_IsOpen(string url)
    {
        var response = await Anonymous().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_SlicePage_StillRedirectsToLogin()
    {
        var response = await Anonymous().GetAsync("/slices/net-before-web-1992");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Anonymous_MissingArticle_IsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync("/reference/missing")).StatusCode);
    }

    [Fact]
    public async Task ArticlePage_HasBadgeAnchorsMentionsAndMenuItem()
    {
        var body = await Anonymous().GetStringAsync("/reference/dns");

        Assert.DoesNotContain(DraftMark.Prefix, body);
        Assert.Contains("class=\"draft-badge\"", body);
        Assert.Contains("id=\"1992\"", body);
        Assert.Contains("(к 1992 году)", body);
        Assert.Contains("(сейчас)", body);
        Assert.Contains("<h2>Где упоминается</h2>", body);
        Assert.Contains("href=\"/slices/net-before-web-1992/examples/directory?impl=ftp-1992\"", body);
        Assert.Contains("href=\"/slices/static-page-1995/examples/directory?impl=web-1995\"", body);
        Assert.Contains("href=\"/reference\">Справка</a>", body);
    }

    [Fact]
    public async Task Learner_NetBeforeWeb_ShowsHeadingsFromSliceAndRefsAfterIntroduces()
    {
        var client = await factory.SignedInLearnerAsync();

        var body = await client.GetStringAsync("/slices/net-before-web-1992");

        string[] order =
        [
            "<h2 data-section=\"linkBack\">С чего начинаем</h2>",
            "<h2 data-section=\"introduces\">Что представлял собой Интернет к началу 1990-х</h2>",
            "<p class=\"refs\">Справка:",
            "<h2 data-section=\"linkForward\">Что изменится с появлением HTTP</h2>",
            "<h2 data-section=\"relevance\">Что осталось актуальным и в 2020-е</h2>",
        ];
        var positions = order.Select(h => body.IndexOf(h, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("<a href=\"/reference/internet-before-web\" target=\"_blank\" rel=\"noopener\">Интернет до веба</a>", body);
    }

    [Fact]
    public async Task Learner_FtpExample_ScheduleAndNoJsListHaveRefs()
    {
        var client = await factory.SignedInLearnerAsync();

        var body = await client.GetStringAsync("/slices/net-before-web-1992/examples/directory?impl=ftp-1992");

        var json = Regex.Match(body, "<script type=\"application/json\" class=\"process-schedule\">(.*?)</script>", RegexOptions.Singleline);
        using var schedule = JsonDocument.Parse(json.Groups[1].Value);
        string[] Hrefs(int i) => schedule.RootElement[i].GetProperty("detail").GetProperty("refs")
            .EnumerateArray().Select(r => r.GetProperty("href").GetString()!).ToArray();
        Assert.Equal(["/reference/top-level-domains#1992", "/reference/ftp#1992"], Hrefs(1));
        Assert.Equal(["/reference/dns#1992"], Hrefs(2));
        Assert.Equal("Система имён DNS: Как было к 1992 году",
            schedule.RootElement[2].GetProperty("detail").GetProperty("refs")[0].GetProperty("label").GetString());
        // Без JS — та же строка в списке кадров, ссылка в новой вкладке.
        Assert.Contains("<a href=\"/reference/dns#1992\" target=\"_blank\" rel=\"noopener\">Система имён DNS: Как было к 1992 году</a>", body);
        Assert.Contains("class=\"player-stop-refs refs\" hidden", body);
    }
}
