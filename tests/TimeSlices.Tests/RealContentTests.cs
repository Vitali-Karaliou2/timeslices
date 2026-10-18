using System.Text.RegularExpressions;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>Настоящее содержание Content/: первый набор (ADR 0003, план сессии 02).</summary>
public class RealContentTests
{
    private readonly ContentCatalog _catalog = ContentLoader.Load(TestContent.RealContentRoot());

    [Fact]
    public void FirstSet_HasFourTracksAndSevenSlices()
    {
        Assert.Equal(["hardware", "computer-networks", "frontend", "process"], _catalog.Tracks.Select(t => t.Id));
        Assert.Equal(
            ["ajax-2005", "cpu-cycle", "forms-cgi-1995", "net-before-web-1992", "punch-cards-1972",
             "rup-vs-xp-2001", "static-page-1995"],
            _catalog.Slices.Keys.Order(StringComparer.Ordinal));
        Assert.All(_catalog.Slices.Values, s => Assert.Equal("draft", s.Status));
    }

    [Fact]
    public void Graph_HasNoCycles()
    {
        // Загрузчик отказывает при цикле; здесь — независимая проверка: ни один срез не следует за самим собой.
        foreach (var key in _catalog.ThematicSlices)
            Assert.DoesNotContain(key, _catalog.AllSuccessorsOf(key));
    }

    [Fact]
    public void StaticPage_FollowsNetBeforeWebAcrossTracks_AndNotCpuCycle()
    {
        var prerequisites = _catalog.PrerequisitesOf(new ThematicSliceKey("static-page-1995", "frontend"));

        Assert.Equal([new ThematicSliceKey("net-before-web-1992", "computer-networks")], prerequisites);
        Assert.DoesNotContain(prerequisites, p => p.SliceId == "cpu-cycle");
    }

    [Fact]
    public async Task Done_OnNetBeforeWeb_OpensStaticPageAcrossTracks()
    {
        using var fx = new ProgressFixture(_catalog);
        Assert.Equal(SliceState.Unavailable, (await fx.StatusOf("static-page-1995@frontend")).State);

        Assert.True(await fx.Service.MarkDoneAsync(ProgressFixture.UserId, new("net-before-web-1992", "computer-networks")));

        Assert.Equal(SliceState.Available, (await fx.StatusOf("static-page-1995@frontend")).State);
        Assert.Equal(SliceState.Unavailable, (await fx.StatusOf("forms-cgi-1995@frontend")).State);
    }

    [Fact]
    public void Directory_IntroducedInNetBeforeWeb_PlayedForwardToStaticPage()
    {
        var directory = _catalog.Examples["directory"];

        Assert.Equal("net-before-web-1992", directory.IntroducedInSlice);
        Assert.Equal(["directory"], _catalog.Slices["net-before-web-1992"].Examples);
        Assert.Equal(["directory"], _catalog.Slices["static-page-1995"].Examples);
        Assert.Equal(
            [("ftp-1992", "net-before-web-1992", "emulated"), ("gopher-1992", "net-before-web-1992", "emulated"),
             ("web-1995", "static-page-1995", "executable")],
            directory.Implementations.Select(i => (i.Id, i.Slice, i.CodeMode)));
    }

    [Fact]
    public void Directory_WebPage_ShowsTheSameFileItRuns()
    {
        var web = _catalog.Examples["directory"].Implementations.Single(i => i.Id == "web-1995");

        Assert.Contains(web.CodeFiles, c => c.Path == web.Entry);
    }

    [Fact]
    public void Directory_Imitations_EmbedVerbatimCopyOfCodeFile()
    {
        // Имитация в iframe не может прочесть code/*.txt (sandbox без allow-same-origin), поэтому
        // держит его копию в <script type="text/plain" data-code="...">; копия не должна разойтись с показом.
        foreach (var impl in _catalog.Examples["directory"].Implementations.Where(i => i.CodeMode == "emulated"))
        {
            var html = File.ReadAllText(Path.Combine(impl.Directory, impl.Entry));
            var code = Assert.Single(impl.CodeFiles);
            var block = Regex.Match(html, $"<script type=\"text/plain\" id=\"\\w+\" data-code=\"{Regex.Escape(code.Path)}\">(.*?)</script>",
                RegexOptions.Singleline);

            Assert.True(block.Success, $"{impl.Id}: нет встроенной копии {code.Path}");
            Assert.Equal(File.ReadAllText(Path.Combine(impl.Directory, code.Path)), block.Groups[1].Value);
            foreach (var file in Directory.GetFiles(impl.Directory, "*.js").Prepend(Path.Combine(impl.Directory, impl.Entry)))
                Assert.Contains("средство визуализации, не код эпохи", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Directory_StopFrames_PointAtExistingLinesOfCodeFiles()
    {
        foreach (var impl in _catalog.Examples["directory"].Implementations)
            foreach (var frame in impl.Processes.SelectMany(p => p.Process!.Frames).Where(f => f.Stop))
            {
                var d = frame.Detail!;
                Assert.Contains(impl.CodeFiles, c => c.Path == d.CodeFile);
                var count = File.ReadAllLines(Path.Combine(impl.Directory, d.CodeFile!)).Length;
                var range = d.Lines!.Split('-').Select(int.Parse).ToArray();
                Assert.True(range[0] >= 1 && range[^1] >= range[0] && range[^1] <= count,
                    $"{impl.Id}, кадр {frame.Order}: строки {d.Lines} вне 1..{count} в {d.CodeFile}");
            }
    }
}
