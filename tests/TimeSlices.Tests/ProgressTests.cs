using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>Состояния тематических срезов (docs/acceptance.md, п. 3–5; brief 5.3).</summary>
public class ProgressTests : IDisposable
{
    // a ──► c ──► d
    // b ──┘   └─► e
    private readonly TestContent _content = new TestContent().Track("t")
        .Slice("a", "t")
        .Slice("b", "t")
        .Slice("c", "t", "a@t", "b@t")
        .Slice("d", "t", "c@t")
        .Slice("e", "t", "c@t");

    private readonly ProgressFixture _fx;

    public ProgressTests()
    {
        _fx = new ProgressFixture(_content.Load());
    }

    public void Dispose()
    {
        _fx.Dispose();
        _content.Dispose();
    }

    private static ThematicSliceKey K(string slice) => new(slice, "t");
    private Task<bool> Done(string slice) => _fx.Service.MarkDoneAsync(ProgressFixture.UserId, K(slice));

    [Fact]
    public async Task SliceIsUnavailable_UntilAllPrerequisitesAreDone()
    {
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("a@t")).State);
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("c@t")).State);

        await Done("a");
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("c@t")).State);

        await Done("b");
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("c@t")).State);
    }

    [Fact]
    public async Task UnavailableSlice_CannotBeOpenedOrMarkedDone()
    {
        var opened = await _fx.Service.OpenAsync(ProgressFixture.UserId, K("c"));

        Assert.Equal(SliceState.Unavailable, opened.State);
        Assert.False(await Done("c"));
        Assert.Empty(_fx.Db.UserSliceStates);
    }

    [Fact]
    public async Task Opening_AvailableSlice_PutsItInProgress()
    {
        await _fx.Service.OpenAsync(ProgressFixture.UserId, K("a"));

        Assert.Equal(SliceState.InProgress, (await _fx.StatusOf("a@t")).State);
    }

    [Fact]
    public async Task Done_MarksSliceDoneAndOpensNext()
    {
        await Done("a");
        await Done("b");
        Assert.True(await Done("c"));

        Assert.Equal(SliceState.Done, (await _fx.StatusOf("c@t")).State);
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("d@t")).State);
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("e@t")).State);
    }

    [Fact]
    public async Task Next_ReturnsOneOrOffersChoice()
    {
        await Done("a");
        Assert.Empty(await _fx.Service.GetNextAsync(ProgressFixture.UserId, K("a"))); // c ждёт ещё b

        await Done("b");
        Assert.Equal([K("c")], await _fx.Service.GetNextAsync(ProgressFixture.UserId, K("a")));

        await Done("c");
        Assert.Equal([K("d"), K("e")], await _fx.Service.GetNextAsync(ProgressFixture.UserId, K("c")));
    }

    [Fact]
    public async Task Repeat_WhenNotAdvanced_SliceIsSimplyInProgress()
    {
        await Done("a");
        await Done("b");
        await Done("c");

        Assert.False(await _fx.Service.HasAdvancedAsync(ProgressFixture.UserId, K("c")));
        Assert.True(await _fx.Service.RepeatAsync(ProgressFixture.UserId, K("c"), resetFollowing: false));

        Assert.Equal(new(SliceState.InProgress, false), await _fx.StatusOf("c@t"));
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("d@t")).State);
    }

    [Fact]
    public async Task Repeat_KeepFollowing_LeavesFollowingDoneAndMarksRepeat()
    {
        foreach (var s in new[] { "a", "b", "c", "d" })
            await Done(s);
        Assert.True(await _fx.Service.HasAdvancedAsync(ProgressFixture.UserId, K("c")));

        await _fx.Service.RepeatAsync(ProgressFixture.UserId, K("c"), resetFollowing: false);

        Assert.Equal(new(SliceState.InProgress, true), await _fx.StatusOf("c@t"));
        Assert.Equal(SliceState.Done, (await _fx.StatusOf("d@t")).State);
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("e@t")).State);

        // «Готово» после повтора снимает пометку «повторно».
        await Done("c");
        Assert.Equal(new(SliceState.Done, false), await _fx.StatusOf("c@t"));
        Assert.Equal(SliceState.Done, (await _fx.StatusOf("d@t")).State);
    }

    [Fact]
    public async Task Repeat_ResetFollowing_MakesAllFollowingUnavailable()
    {
        foreach (var s in new[] { "a", "b", "c", "d" })
            await Done(s);

        await _fx.Service.RepeatAsync(ProgressFixture.UserId, K("a"), resetFollowing: true);

        Assert.Equal(new(SliceState.InProgress, false), await _fx.StatusOf("a@t"));
        Assert.Equal(SliceState.Done, (await _fx.StatusOf("b@t")).State);
        foreach (var s in new[] { "c@t", "d@t", "e@t" })
            Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf(s)).State);

        // Сброс — это сброс: после повторного «Готово» на a срез d снова не пройден.
        await Done("a");
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("c@t")).State);
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("d@t")).State);
    }

    [Fact]
    public async Task Repeat_OnlyForDoneSlice()
    {
        Assert.False(await _fx.Service.RepeatAsync(ProgressFixture.UserId, K("a"), resetFollowing: false));
    }
}
