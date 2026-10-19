// Средство визуализации, не код эпохи: учебная машина среза «Программа в памяти» (описание hw-01, раздел 5).
// Не EDSAC: свой формат слова (код операции × 1000 + адрес), 128 ячеек, восемь команд.
// Память загружается из копии code/squares.txt (id="program") или code/cubes.txt (id="cubes"),
// поэтому машина исполняет ровно показанную программу.
(function () {
    "use strict";

    var SIZE = 128;
    var SPEED = 600;                  // команд в секунду — порядок скорости машин 1949 года (описание hw-01, раздел 6)
    var CODES = { Z: 0, A: 1, S: 2, T: 3, U: 4, E: 5, G: 6, O: 7 };
    var LETTERS = ["Z", "A", "S", "T", "U", "E", "G", "O"];
    var COUNTER = 22;                 // ячейка счётчика: N − 1

    var memory, kinds, acc, pc, ir, ticks, halted, changed, printed, running = false, generation = 0;
    var cells = [];

    // Разбор листинга: «адрес  буква [адрес]  слово  смысл» — команда, «адрес[–адрес]  число  смысл» — данные.
    // Возвращает образ памяти и вид ячеек (prog — команда, data — число, "" — таблица и пустые ячейки).
    function parse(id) {
        var image = new Array(SIZE).fill(0), kind = new Array(SIZE).fill("");
        document.getElementById(id).textContent.split("\n").forEach(function (line) {
            var m = /^\s*(\d+)\s+([A-Z])(?:\s+(\d+))?\s+(\d+)/.exec(line);
            if (m) {
                var word = CODES[m[2]] * 1000 + Number(m[3] || 0);
                if (word !== Number(m[4])) throw new Error("слово не совпадает с командой: " + line);
                image[Number(m[1])] = word;
                kind[Number(m[1])] = "prog";
                return;
            }
            m = /^\s*(\d+)(?:–(\d+))?\s+(-?\d+)\s/.exec(line);
            if (m) for (var a = Number(m[1]); a <= Number(m[2] || m[1]); a++) {
                image[a] = Number(m[3]);
                if (!m[2]) kind[a] = "data";
            }
        });
        image[COUNTER] = selectedCounter();
        return { memory: image, kinds: kind };
    }

    function load() {
        var image = parse(selectedTask());
        memory = image.memory;
        kinds = image.kinds;
        acc = 0; pc = 0; ir = null; ticks = 0; halted = false; changed = -1; printed = [];
        cells.forEach(function (td) { td.classList.remove("toggled"); });
    }

    function selectedCounter() {
        return Number(document.querySelector("input[name=n]:checked").value);
    }

    function selectedTask() {
        return document.querySelector("input[name=task]:checked").value;
    }

    // «0–19» по списку адресов одного вида; адреса идут подряд — для подписи этого достаточно.
    function spans(list) {
        var parts = [];
        list.forEach(function (a, i) {
            if (i > 0 && a === list[i - 1] + 1) parts[parts.length - 1][1] = a;
            else parts.push([a, a]);
        });
        return parts.map(function (p) { return p[0] === p[1] ? String(p[0]) : p[0] + "–" + p[1]; }).join(", ");
    }

    function addresses(kind) {
        var list = [];
        kinds.forEach(function (k, a) { if (k === kind) list.push(a); });
        return list;
    }

    function loadedText() {
        return "Программа загружена: " + spans(addresses("prog")) + " — команды, " + spans(addresses("data")) +
            " — числа, 28–" + (28 + memory[COUNTER]) + " — пустая таблица.";
    }

    // Буквенная запись слова: у любого неотрицательного числа с кодом 0–7 она есть — команда и число неотличимы.
    function letters(word) {
        var op = Math.floor(word / 1000), addr = word % 1000;
        if (word < 0 || op >= LETTERS.length) return "—";
        return op === 0 && addr === 0 ? "Z" : LETTERS[op] + " " + addr;
    }

    // Один такт: выборка (ячейка по счётчику команд — в регистр команды, счётчик + 1), декодирование, исполнение.
    function tick() {
        if (halted) { pc = 0; halted = false; }   // повторный пуск — с ячейки 0, память не перезагружается
        changed = -1;
        ir = memory[pc];
        var from = pc;
        pc = pc + 1;
        ticks++;
        var op = Math.floor(ir / 1000), addr = ir % 1000;
        if (ir < 0 || op >= LETTERS.length) return stop("В ячейке " + from + " — число " + ir + ", у него нет буквенной записи: машина остановилась.", true);
        if (op !== 0 && addr >= SIZE) return stop("Команда " + letters(ir) + " из ячейки " + from + ": адреса " + addr + " в памяти нет (0–" + (SIZE - 1) + "). Машина остановилась.", true);
        var text = "Такт " + ticks + ": выборка из ячейки " + from + " — " + letters(ir) + " (" + ir + "). ";
        switch (LETTERS[op]) {
            case "A": acc += memory[addr]; text += "Аккумулятор += [" + addr + "] = " + acc + "."; break;
            case "S": acc -= memory[addr]; text += "Аккумулятор −= [" + addr + "] = " + acc + "."; break;
            case "T": memory[addr] = acc; changed = addr; text += "[" + addr + "] := " + acc + ", аккумулятор := 0."; acc = 0; break;
            case "U": memory[addr] = acc; changed = addr; text += "[" + addr + "] := " + acc + "."; break;
            case "E": if (acc >= 0) { pc = addr; text += "Аккумулятор ≥ 0 — счётчик команд := " + addr + "."; } else text += "Аккумулятор < 0 — дальше по порядку."; break;
            case "G": if (acc < 0) { pc = addr; text += "Аккумулятор < 0 — счётчик команд := " + addr + "."; } else text += "Аккумулятор ≥ 0 — дальше по порядку."; break;
            case "O": printed.push(memory[addr]); text += "Печать [" + addr + "] = " + memory[addr] + "."; break;
            case "Z": return stop(text + "Стоп. В ячейке 2 теперь " + letters(memory[2]) + ".", false);
        }
        return text;
    }

    function stop(text, error) {
        halted = true;
        running = false;
        status.className = error ? "error" : "";
        return text;
    }

    // Пуск: такты со скоростью ~600 в секунду, пока машина не остановится.
    // Во время прогона строка состояния (aria-live) не обновляется — итог объявляется один раз при остановке.
    function run() {
        running = true;
        var mine = ++generation;   // кадр от прежнего, уже сброшенного прогона не продолжает работу
        // «Такт» и «Пуск» на время прогона недоступны: фокус переходит на «Сброс», чтобы не потеряться.
        if (document.activeElement === buttons.run || document.activeElement === buttons.step) buttons.reset.focus();
        status.textContent = "Пуск…";
        var last = performance.now(), debt = 0, text = "";
        update();
        (function frame(now) {
            if (!running || mine !== generation) return;
            debt += (now - last) * SPEED / 1000;
            last = now;
            while (debt >= 1 && running) { text = tick(); debt--; }
            if (running) { update(); requestAnimationFrame(frame); }
            else show(text);
        })(last);
    }

    var status = document.getElementById("status");
    var buttons = { step: document.getElementById("step"), run: document.getElementById("run"), reset: document.getElementById("reset") };

    function build() {
        var table = document.getElementById("memory");
        for (var r = 0; r < SIZE / 8; r++) {
            var row = table.insertRow();
            for (var c = 0; c < 8; c++) {
                var td = row.insertCell();
                cells.push(td);
            }
        }
    }

    function update() {
        document.getElementById("acc").textContent = acc;
        document.getElementById("pc").textContent = pc;
        document.getElementById("ir").textContent = ir === null ? "—" : letters(ir) + " (" + ir + ")";
        document.getElementById("ticks").textContent = ticks;
        document.getElementById("time").textContent = Math.round(ticks * 1000 / SPEED) + " мс";
        document.getElementById("printer").textContent = printed.length ? "Печать: " + printed.join(", ") : "";
        cells.forEach(function (td, a) {
            td.innerHTML = "";
            var i = document.createElement("i");
            i.textContent = a;
            var w = document.createElement("span");
            w.className = "w";
            w.textContent = memory[a];
            td.appendChild(i);
            td.appendChild(w);
            td.appendChild(document.createElement("br"));
            td.appendChild(document.createTextNode(letters(memory[a])));
            td.classList.toggle("prog", kinds[a] === "prog");
            td.classList.toggle("data", kinds[a] === "data");
            td.classList.toggle("pc", a === pc && !halted);
            td.classList.toggle("changed", a === changed);
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
        running = false;   // сброс во время «Пуск» останавливает прогон
        load();
        status.className = "";
        show("Сброс: программа и числа снова загружены из листинга, счётчик команд = 0.");
    });
    // Переключатель меняет только ячейку 22 — остальная память и регистры остаются как есть.
    document.querySelectorAll("input[name=n]").forEach(function (radio) {
        radio.addEventListener("change", function () {
            var before = memory[COUNTER];
            memory[COUNTER] = selectedCounter();
            cells.forEach(function (td) { td.classList.remove("toggled"); });
            cells[COUNTER].classList.add("toggled");
            status.className = "";
            var text = "Изменена только ячейка 22: " + before + " → " + memory[COUNTER] + ".";
            // Куда пойдёт таблица, решает адрес в команде ячейки 2; после прогона он уже не 28.
            if (memory[2] === CODES.T * 1000 + 28) text += " Таблица займёт ячейки 28–" + (28 + memory[COUNTER]) + ".";
            show(text);
        });
    });

    // Переключатель задачи загружает другую программу в ту же память и отмечает ячейки, которые
    // по листингу отличаются от другой программы (число повторов то же): другое слово или другая роль.
    // Ячейка 19 у квадратов пуста, у кубов — Z; слово у обеих 0, но по листингу это разные ячейки.
    document.querySelectorAll("input[name=task]").forEach(function (radio) {
        radio.addEventListener("change", function () {
            var other = parse(selectedTask() === "cubes" ? "program" : "cubes");
            load();
            var diff = [];
            memory.forEach(function (word, a) { if (word !== other.memory[a] || kinds[a] !== other.kinds[a]) diff.push(a); });
            diff.forEach(function (a) { cells[a].classList.add("toggled"); });
            status.className = "";
            show("Загружена программа " + (selectedTask() === "cubes" ? "кубов" : "квадратов") + ": изменились " +
                diff.length + " ячеек — " + spans(diff) + ". Машина та же, счётчик команд = 0.");
        });
    });

    build();
    load();
    status.textContent = loadedText();
    update();
})();
