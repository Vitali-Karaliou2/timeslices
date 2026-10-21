using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Реализация register-machine-1965 примера squares (план сессии 08, ч. В; docs/spec/register-machine.md).</summary>
public class RegisterMachineTests
{
    private readonly ContentCatalog _catalog = ContentLoader.Load(TestContent.RealContentRoot());

    private Implementation Impl => _catalog.Examples["squares"].Implementations.Single(i => i.Id == "register-machine-1965");

    private string[] Listing(string file) => File.ReadAllLines(Path.Combine(Impl.Directory, file));

    [Fact]
    public void Implementation_IsEmulated_InRegisterMachineSlice_WithTenFrames()
    {
        Assert.Equal(("register-machine-1965", "emulated"), (Impl.Slice, Impl.CodeMode));
        Assert.Equal(["code/squares.txt", "code/cubes.txt"], Impl.CodeFiles.Select(c => c.Path));
        var process = Impl.Processes.Single().Process!;
        Assert.Equal("fetch-execute", process.Id);
        Assert.Equal(10, process.Frames.Count);
        Assert.Equal("1965-01-01T10:00:00", process.StartAt);
        Assert.Equal(6, _catalog.QuestionsOf("register-machine-1965").Count);
    }

    [Theory]
    [InlineData("code/squares.txt", 10, 64, 2)]
    [InlineData("code/squares.txt", 100, 604, 2)]
    [InlineData("code/cubes.txt", 10, 75, 3)]
    [InlineData("code/cubes.txt", 100, 705, 3)]
    public void Program_ComputesTable_AndLeavesItselfUnchanged(string file, int n, int ticks, int power)
    {
        // Спецификация, раздел 4: «проверено интерпретатором 21.10.2026» — 64 / 604 и 75 / 705 тактов.
        var machine = RegisterMachine.Load(Listing(file), n);
        var before = machine.Memory.ToArray();

        machine.Run();

        Assert.Null(machine.Error);
        Assert.Equal(Enumerable.Range(0, n).Select(i => (int)Math.Pow(i, power)), machine.Memory.Skip(28).Take(n));
        Assert.All(machine.Memory.Skip(28 + n), v => Assert.Equal(0, v));
        Assert.Equal(ticks, machine.Ticks);
        // Программа не переписывает себя: ячейки 0–27 те же, что после загрузки (hw-03, кадр 8).
        Assert.Equal(before.Take(28), machine.Memory.Take(28));
        Assert.All(machine.Memory, v => Assert.InRange(v, -999_999, 999_999));
    }

    [Theory]
    [InlineData("code/squares.txt", 10)]
    [InlineData("code/cubes.txt", 12)]
    public void ListingWords_AreOpTimes10000PlusRegTimes1000PlusAddress(string file, int count)
    {
        var entries = RegisterMachine.Entries(Listing(file)).ToList();

        Assert.Equal(Enumerable.Range(0, count), entries.Select(e => e.Address));
        Assert.All(entries, e => Assert.Equal(RegisterMachine.Encode(e.Command), e.Word));
        Assert.Equal(40028, RegisterMachine.Encode("STOREX A, 28"));
        Assert.Equal(50001, RegisterMachine.Encode("ADD A, B"));
    }

    [Fact]
    public void Listings_AreVerbatimFromSpecSection4()
    {
        // ADR 0006 и план сессии 08, п. 12: листинги раздела 4 спецификации дословно (после строки-заголовка).
        var spec = File.ReadAllText(Path.Combine(_catalog.Root, "..", "..", "..", "docs", "spec", "register-machine.md"));
        foreach (var file in new[] { "code/squares.txt", "code/cubes.txt" })
        {
            var body = string.Join("\n", Listing(file).Skip(2)) + "\n";
            Assert.Contains("```\n" + body + "```", spec);
        }
    }

    [Fact]
    public void LessOrEqualInsteadOfLess_WritesPastTable_AndStopsAt128()
    {
        // Вопрос 3 среза: условие «≤» — 11 чисел при N = 10 (последнее в ячейке 38), при N = 100 — запись в 128, которой нет.
        // «≤» на этой машине: сравнить с N + 1 — ячейка 21.
        string[] Lte(string[] listing) => listing.Select(l => l.Replace("CMP    X, 20    83020", "CMP    X, 21    83021")).ToArray();
        var ten = RegisterMachine.Load(Lte(Listing("code/squares.txt")), 10);
        ten.Memory[21] = 11;
        ten.Run();
        Assert.Null(ten.Error);
        Assert.Equal(100, ten.Memory[38]);

        var hundred = RegisterMachine.Load(Lte(Listing("code/squares.txt")), 100);
        hundred.Memory[21] = 101;
        hundred.Run();
        Assert.Equal("запись за пределы памяти", hundred.Error);
    }
}

