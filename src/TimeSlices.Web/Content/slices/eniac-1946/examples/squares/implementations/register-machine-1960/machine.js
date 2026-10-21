// Средство визуализации, не код эпохи: учебная машина с регистрами среза «Компьютер изнутри»
// (docs/spec/register-machine.md, разделы 1–5). Обобщённая машина около 1960 года, не эмуляция настоящей.
// Слово команды = КОП × 10000 + РЕГ × 1000 + АДРЕС; 128 ячеек; регистры A, B, C, X; признак LT.
// Память загружается из копии code/squares.txt (id="squares") или code/cubes.txt (id="cubes"),
// поэтому машина исполняет ровно показанную программу.
(function () {
    "use strict";

    var SIZE = 128;
    var N_CELL = 20;                  // N — число строк таблицы
    var TABLE = 28;                   // таблица: [28 + i]
    var MICROSECONDS = 10;            // условное время на команду (спецификация, раздел 5)
    var OPS = ["HALT", "LOAD", "LOADI", "STORE", "STOREX", "ADD", "ADDI", "SUB", "CMP", "BLT"];
    var REGS = ["A", "B", "C", "X"];

    var memory, kinds, reg, pc, ir, lt, ticks, halted, written, changedRegs, running = false, generation = 0;
    var cells = [];

    // Разбор листинга: «адрес  команда (16 знаков)  слово  смысл». Слово сверяется с командой:
    // расхождение листинга и машины — ошибка сборки, а не то, что должен увидеть обучаемый.
    function parse(id) {
        var image = new Array(SIZE).fill(0), kind = new Array(SIZE).fill("");
        document.getElementById(id).textContent.split("\n").forEach(function (line) {
            var m = /^\s*(\d+)  (.{16})\s*(\d+)(?:  |$)/.exec(line);
            if (!m) return;
            var word = Number(m[3]);
            if (mnemonic(word) !== m[2].trim().replace(/\s+/g, " ")) throw new Error("слово не совпадает с командой: " + line);
            image[Number(m[1])] = word;
            kind[Number(m[1])] = "prog";
        });
        image[N_CELL] = selectedN();
        kind[N_CELL] = "data";
        return { memory: image, kinds: kind };
    }

    // Поля слова. Команда — только число 0…99 999 с допустимыми полями; остальные числа мнемоники не имеют.
    function fields(word) {
        if (word < 0 || word > 99999 || word !== Math.floor(word)) return null;
        var op = Math.floor(word / 10000), r = Math.floor(word / 1000) % 10, a = word % 1000;
        if (op === 0) return word === 0 ? { op: 0, r: 0, a: 0 } : null;          // HALT — только 0
        if (op === 9) return r === 0 ? { op: op, r: 0, a: a } : null;             // BLT — без регистра
        if (r > 3) return null;
        if ((op === 5 || op === 7) && a > 3) return null;                         // ADD, SUB: второй операнд — регистр
        return { op: op, r: r, a: a };
    }

    // Мнемоника слова: «LOADI A, 0», «ADD A, B», «BLT 3», «HALT»; null — число не читается как команда.
    function mnemonic(word) {
        var f = fields(word);
        if (!f) return null;
        var name = OPS[f.op];
        if (f.op === 0) return name;
        if (f.op === 9) return name + " " + f.a;
        return name + " " + REGS[f.r] + ", " + (f.op === 5 || f.op === 7 ? REGS[f.a] : f.a);
    }

    function num(n) { return n < 0 ? "−" + (-n) : String(n); }

    function selectedN() { return Number(document.querySelector("input[name=n]:checked").value); }

    function selectedTask() { return document.querySelector("input[name=task]:checked").value; }

    function source() { return parse(selectedTask()); }

    function load() {
        var image = source();
        memory = image.memory;
        kinds = image.kinds;
        reg = [0, 0, 0, 0]; pc = 0; ir = null; lt = false; ticks = 0; halted = false; written = -1; changedRegs = [];
        cells.forEach(function (td) { td.classList.remove("toggled"); });
    }

    // «0–9» по списку адресов; адреса идут подряд — для подписи этого достаточно.
    function spans(list) {
        var parts = [];
        list.forEach(function (a, i) {
            if (i > 0 && a === list[i - 1] + 1) parts[parts.length - 1][1] = a;
            else parts.push([a, a]);
        });
        return parts.map(function (p) { return p[0] === p[1] ? String(p[0]) : p[0] + "–" + p[1]; }).join(", ");
    }

    function programCells() {
        var list = [];
        kinds.forEach(function (k, a) { if (k === "prog") list.push(a); });
        return list;
    }

    function loadedText() {
        return "Программа загружена: " + spans(programCells()) + " — команды, " + N_CELL + " — N = " + memory[N_CELL] +
            ", " + TABLE + "–" + (TABLE + memory[N_CELL] - 1) + " — пустая таблица; регистры — нули.";
    }

    // Диапазон ±999 999 (спецификация, раздел 1) — условность показа, переполнение машина не проверяет:
    // у кубов при N = 100 последний проход оставляет в A число 100³ = 1 000 000, которое в таблицу уже не пишется.
    function setReg(r, value) {
        reg[r] = value;
        changedRegs.push(REGS[r]);
    }

    function noCell(text, a) {
        return stop(text + ": ячейки " + a + " нет (0–" + (SIZE - 1) + "). Машина остановилась.", true);
    }

    // Один такт: выборка (IR := [PC], PC += 1) → расшифровка (КОП, РЕГ, АДРЕС) → исполнение.
    function tick() {
        if (halted) { pc = 0; halted = false; }   // повторный пуск — с ячейки 0, память не перезагружается
        written = -1;
        changedRegs = [];
        if (pc >= SIZE) return stop("Счётчик команд = " + pc + ": такой ячейки нет (0–" + (SIZE - 1) + "). Машина остановилась.", true);
        var from = pc;
        ir = memory[pc];
        pc = pc + 1;
        ticks++;
        changedRegs.push("PC", "IR");
        var f = fields(ir), name = mnemonic(ir);
        var text = "Такт " + ticks + ". Выборка: IR := [" + from + "] = " + num(ir) + ". ";
        if (!f) return stop(text + "Расшифровка: число " + num(ir) + " — не команда, у него нет мнемоники. Машина остановилась.", true);
        text += "Расшифровка: " + name + ". Исполнение: ";
        var r = f.r, a = f.a, R = REGS[r], v;
        switch (OPS[f.op]) {
            case "HALT":
                return stop(text + "стоп. Программа в памяти та же, что была загружена, — её можно запустить снова.", false);
            case "LOAD":
                if (a >= SIZE) return noCell(text + name, a);
                setReg(r, memory[a]);
                return text + R + " := [" + a + "] = " + num(reg[r]) + ".";
            case "LOADI":
                setReg(r, a);
                return text + R + " := " + a + ".";
            case "STORE":
                if (a >= SIZE) return noCell(text + name, a);
                memory[a] = reg[r]; written = a;
                return text + "[" + a + "] := " + R + " = " + num(reg[r]) + ".";
            case "STOREX":
                v = a + reg[3];
                if (v < 0 || v >= SIZE)
                    return stop(text + "адрес = " + a + " + X = " + a + " + " + num(reg[3]) + " = " + num(v) +
                        " — за пределами памяти (0–" + (SIZE - 1) + "). Запись не выполнена, машина остановилась.", true);
                memory[v] = reg[r]; written = v;
                return text + "адрес = " + a + " + X = " + a + " + " + num(reg[3]) + " = " + v + "; [" + v + "] := " + R + " = " + num(reg[r]) + ".";
            case "ADD":
            case "SUB":
                var s = REGS[a], before = reg[r], sign = f.op === 5 ? " + " : " − ";
                v = f.op === 5 ? before + reg[a] : before - reg[a];
                setReg(r, v);
                return text + R + " := " + R + sign + s + " = " + num(before) + sign + num(reg[a]) + " = " + num(v) + ".";
            case "ADDI":
                v = reg[r] + a;
                setReg(r, v);
                return text + R + " := " + R + " + " + a + " = " + num(v) + ".";
            case "CMP":
                if (a >= SIZE) return noCell(text + name, a);
                lt = reg[r] < memory[a];
                changedRegs.push("LT");
                return text + R + " = " + num(reg[r]) + (lt ? " < " : " не меньше ") + "[" + a + "] = " + num(memory[a]) +
                    " → LT = " + (lt ? "да" : "нет") + ".";
            case "BLT":
                if (!lt) return text + "LT = нет → дальше по порядку, PC = " + pc + ".";
                pc = a;
                return text + "LT = да → PC := " + a + ".";
        }
    }

    function stop(text, error) {
        halted = true;
        running = false;
        status.className = error ? "error" : "";
        return text;
    }

    // Прогон: такты с выбранной скоростью, пока машина не остановится.
    // Во время прогона строка такта (aria-live) не обновляется — итог объявляется один раз при остановке.
    function run() {
        running = true;
        var mine = ++generation;   // кадр от прежнего, уже сброшенного прогона не продолжает работу
        if (document.activeElement === buttons.run || document.activeElement === buttons.step) buttons.reset.focus();
        status.textContent = "Прогон…";
        var last = performance.now(), debt = 0, text = "";
        update();
        (function frame(now) {
            if (!running || mine !== generation) return;
            debt += (now - last) * speed() / 1000;
            last = now;
            while (debt >= 1 && running) { text = tick(); debt--; }
            if (running) { update(); requestAnimationFrame(frame); }
            else show(text);
        })(last);
    }

    function speed() { return Number(document.getElementById("speed").value); }

    var status = document.getElementById("status");
    var buttons = { step: document.getElementById("step"), run: document.getElementById("run"), reset: document.getElementById("reset") };

    function build() {
        var table = document.getElementById("memory");
        for (var r = 0; r < SIZE / 8; r++) {
            var row = table.insertRow();
            for (var c = 0; c < 8; c++) cells.push(row.insertCell());
        }
    }

    function showReg(id, value, changed) {
        var el = document.getElementById(id);
        el.textContent = value;
        el.parentNode.classList.toggle("changed", changed);
    }

    function update() {
        REGS.forEach(function (R, r) { showReg("reg-" + R, num(reg[r]), changedRegs.indexOf(R) >= 0); });
        showReg("reg-PC", pc, changedRegs.indexOf("PC") >= 0);
        showReg("reg-IR", ir === null ? "—" : num(ir) + (mnemonic(ir) ? " · " + mnemonic(ir) : ""), changedRegs.indexOf("IR") >= 0);
        showReg("reg-LT", lt ? "да" : "нет", changedRegs.indexOf("LT") >= 0);
        document.getElementById("ticks").textContent = ticks;
        document.getElementById("time").textContent = ticks * MICROSECONDS + " мкс";
        cells.forEach(function (td, a) {
            td.innerHTML = "";
            var i = document.createElement("i");
            i.textContent = a;
            var w = document.createElement("span");
            w.className = "w";
            w.textContent = num(memory[a]);
            td.appendChild(i);
            td.appendChild(w);
            td.appendChild(document.createElement("br"));
            td.appendChild(document.createTextNode(mnemonic(memory[a]) || "—"));
            td.classList.toggle("prog", kinds[a] === "prog");
            td.classList.toggle("data", kinds[a] === "data");
            td.classList.toggle("pc", a === pc && !halted);
            td.classList.toggle("changed", a === written);
        });
        buttons.step.disabled = running;
        buttons.run.disabled = running;
        document.querySelectorAll("input[name=n], input[name=task]").forEach(function (r) { r.disabled = running; });
    }

    function show(text) {
        status.textContent = text;
        update();
    }

    buttons.step.addEventListener("click", function () { status.className = ""; show(tick()); });
    buttons.run.addEventListener("click", function () { status.className = ""; run(); });
    buttons.reset.addEventListener("click", function () {
        running = false;   // сброс во время прогона останавливает его
        load();
        status.className = "";
        show("Сброс: программа снова загружена из листинга, регистры — нули, счётчик команд = 0.");
    });
    // «10 / 100» меняет только ячейку 20 — остальная память и регистры остаются как есть.
    document.querySelectorAll("input[name=n]").forEach(function (radio) {
        radio.addEventListener("change", function () {
            var before = memory[N_CELL];
            memory[N_CELL] = selectedN();
            cells.forEach(function (td) { td.classList.remove("toggled"); });
            cells[N_CELL].classList.add("toggled");
            status.className = "";
            show("Изменена только ячейка " + N_CELL + ": " + before + " → " + memory[N_CELL] + ". Таблица займёт ячейки " +
                TABLE + "–" + (TABLE + memory[N_CELL] - 1) + ".");
        });
    });
    // «Квадраты / кубы» загружает другую программу целиком и отмечает ячейки, в которых другое число.
    document.querySelectorAll("input[name=task]").forEach(function (radio) {
        radio.addEventListener("change", function () {
            var other = parse(selectedTask() === "cubes" ? "squares" : "cubes");
            load();
            var diff = [];
            memory.forEach(function (word, a) { if (word !== other.memory[a]) diff.push(a); });
            diff.forEach(function (a) { cells[a].classList.add("toggled"); });
            status.className = "";
            show("Загружена программа " + (selectedTask() === "cubes" ? "кубов" : "квадратов") + " (ячейки " + spans(programCells()) +
                "): другие числа в " + diff.length + " ячейках — " + spans(diff) + ". Регистры — нули, счётчик команд = 0.");
        });
    });

    build();
    load();
    status.textContent = loadedText();
    update();
})();
