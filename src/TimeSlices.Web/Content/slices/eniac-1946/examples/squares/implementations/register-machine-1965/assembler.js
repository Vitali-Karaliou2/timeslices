// Средство визуализации, не код эпохи: простой ассемблер учебной машины с регистрами для режима
// «Своя программа» (docs/spec/register-machine.md, раздел 8). Без DOM: тот же файл загружает сборка в рамке
// и проверка scripts/check_assembler.js; поведение сверяется с тестовым дубликатом на C# по одной таблице
// фикстур (tests/TimeSlices.Tests/Fixtures/register-assembler.json).
//
// Синтаксис — строка на ячейку: «[адрес:] МНЕМОНИКА операнды» или данные «[адрес:] число»; комментарий — после «;».
// Без адреса строка занимает ячейку, следующую за предыдущей (первая — 0).
(function (root) {
    "use strict";

    var SIZE = 128, N_CELL = 20, TABLE = 28, LIMIT = 999999;
    var OPS = ["HALT", "LOAD", "LOADI", "STORE", "STOREX", "ADD", "ADDI", "SUB", "CMP", "BLT"];
    var REGS = ["A", "B", "C", "X"];
    // Вид операндов: "" — нет; "a" — адрес ячейки; "ra" — регистр и адрес; "rn" — регистр и число в команде; "rs" — два регистра.
    var FORMS = { HALT: "", LOAD: "ra", LOADI: "rn", STORE: "ra", STOREX: "ra", ADD: "rs", ADDI: "rn", SUB: "rs", CMP: "ra", BLT: "a" };

    var FIELD = "число в команде берётся из поля адреса, 0–999";
    var NEGATIVE = FIELD + "; отрицательную константу положите в ячейку памяти или получите вычитанием";

    function fail(message) { throw { message: message }; }

    function number(text) {
        var m = /^([+\-−]?)(\d+)$/.exec(text);
        if (!m) return null;
        var n = Number(m[2]);
        return m[1] === "-" || m[1] === "−" ? -n : n;
    }

    function register(text, op) {
        var r = REGS.indexOf(text.toUpperCase());
        if (r < 0) fail("нет регистра — после " + op + " нужен A, B, C или X" + (text ? ", а не «" + text + "»" : ""));
        return r;
    }

    function cell(text, op) {
        var a = number(text);
        if (a === null) fail("после " + op + " нужен адрес ячейки — число 0–" + (SIZE - 1) + (text ? ", а не «" + text + "»" : ""));
        if (a < 0 || a >= SIZE) fail("адрес " + a + " вне памяти — ячейки 0–" + (SIZE - 1));
        return a;
    }

    function constant(text, op) {
        var n = number(text);
        if (n === null) fail("после " + op + " нужно число 0–999" + (text ? ", а не «" + text + "»" : ""));
        if (n < 0) fail(NEGATIVE);
        if (n > 999) fail("число " + n + " не помещается в команду: " + FIELD);
        return n;
    }

    // Слово команды: КОП × 10000 + РЕГ × 1000 + АДРЕС (спецификация, раздел 1).
    function instruction(text) {
        var m = /^([A-Za-z]+)\s*(.*)$/.exec(text);
        if (!m) fail("непонятная строка «" + text + "»: ожидается команда или число");
        var op = m[1].toUpperCase(), code = OPS.indexOf(op);
        if (code < 0) fail("неизвестная команда «" + m[1] + "». Команды: " + OPS.join(", "));
        var args = m[2].trim() === "" ? [] : m[2].split(",").map(function (s) { return s.trim(); });
        var form = FORMS[op];
        if (args.length > form.length) fail("у " + op + " лишний операнд «" + args[form.length] + "»");
        while (args.length < form.length) args.push("");
        switch (form) {
            case "": return 0;
            case "a": return code * 10000 + cell(args[0], op);
            case "ra": return code * 10000 + register(args[0], op) * 1000 + cell(args[1], op);
            case "rn": return code * 10000 + register(args[0], op) * 1000 + constant(args[1], op);
            case "rs": return code * 10000 + register(args[0], op) * 1000 + register(args[1], op);
        }
    }

    // Перевод текста в слова. Возвращает { cells: [{ address, word, kind, line }], errors: ["Строка N: …"] };
    // при ошибках в память ничего не пишется.
    function assemble(text) {
        var cells = [], errors = [], taken = {}, next = 0;
        String(text).split(/\r?\n/).forEach(function (raw, i) {
            var line = i + 1, body = raw.replace(/;.*$/, "").trim();
            if (body === "") return;
            try {
                var address = next, m = /^(\S+?)\s*:\s*(.*)$/.exec(body);
                if (m) {
                    address = number(m[1]);
                    if (address === null) fail("адрес «" + m[1] + "» — не число");
                    if (address < 0 || address >= SIZE) fail("адрес " + address + " вне памяти — ячейки 0–" + (SIZE - 1));
                    body = m[2].trim();
                    if (body === "") fail("после адреса " + address + " нет ни команды, ни числа");
                }
                if (address >= SIZE) fail("программа не помещается в память — ячейки 0–" + (SIZE - 1));
                if (address === N_CELL) fail("программа налезает на ячейку " + N_CELL + " — в ней N, его задаёт поле «N»");
                if (address >= TABLE) fail("программа налезает на таблицу — ячейки " + TABLE + "–" + (SIZE - 1) + " заполняет сама программа");
                if (taken[address]) fail("ячейка " + address + " уже занята строкой " + taken[address]);
                var value = number(body), word, kind;
                if (value !== null) {
                    if (Math.abs(value) > LIMIT) fail("число " + value + " вне ±999 999");
                    word = value; kind = "data";
                } else {
                    word = instruction(body); kind = "prog";
                }
                taken[address] = line;
                cells.push({ address: address, word: word, kind: kind, line: line });
                next = address + 1;
            } catch (e) {
                if (!e || typeof e.message !== "string" || e instanceof Error) throw e;
                errors.push("Строка " + line + ": " + e.message + ".");
                next = next + 1;
            }
        });
        return { cells: errors.length ? [] : cells, errors: errors };
    }

    var api = { assemble: assemble };
    root.RegisterAssembler = api;
    if (typeof module !== "undefined" && module.exports) module.exports = api;
})(this);
