using System.Globalization;
using System.Text.RegularExpressions;

namespace TimeSlices.Tests;

/// <summary>
/// Дубликат assembler.js (режим «Своя программа», docs/spec/register-machine.md, раздел 8) — только для тестов.
/// Тест на C# не может исполнить JS без новых зависимостей (план сессии 08, п. 16), поэтому оба ассемблера
/// сверяются с одной таблицей фикстур Fixtures/register-assembler.json; JS-сторону проверяет scripts/check_assembler.js.
/// Тексты сообщений здесь и в assembler.js должны совпадать дословно.
/// </summary>
public static class RegisterAssembler
{
    private const int Size = 128, NCell = 20, Table = 28, Limit = 999_999;
    private static readonly string[] Ops = RegisterMachine.Ops;
    private static readonly string[] Regs = RegisterMachine.Regs;
    private static readonly Dictionary<string, string> Forms = new()
    {
        ["HALT"] = "", ["LOAD"] = "ra", ["LOADI"] = "rn", ["STORE"] = "ra", ["STOREX"] = "ra",
        ["ADD"] = "rs", ["ADDI"] = "rn", ["SUB"] = "rs", ["CMP"] = "ra", ["BLT"] = "a",
    };

    private const string Field = "число в команде берётся из поля адреса, 0–999";
    private const string Negative = Field + "; отрицательную константу положите в ячейку памяти или получите вычитанием";

    private sealed class AsmError(string message) : Exception(message);

    public sealed record Result(IReadOnlyList<(int Address, int Word)> Cells, IReadOnlyList<string> Errors);

    public static Result Assemble(string text)
    {
        var cells = new List<(int, int)>();
        var errors = new List<string>();
        var taken = new Dictionary<int, int>();
        var next = 0;
        var lines = Regex.Split(text, "\r?\n");
        for (var i = 0; i < lines.Length; i++)
        {
            var line = i + 1;
            var body = Regex.Replace(lines[i], ";.*$", "").Trim();
            if (body.Length == 0)
                continue;
            try
            {
                var address = next;
                var m = Regex.Match(body, @"^(\S+?)\s*:\s*(.*)$");
                if (m.Success)
                {
                    address = Number(m.Groups[1].Value) ?? throw new AsmError($"адрес «{m.Groups[1].Value}» — не число");
                    if (address is < 0 or >= Size)
                        throw new AsmError($"адрес {address} вне памяти — ячейки 0–{Size - 1}");
                    body = m.Groups[2].Value.Trim();
                    if (body.Length == 0)
                        throw new AsmError($"после адреса {address} нет ни команды, ни числа");
                }
                if (address >= Size)
                    throw new AsmError($"программа не помещается в память — ячейки 0–{Size - 1}");
                if (address == NCell)
                    throw new AsmError($"программа налезает на ячейку {NCell} — в ней N, его задаёт поле «N»");
                if (address >= Table)
                    throw new AsmError($"программа налезает на таблицу — ячейки {Table}–{Size - 1} заполняет сама программа");
                if (taken.TryGetValue(address, out var by))
                    throw new AsmError($"ячейка {address} уже занята строкой {by}");
                int word;
                if (Number(body) is { } value)
                {
                    if (Math.Abs(value) > Limit)
                        throw new AsmError($"число {value} вне ±999 999");
                    word = value;
                }
                else
                {
                    word = Instruction(body);
                }
                taken[address] = line;
                cells.Add((address, word));
                next = address + 1;
            }
            catch (AsmError e)
            {
                errors.Add($"Строка {line}: {e.Message}.");
                next++;
            }
        }
        return new Result(errors.Count > 0 ? [] : cells, errors);
    }

    private static int? Number(string text)
    {
        var m = Regex.Match(text, @"^([+\-−]?)(\d+)$");
        if (!m.Success)
            return null;
        var n = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return m.Groups[1].Value is "-" or "−" ? -n : n;
    }

    private static string Got(string text) => text.Length > 0 ? $", а не «{text}»" : "";

    private static int Register(string text, string op)
    {
        var r = Array.IndexOf(Regs, text.ToUpperInvariant());
        return r >= 0 ? r : throw new AsmError($"нет регистра — после {op} нужен A, B, C или X{Got(text)}");
    }

    private static int Cell(string text, string op)
    {
        var a = Number(text) ?? throw new AsmError($"после {op} нужен адрес ячейки — число 0–{Size - 1}{Got(text)}");
        return a is >= 0 and < Size ? a : throw new AsmError($"адрес {a} вне памяти — ячейки 0–{Size - 1}");
    }

    private static int Constant(string text, string op)
    {
        var n = Number(text) ?? throw new AsmError($"после {op} нужно число 0–999{Got(text)}");
        if (n < 0)
            throw new AsmError(Negative);
        if (n > 999)
            throw new AsmError($"число {n} не помещается в команду: {Field}");
        return n;
    }

    private static int Instruction(string text)
    {
        var m = Regex.Match(text, @"^([A-Za-z]+)\s*(.*)$");
        if (!m.Success)
            throw new AsmError($"непонятная строка «{text}»: ожидается команда или число");
        var op = m.Groups[1].Value.ToUpperInvariant();
        var code = Array.IndexOf(Ops, op);
        if (code < 0)
            throw new AsmError($"неизвестная команда «{m.Groups[1].Value}». Команды: {string.Join(", ", Ops)}");
        var args = m.Groups[2].Value.Trim().Length == 0 ? [] : m.Groups[2].Value.Split(',').Select(s => s.Trim()).ToList();
        var form = Forms[op];
        if (args.Count > form.Length)
            throw new AsmError($"у {op} лишний операнд «{args[form.Length]}»");
        while (args.Count < form.Length)
            args.Add("");
        return form switch
        {
            "" => 0,
            "a" => code * 10000 + Cell(args[0], op),
            "ra" => code * 10000 + Register(args[0], op) * 1000 + Cell(args[1], op),
            "rn" => code * 10000 + Register(args[0], op) * 1000 + Constant(args[1], op),
            _ => code * 10000 + Register(args[0], op) * 1000 + Register(args[1], op),
        };
    }
}
