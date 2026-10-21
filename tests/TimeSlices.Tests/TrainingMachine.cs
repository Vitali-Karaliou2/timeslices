using System.Text.RegularExpressions;

namespace TimeSlices.Tests;

/// <summary>
/// Маленький интерпретатор учебной машины с аккумулятором (реализации stored-program-1949*, описание hw-04, раздел 5) — только для тестов:
/// программы для показа (code/squares.txt, code/cubes.txt) должны давать то, что видит обучаемый. Машина в iframe
/// (machine.js) — отдельная реализация тех же правил; обе читают один листинг.
/// </summary>
public sealed class TrainingMachine
{
    public const int Size = 128;
    public static readonly string Letters = "ZASTUEGO"; // код операции = позиция буквы

    public int[] Memory { get; } = new int[Size];
    /// <summary>Роль ячейки по листингу: 'c' — команда, 'd' — число, '\0' — таблица или ячейка вне листинга.</summary>
    public char[] Kinds { get; } = new char[Size];
    public int Accumulator { get; private set; }
    public int Counter { get; private set; }
    public int Ticks { get; private set; }

    /// <summary>Загрузить листинг: «адрес  буква [адрес]  слово  смысл» и «адрес[–адрес]  число  смысл».</summary>
    public static TrainingMachine Load(IEnumerable<string> listing)
    {
        var m = new TrainingMachine();
        foreach (var line in listing)
        {
            var command = Regex.Match(line, @"^\s*(\d+)\s+([A-Z])(?:\s+(\d+))?\s+(\d+)");
            if (command.Success)
            {
                var word = Letters.IndexOf(command.Groups[2].Value[0]) * 1000 + (command.Groups[3].Success ? int.Parse(command.Groups[3].Value) : 0);
                Assert.True(Letters.Contains(command.Groups[2].Value[0]), $"неизвестная команда: {line}");
                Assert.True(word == int.Parse(command.Groups[4].Value), $"слово не совпадает с командой: {line}");
                m.Memory[int.Parse(command.Groups[1].Value)] = word;
                m.Kinds[int.Parse(command.Groups[1].Value)] = 'c';
                continue;
            }
            var data = Regex.Match(line, @"^\s*(\d+)(?:–(\d+))?\s+(-?\d+)\s");
            if (data.Success)
            {
                var from = int.Parse(data.Groups[1].Value);
                var to = data.Groups[2].Success ? int.Parse(data.Groups[2].Value) : from;
                for (var a = from; a <= to; a++)
                    m.Memory[a] = int.Parse(data.Groups[3].Value);
                if (!data.Groups[2].Success)
                    m.Kinds[from] = 'd';
            }
        }
        return m;
    }

    /// <summary>Исполнять с ячейки 0 до команды Z; один такт — выборка и исполнение одной команды.</summary>
    public void Run(int maxTicks = 100_000)
    {
        Counter = 0;
        while (Ticks < maxTicks)
        {
            var word = Memory[Counter++];
            Ticks++;
            var (op, addr) = (word / 1000, word % 1000);
            Assert.True(word >= 0 && op < Letters.Length && (op == 0 || addr < Size), $"не команда в ячейке {Counter - 1}: {word}");
            switch (Letters[op])
            {
                case 'Z': return;
                case 'A': Accumulator += Memory[addr]; break;
                case 'S': Accumulator -= Memory[addr]; break;
                case 'T': Memory[addr] = Accumulator; Accumulator = 0; break;
                case 'U': Memory[addr] = Accumulator; break;
                case 'E': if (Accumulator >= 0) Counter = addr; break;
                case 'G': if (Accumulator < 0) Counter = addr; break;
                case 'O': break; // печать: программы squares и cubes её не используют
            }
        }
        Assert.Fail($"нет остановки за {maxTicks} тактов");
    }

    public static string LettersOf(int word) =>
        word / 1000 == 0 && word % 1000 == 0 ? "Z" : $"{Letters[word / 1000]} {word % 1000}";
}
