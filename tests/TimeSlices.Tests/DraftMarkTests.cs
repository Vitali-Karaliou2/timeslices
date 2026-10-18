using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Пометка «ЧЕРНОВИК»: в файлах — префикс, в интерфейсе — один значок у блока (план сессии 04, часть 1, п. 2).</summary>
public class DraftMarkTests
{
    [Theory]
    [InlineData("ЧЕРНОВИК: текст", true, "текст")]
    [InlineData("  ЧЕРНОВИК:текст", true, "текст")]
    [InlineData("текст", false, "текст")]
    [InlineData("текст. ЧЕРНОВИК: в середине", false, "текст. ЧЕРНОВИК: в середине")]
    [InlineData("черновик: строчными", false, "черновик: строчными")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void Strip_RemovesOnlyLeadingPrefix(string? text, bool draft, string shown)
    {
        Assert.Equal(draft, DraftMark.IsDraft(text));
        Assert.Equal(shown, DraftMark.Strip(text));
    }

    [Fact]
    public void Badge_OnePerBlock_EmptyForApprovedText()
    {
        Assert.Contains("draft-badge", DraftMark.BadgeFor("текст", "ЧЕРНОВИК: другой").ToString());
        Assert.Equal("", DraftMark.BadgeFor("текст", null).ToString());
        Assert.Null(DraftMark.StripOrNull("ЧЕРНОВИК: "));
    }

    [Fact]
    public void Schedule_StripsPrefixFromFrameTexts_AndMarksFrameOnce()
    {
        var process = new Process
        {
            Id = "p", Type = "execution",
            Frames =
            [
                new() { Order = 1, Text = "ЧЕРНОВИК: кадр", DurationNote = "ЧЕРНОВИК: 1 с", Stop = true,
                        Detail = new() { CodeFile = "code/a.txt", Lines = "1", Explanation = "ЧЕРНОВИК: пояснение" } },
                new() { Order = 2, Text = "утверждённый кадр" },
                new() { Order = 3, Text = "кадр", Stop = true, Detail = new() { Explanation = "ЧЕРНОВИК: только пояснение" } },
            ],
        };

        var steps = ProcessSchedule.Build(process);

        Assert.Equal(("кадр", "1 с", "пояснение"), (steps[0].Text, steps[0].DurationNote, steps[0].Detail!.Explanation));
        Assert.Equal([true, false, true], steps.Select(s => s.Draft));
        Assert.Equal("только пояснение", steps[2].Detail!.Explanation);
    }
}
