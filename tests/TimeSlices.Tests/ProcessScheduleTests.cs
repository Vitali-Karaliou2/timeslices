using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Расписание проигрывания процесса (план сессии 05, часть 2, п. 1).</summary>
public class ProcessScheduleTests
{
    private static Frame F(int order, string? at, bool stop = false, FrameDetail? detail = null) =>
        new() { Order = order, At = at, Text = $"кадр {order}", Stop = stop, Detail = detail };

    private static Process P(params Frame[] frames) => new() { Id = "p", Type = "execution", Frames = [.. frames] };

    [Fact]
    public void Steps_FollowFrameOrder_NotFileOrder()
    {
        var steps = ProcessSchedule.Build(P(F(3, "1992-10-05T14:00:20"), F(1, "1992-10-05T14:00:00"), F(2, "1992-10-05T14:00:10")));

        Assert.Equal([1, 2, 3], steps.Select(s => s.Order));
        Assert.Equal([10.0, 10.0, null], steps.Select(s => s.GapSeconds));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(22)]
    [InlineData(12_000)]
    [InlineData(86_400 * 3)]
    public void Delay_IsCompressedInto_0_6_To_3_Seconds(int gapSeconds)
    {
        var delay = ProcessSchedule.DelayFor(TimeSpan.FromSeconds(gapSeconds));

        Assert.InRange(delay, ProcessSchedule.MinDelayMs, ProcessSchedule.MaxDelayMs);
        Assert.Equal(600, ProcessSchedule.MinDelayMs);
        Assert.Equal(3000, ProcessSchedule.MaxDelayMs);
    }

    [Fact]
    public void Delay_KeepsOrderOfGaps_AndHoursTakeAtMostThreeSeconds()
    {
        var delays = new[] { 1, 3, 7, 60, 3600, 12_000 }.Select(s => ProcessSchedule.DelayFor(TimeSpan.FromSeconds(s))).ToArray();

        Assert.True(delays[0] < delays[1] && delays[1] < delays[2] && delays[2] < delays[3], string.Join(", ", delays));
        Assert.Equal(3000, delays[4]);
        Assert.Equal(3000, delays[5]);
    }

    [Theory]
    [InlineData(60, null)]
    [InlineData(61, "прошло 1 мин 1 с")]
    [InlineData(90, "прошло 1 мин 30 с")]
    [InlineData(25 * 60, "прошло 25 мин")]
    [InlineData(3600, "прошло 1 ч")]
    [InlineData(3 * 3600 + 20 * 60, "прошло 3 ч 20 мин")]
    [InlineData(3 * 3600 + 20 * 60 + 15, "прошло 3 ч 20 мин")]
    [InlineData(26 * 3600 + 5, "прошло 1 сут 2 ч")]
    public void LongGaps_GetRussianLabel_WithoutSecondsAfterAnHour(int gapSeconds, string? label)
    {
        var steps = ProcessSchedule.Build(P(F(1, "1972-03-14T09:00:00"), F(2, $"{new DateTime(1972, 3, 14, 9, 0, 0).AddSeconds(gapSeconds):s}")));

        Assert.Equal(label, steps[0].GapLabel);
        Assert.True(steps[0].DelayMs <= ProcessSchedule.MaxDelayMs);
    }

    [Fact]
    public void FramesWithoutTime_OrWithSameTime_GoWithMinimalDelay()
    {
        var steps = ProcessSchedule.Build(P(
            F(1, "1995-10-01T09:00:01"), F(2, "1995-10-01T09:00:01"), F(3, null), F(4, "1995-10-01T09:00:08"), F(5, "")));

        Assert.All(steps.Take(4), s => Assert.Equal(ProcessSchedule.MinDelayMs, s.DelayMs));
        Assert.Equal([0.0, null, null, null, null], steps.Select(s => s.GapSeconds));
        Assert.All(steps, s => Assert.Null(s.GapLabel));
        Assert.Null(steps[2].AtDisplay);
    }

    [Fact]
    public void LastFrame_HasNoDelay()
    {
        var steps = ProcessSchedule.Build(P(F(1, "1992-10-05T14:00:00"), F(2, "1992-10-05T14:00:30")));

        Assert.Equal(0, steps[^1].DelayMs);
        Assert.Null(steps[^1].GapSeconds);
    }

    [Fact]
    public void StopFrames_AlwaysStop_AndCarryCodeRange()
    {
        var steps = ProcessSchedule.Build(P(
            F(1, "1992-10-05T14:00:00", stop: true, new FrameDetail { CodeFile = "code/session.txt", Lines = "3-9", Explanation = "сеанс" }),
            F(2, "1992-10-05T14:00:03"),
            F(3, "1992-10-05T14:00:10", stop: true, new FrameDetail { CodeFile = "code/session.txt", Lines = "32", Explanation = "поиск" })));

        Assert.Equal([true, false, true], steps.Select(s => s.Stop));
        Assert.Equal(new StopDetail("code/session.txt", "3-9", 3, 9, "сеанс"), steps[0].Detail);
        Assert.Equal((32, 32), (steps[2].Detail!.LineFrom, steps[2].Detail!.LineTo));
        Assert.Null(steps[1].Detail);
        // Стоп-кадр тоже знает свою задержку: «Дальше» продолжает с неё.
        Assert.True(steps[0].DelayMs > ProcessSchedule.MinDelayMs);
    }

    [Theory]
    [InlineData("12-20", 12, 20)]
    [InlineData("32", 32, 32)]
    [InlineData("20-12", null, null)]
    [InlineData("0-3", null, null)]
    [InlineData("a-b", null, null)]
    [InlineData("1-2-3", null, null)]
    [InlineData(null, null, null)]
    public void Lines_AreParsedOrIgnored(string? lines, int? from, int? to)
    {
        var range = ProcessSchedule.ParseLines(lines);

        Assert.Equal(from, range?.From);
        Assert.Equal(to, range?.To);
    }

    [Fact]
    public void Time_IsShownInRussian()
    {
        Assert.Equal("5 октября 1992, 14:00:03", ProcessSchedule.FormatAt(new DateTime(1992, 10, 5, 14, 0, 3)));
    }

    [Fact]
    public void RealProcesses_HaveStepForEveryFrame_AndStopDetailsWithLines()
    {
        var catalog = ContentLoader.Load(TestContent.RealContentRoot());
        foreach (var pr in catalog.Examples["directory"].Implementations.SelectMany(i => i.Processes))
        {
            var steps = ProcessSchedule.Build(pr.Process!);
            Assert.Equal(pr.Process!.Frames.Count, steps.Count);
            Assert.All(steps.Where(s => s.Stop), s => Assert.NotNull(s.Detail?.LineFrom));
            Assert.All(steps.SkipLast(1), s => Assert.InRange(s.DelayMs, ProcessSchedule.MinDelayMs, ProcessSchedule.MaxDelayMs));
        }
    }
}
