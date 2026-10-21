using System.Text.RegularExpressions;

namespace TimeSlices.Tests;

/// <summary>
/// Интерпретатор учебной машины с регистрами (docs/spec/register-machine.md) — только для тестов:
/// программы для показа (code/squares.txt, code/cubes.txt) должны давать то, что видит обучаемый.
/// Машина в iframe (machine.js) — отдельная реализация тех же правил; обе читают один листинг.
/// </summary>
public sealed class RegisterMachine
{
    public const int Size = 128;
    public static readonly string[] Ops = ["HALT", "LOAD", "LOADI", "STORE", "STOREX", "ADD", "ADDI", "SUB", "CMP", "BLT"];
    public static readonly string[] Regs = ["A", "B", "C", "X"];

    public int[] Memory { get; } = new int[Size];
    public bool[] IsProgram { get; } = new bool[Size];
    public int[] Reg { get; } = new int[4];
    public int Pc { get; private set; }
    public bool Lt { get; private set; }
    public int Ticks { get; private set; }

    /// <summary>Причина останова с ошибкой; null — машина остановилась по HALT.</summary>
    public string? Error { get; private set; }

    /// <summary>Строка листинга: «адрес  команда (16 знаков)  слово  смысл» — как разбирает machine.js.</summary>
    public static readonly Regex ListingLine = new(@"^\s*(\d+)  (.{16})\s*(\d+)(?:  |$)");

    public static RegisterMachine Load(IEnumerable<string> listing, int n)
    {
        var m = new RegisterMachine();
        foreach (var line in listing)
        {
            var match = ListingLine.Match(line);
            if (!match.Success)
                continue;
            var address = int.Parse(match.Groups[1].Value);
            m.Memory[address] = int.Parse(match.Groups[3].Value);
            m.IsProgram[address] = true;
        }
        m.Memory[20] = n;
        return m;
    }

    /// <summary>Строки листинга: (адрес, текст команды, слово).</summary>
    public static IEnumerable<(int Address, string Command, int Word)> Entries(IEnumerable<string> listing) =>
        listing.Select(l => ListingLine.Match(l)).Where(m => m.Success)
            .Select(m => (int.Parse(m.Groups[1].Value), Regex.Replace(m.Groups[2].Value.Trim(), @"\s+", " "), int.Parse(m.Groups[3].Value)));

    /// <summary>Слово команды по спецификации, раздел 1: КОП × 10000 + РЕГ × 1000 + АДРЕС.</summary>
    public static int Encode(string command)
    {
        var parts = command.Split(' ', 2);
        var op = Array.IndexOf(Ops, parts[0]);
        if (op < 0)
            throw new ArgumentException(command);
        if (op == 0)
            return 0;
        if (op == 9)
            return 90000 + int.Parse(parts[1]);
        var operands = parts[1].Split(", ");
        var r = Array.IndexOf(Regs, operands[0]);
        var a = op is 5 or 7 ? Array.IndexOf(Regs, operands[1]) : int.Parse(operands[1]);
        return op * 10000 + r * 1000 + a;
    }

    /// <summary>Прогон до HALT, ошибки или maxTicks тактов.</summary>
    public RegisterMachine Run(int maxTicks = 20_000)
    {
        while (Ticks < maxTicks && Step())
        {
        }
        return this;
    }

    /// <summary>Один такт; false — машина остановилась.</summary>
    public bool Step()
    {
        if (Pc >= Size)
            return Fail("счётчик команд вне памяти");
        var word = Memory[Pc++];
        Ticks++;
        if (word is < 0 or > 99_999)
            return Fail("не команда");
        int op = word / 10000, r = word / 1000 % 10, a = word % 1000;
        if (op == 0)
            return word == 0 ? false : Fail("не команда");
        if (op != 9 && r > 3 || op == 9 && r != 0 || op is 5 or 7 && a > 3)
            return Fail("не команда");
        switch (op)
        {
            case 1: return a < Size ? Set(r, Memory[a]) : Fail("нет ячейки");
            case 2: return Set(r, a);
            case 3:
                if (a >= Size)
                    return Fail("нет ячейки");
                Memory[a] = Reg[r];
                return true;
            case 4:
                var address = a + Reg[3];
                if (address is < 0 or >= Size)
                    return Fail("запись за пределы памяти");
                Memory[address] = Reg[r];
                return true;
            case 5: return Set(r, Reg[r] + Reg[a]);
            case 6: return Set(r, Reg[r] + a);
            case 7: return Set(r, Reg[r] - Reg[a]);
            case 8:
                if (a >= Size)
                    return Fail("нет ячейки");
                Lt = Reg[r] < Memory[a];
                return true;
            default:
                if (Lt)
                    Pc = a;
                return true;
        }
    }

    // Переполнение не проверяется, как и в machine.js: ±999 999 — условность показа (спецификация, раздел 1).
    private bool Set(int r, int value)
    {
        Reg[r] = value;
        return true;
    }

    private bool Fail(string error)
    {
        Error = error;
        return false;
    }
}
