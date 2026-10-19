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
    public void Directory_WebPage_HttpTranscript_MatchesTheFileItServes()
    {
        // Расшифровка обмена (ADR 0007) — реконструкция, но длина в ответе должна совпадать с показанным файлом.
        var web = _catalog.Examples["directory"].Implementations.Single(i => i.Id == "web-1995");
        var http = File.ReadAllLines(Path.Combine(web.Directory, "code/http.txt"));

        Assert.Equal("GET /staff/ HTTP/1.0", http[0]);
        Assert.Contains("Content-type: text/html", http);
        Assert.Contains($"Content-length: {new FileInfo(Path.Combine(web.Directory, web.Entry)).Length}", http);
    }

    [Fact]
    public void Imitations_EmbedVerbatimCopyOfCodeFile()
    {
        // Имитация в iframe не может прочесть code/*.txt (sandbox без allow-same-origin), поэтому
        // держит его копию в <script type="text/plain" data-code="...">; копия не должна разойтись с показом (ADR 0006).
        var emulated = _catalog.Examples.Values.SelectMany(e => e.Implementations).Where(i => i.CodeMode == "emulated").ToList();
        Assert.Equal(["eniac-1946", "ftp-1992", "gopher-1992", "stored-program-1949"], emulated.Select(i => i.Id).Order(StringComparer.Ordinal));
        foreach (var impl in emulated)
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
    public void StopFrames_PointAtExistingLinesOfCodeFiles()
    {
        foreach (var impl in _catalog.Examples.Values.SelectMany(e => e.Implementations))
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

    [Fact]
    public void CpuCycle_HasSquaresWithTwoEmulatedImplementations_AndSixQuestions()
    {
        var slice = _catalog.Slices["cpu-cycle"];
        var squares = _catalog.Examples["squares"];

        Assert.Equal(["squares"], slice.Examples);
        Assert.Equal("a", slice.Relevance);
        Assert.DoesNotContain("stub-cpu-cycle", _catalog.Examples.Keys);
        Assert.Equal(("cpu-cycle", 1), (squares.IntroducedInSlice, squares.Complexity));
        Assert.Equal(
            [("eniac-1946", "cpu-cycle", "emulated", "setup-and-run"), ("stored-program-1949", "cpu-cycle", "emulated", "fetch-execute")],
            squares.Implementations.Select(i => (i.Id, i.Slice, i.CodeMode, i.Processes.Single().Id)));
        using var questions = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_catalog.Root, "slices", "cpu-cycle", "questions.json")));
        Assert.Equal(6, questions.RootElement.GetProperty("questions").GetArrayLength());
    }

    private string[] SquaresListing()
    {
        var impl = _catalog.Examples["squares"].Implementations.Single(i => i.Id == "stored-program-1949");
        return File.ReadAllLines(Path.Combine(impl.Directory, "code/squares.txt"));
    }

    [Theory]
    [InlineData(9, 161, "T 38")]
    [InlineData(99, 1601, "T 128")]
    public void Squares_ProgramShown_ComputesTableOfSquares(int counter, int ticks, string cell2After)
    {
        // Эталон прогона — план сессии 04, ч. 2, п. 5. Переключатель «10 / 100» меняет только ячейку 22.
        var machine = TrainingMachine.Load(SquaresListing());
        Assert.Equal(9, machine.Memory[22]);
        Assert.Equal("T 28", TrainingMachine.LettersOf(machine.Memory[2]));
        var before = machine.Memory.ToArray();
        machine.Memory[22] = counter;

        machine.Run();

        var n = counter + 1;
        Assert.Equal(Enumerable.Range(0, n).Select(k => k * k), machine.Memory.Skip(28).Take(n));
        Assert.All(machine.Memory.Skip(28 + n), v => Assert.Equal(0, v));
        Assert.Equal(ticks, machine.Ticks);
        Assert.Equal(cell2After, TrainingMachine.LettersOf(machine.Memory[2]));
        // Программа, кроме ячейки 2, не изменилась; числа — изменились (20–22).
        Assert.Equal(before.Take(17).Where((_, a) => a != 2), machine.Memory.Take(17).Where((_, a) => a != 2));
    }

    [Fact]
    public void Squares_ListingHas17ProgramWords_AndDataByTheDescription()
    {
        var machine = TrainingMachine.Load(SquaresListing());

        Assert.Equal(
            ["T 25", "A 20", "T 28", "A 2", "A 24", "T 2", "A 20", "A 21", "T 20", "A 21", "A 23", "T 21", "A 22", "S 24", "U 22", "E 0", "Z"],
            machine.Memory.Take(17).Select(TrainingMachine.LettersOf));
        Assert.Equal([0, 1, 9, 2, 1, 0], machine.Memory.Skip(20).Take(6));
    }

    [Fact]
    public void Eniac_ToggleChanges_PointAtLinesOfSetupSheet()
    {
        // eniac.js подменяет строки листа настройки для 100 квадратов; номер строки и текст должны совпадать с листом.
        var impl = _catalog.Examples["squares"].Implementations.Single(i => i.Id == "eniac-1946");
        var sheet = File.ReadAllLines(Path.Combine(impl.Directory, "code/setup-sheet.txt"));
        var changes = Regex.Matches(File.ReadAllText(Path.Combine(impl.Directory, "eniac.js")), "\\{ line: (\\d+), from: \"([^\"]+)\", to: \"([^\"]+)\" \\}");

        Assert.NotEmpty(changes);
        foreach (Match c in changes)
            Assert.Contains(c.Groups[2].Value, sheet[int.Parse(c.Groups[1].Value) - 1]);
    }
}
