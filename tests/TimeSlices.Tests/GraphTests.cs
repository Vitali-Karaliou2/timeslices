using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Граф срезов (docs/acceptance.md, п. 1 и 7).</summary>
public class GraphTests
{
    [Fact]
    public void RealContent_LoadsAsDagWithPrerequisitesBeforeSuccessors()
    {
        var catalog = ContentLoader.Load(TestContent.RealContentRoot());

        Assert.NotEmpty(catalog.ThematicSlices);
        var position = catalog.ThematicSlices.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        foreach (var key in catalog.ThematicSlices)
            foreach (var p in catalog.PrerequisitesOf(key))
                Assert.True(position[p] < position[key], $"{p} должен идти раньше {key}");
        // Каждый срез входит хотя бы в один трек, и каждый тематический срез виден в своём треке.
        Assert.All(catalog.Slices.Values, s => Assert.NotEmpty(s.Tracks));
        Assert.Equal(catalog.ThematicSlices.Count, catalog.Tracks.Sum(t => catalog.SlicesInTrack(t.Id).Count()));
    }

    [Fact]
    public void Cycle_FailsAtLoadWithSlicesOfTheCycle()
    {
        using var content = new TestContent().Track("t")
            .Slice("a", "t", "c@t")
            .Slice("b", "t", "a@t")
            .Slice("c", "t", "b@t");

        var ex = Assert.Throws<ContentLoadException>(content.Load);

        var problem = Assert.Single(ex.Problems);
        Assert.Contains("цикл", problem);
        Assert.Contains("a → c → b → a", problem);
    }

    [Theory]
    [InlineData("missing@t", "предшественник 'missing' не найден")]
    [InlineData("a@other", "не входит в трек 'other'")]
    [InlineData("b@t", "предшественником самого себя")]
    public void BrokenPrerequisite_FailsAtLoad(string prerequisite, string expected)
    {
        using var content = new TestContent().Track("t").Track("other")
            .Slice("a", "t")
            .Slice("b", "t", prerequisite);

        var ex = Assert.Throws<ContentLoadException>(content.Load);

        Assert.Contains(ex.Problems, p => p.Contains(expected));
    }

    [Fact]
    public void UnknownTrack_FailsAtLoad()
    {
        using var content = new TestContent().Track("t").Slice("a", "nope");

        var ex = Assert.Throws<ContentLoadException>(content.Load);

        Assert.Contains(ex.Problems, p => p.Contains("трек 'nope' не объявлен"));
    }

    [Fact]
    public void NewSliceFolder_AppearsWithoutCodeChanges()
    {
        using var content = new TestContent().Track("t").Slice("a", "t");
        Assert.Single(content.Load().Slices);

        content.Slice("b", "t", "a@t");
        var catalog = content.Load();

        Assert.Equal(["a", "b"], catalog.SlicesInTrack("t").Select(k => k.SliceId));
        Assert.Equal([new ThematicSliceKey("b", "t")], catalog.SuccessorsOf(new ThematicSliceKey("a", "t")));
    }

    [Fact]
    public void SliceInTwoTracks_IsTwoThematicSlicesWithCrossTrackEdge()
    {
        using var content = new TestContent().Track("hw").Track("fe")
            .Slice("cpu", "hw")
            .Slice("page", ["fe", "hw"], ["cpu@hw"]);

        var catalog = content.Load();

        Assert.Equal(3, catalog.ThematicSlices.Count);
        Assert.Equal(2, catalog.SuccessorsOf(new ThematicSliceKey("cpu", "hw")).Count);
    }
}
