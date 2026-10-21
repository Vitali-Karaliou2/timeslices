using System.Diagnostics;
using System.Text.Json;
using TimeSlices.Web.Collection;
using Xunit.Abstractions;

namespace TimeSlices.Tests;

/// <summary>Режим «Своя программа»: ассемблер (план сессии 08, ч. Г; docs/spec/register-machine.md, разделы 7–8).</summary>
public class RegisterAssemblerTests(ITestOutputHelper output)
{
    private static readonly string Repo = Path.GetFullPath(Path.Combine(TestContent.RealContentRoot(), "..", "..", ".."));
    private static readonly string FixturePath = Path.Combine(Repo, "tests", "TimeSlices.Tests", "Fixtures", "register-assembler.json");

    private readonly ContentCatalog _catalog = ContentLoader.Load(TestContent.RealContentRoot());

    private string ImplDir => _catalog.Examples["squares"].Implementations.Single(i => i.Id == "register-machine-1965").Directory;

    private sealed record Case(string Name, string Source, int[][]? Words, string[]? Errors);

    private static List<Case> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c => new Case(
            c.GetProperty("name").GetString()!, c.GetProperty("source").GetString()!,
            c.TryGetProperty("words", out var w) ? w.EnumerateArray().Select(p => p.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray() : null,
            c.TryGetProperty("errors", out var e) ? e.EnumerateArray().Select(x => x.GetString()!).ToArray() : null)).ToList();
    }

    [Fact]
    public void Assembler_MatchesFixtureTable()
    {
        var cases = Cases();
        Assert.True(cases.Count(c => c.Errors is not null) >= 5, "ошибочных строк должно быть не меньше пяти");
        foreach (var c in cases)
        {
            var result = RegisterAssembler.Assemble(c.Source);
            if (c.Errors is { } errors)
            {
                Assert.Equal(errors, result.Errors);
                Assert.Empty(result.Cells);
            }
            else
            {
                Assert.Empty(result.Errors);
                Assert.Equal(c.Words!.Select(w => (w[0], w[1])), result.Cells);
            }
        }
    }

    [Theory]
    [InlineData("squares", "code/squares.txt")]
    [InlineData("cubes", "code/cubes.txt")]
    public void FixturePrograms_GiveTheWordsOfTheShownListings(string name, string file)
    {
        // Ассемблер переводит обе программы раздела 4 в те же слова, что в листингах (спецификация, раздел 8).
        var listing = RegisterMachine.Entries(File.ReadAllLines(Path.Combine(ImplDir, file))).Select(e => (e.Address, e.Word));
        var source = Cases().Single(c => c.Name == name).Source;

        Assert.Equal(listing, RegisterAssembler.Assemble(source).Cells);
    }

    [Fact]
    public void Polynomial_ReferenceFromSection7_FillsTable()
    {
        // Эталон самопроверки (спецификация, раздел 7): N = 50 → ячейки 28–77 = −7, 1, 15, 35, 61, …, 7441; 306 тактов.
        var spec = File.ReadAllText(Path.Combine(Repo, "docs", "spec", "register-machine.md"));
        var section7 = spec[spec.IndexOf("## 7.", StringComparison.Ordinal)..spec.IndexOf("## 8.", StringComparison.Ordinal)];
        var listing = RegisterMachine.Entries(section7.Split('\n')).Select(e => (e.Address, e.Word)).ToList();
        var cells = RegisterAssembler.Assemble(Cases().Single(c => c.Name == "polynomial").Source).Cells;
        Assert.Equal(listing, cells);

        var machine = new RegisterMachine();
        foreach (var (address, word) in cells)
            machine.Memory[address] = word;
        machine.Memory[20] = 50;
        machine.Run();

        Assert.Null(machine.Error);
        Assert.Equal(Enumerable.Range(0, 50).Select(x => 3 * x * x + 5 * x - 7), machine.Memory.Skip(28).Take(50));
        Assert.Equal(7441, machine.Memory[77]);
        Assert.Equal(306, machine.Ticks);
    }

    [Fact]
    public void Machine_StopsAfter20000Ticks()
    {
        // Защита от зацикливания (спецификация, раздел 8): BLT 0 при LT = да — бесконечный цикл.
        var cells = RegisterAssembler.Assemble("LOADI A, 0\nCMP A, 20\nBLT 0").Cells;
        var machine = new RegisterMachine();
        foreach (var (address, word) in cells)
            machine.Memory[address] = word;
        machine.Memory[20] = 10;

        machine.Run(maxTicks: 20_000);

        Assert.Equal(20_000, machine.Ticks);
        var js = File.ReadAllText(Path.Combine(ImplDir, "machine.js"));
        Assert.Contains("var MAX_TICKS = 20000;", js);
    }

    [Fact]
    public void AssemblerJs_MatchesFixtureTable_WhenNodeIsAvailable()
    {
        // Сам assembler.js из C# без новых зависимостей не исполнить; если node установлен, тест запускает
        // scripts/check_assembler.js на той же таблице. Без node сверка JS — вручную тем же скриптом.
        var psi = new ProcessStartInfo("node", $"\"{Path.Combine(Repo, "scripts", "check_assembler.js")}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output.WriteLine("node не найден: assembler.js не сверен с таблицей (запустите node scripts/check_assembler.js).");
            return;
        }
        using (process)
        {
            var stdout = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout);
            output.WriteLine(stdout);
        }
    }
}