/// <summary>Панель «Два столбца» среза edsac-1949-deep (план сессии 08, ч. Д).</summary>
public class CompareColumnsTests
{
    private readonly ContentCatalog _catalog = ContentLoader.Load(TestContent.RealContentRoot());

    private string Dir(string impl) => _catalog.Examples["squares"].Implementations.Single(i => i.Id == impl).Directory;

    private static readonly System.Text.RegularExpressions.Regex Row = new(@"^\s*(\d+) \| (.*?)\s*\| (.*?)\s*\|\s?(.*)$");
    private static readonly System.Text.RegularExpressions.Regex Command = new(@"^\s*(\d+)  (.+)$");

    [Fact]
    public void Groups_CoverAll17RightAnd10Left_WithoutOverlap()
    {
        var rows = File.ReadAllLines(Path.Combine(Dir("stored-program-1949"), "code/compare.txt"))
            .Select(l => Row.Match(l)).Where(m => m.Success).ToList();
        var left = new List<(int Group, int Address, string Text)>();
        var right = new List<(int Group, int Address, string Text)>();
        foreach (var r in rows)
        {
            var g = int.Parse(r.Groups[1].Value);
            if (Command.Match(r.Groups[2].Value) is { Success: true } l)
                left.Add((g, int.Parse(l.Groups[1].Value), l.Groups[2].Value.Trim()));
            if (Command.Match(r.Groups[3].Value) is { Success: true } c)
                right.Add((g, int.Parse(c.Groups[1].Value), c.Groups[2].Value.Trim()));
        }

        // Каждая команда — ровно в одной группе (без пересечений), и покрыты все.
        Assert.Equal(Enumerable.Range(0, 10), left.Select(x => x.Address).Order());
        Assert.Equal(Enumerable.Range(0, 17), right.Select(x => x.Address).Order());
        Assert.Equal(8, rows.Select(r => r.Groups[1].Value).Distinct().Count());
        // Слева — листинг машины с регистрами, справа — листинг этой машины, команда в команду.
        var registers = RegisterMachine.Entries(File.ReadAllLines(Path.Combine(Dir("register-machine-1965"), "code/squares.txt")))
            .ToDictionary(e => e.Address, e => e.Command);
        Assert.All(left, x => Assert.Equal(registers[x.Address], System.Text.RegularExpressions.Regex.Replace(x.Text, @"\s+", " ")));
        var accumulator = TrainingMachine.Load(File.ReadAllLines(Path.Combine(Dir("stored-program-1949"), "code/squares.txt")));
        Assert.All(right, x => Assert.Equal(TrainingMachine.LettersOf(accumulator.Memory[x.Address]), x.Text));
        // Цикл: STOREX…BLT (3–8) слева — 6 команд; справа E 0 возвращает к ячейке 0 — цикл 0–15, 16 команд
        // (161 такт = 10 × 16 + 1). В спецификации, раздел 6, и hw-04, кадр 10, — «15»: расхождение в отчёте сессии 08.
        Assert.Equal(6, left.Count(x => x.Group is >= 3 and <= 7));
        Assert.Equal(16, right.Count(x => x.Group is >= 3 and <= 7));
    }

    [Fact]
    public void Panel_OnlyInFullMode_AndFrame10PointsAtCompare()
    {
        var full = _catalog.Examples["squares"].Implementations.Single(i => i.Id == "stored-program-1949");
        Assert.Equal(["code/squares.txt", "code/cubes.txt", "code/compare.txt"], full.CodeFiles.Select(c => c.Path));
        Assert.Contains("id=\"compare-table\"", File.ReadAllText(Path.Combine(Dir("stored-program-1949"), "index.html")));
        var overview = File.ReadAllText(Path.Combine(Dir("stored-program-1949-overview"), "index.html"));
        Assert.DoesNotContain("id=\"compare-table\"", overview);
        Assert.DoesNotContain("code/compare.txt", overview);

        var frames = full.Processes.Single().Process!.Frames;
        Assert.Equal(10, frames.Count);
        Assert.Equal("code/compare.txt", frames.Single(f => f.Order == 10).Detail!.CodeFile);
    }
}
