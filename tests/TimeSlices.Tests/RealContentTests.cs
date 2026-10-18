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
             "rup-vs-iterative-2001", "static-page-1995"],
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
}
