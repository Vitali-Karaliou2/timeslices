// Средство визуализации, не код эпохи: переключатели «квадраты / кубы» и «10 / 100» на схеме панелей ENIAC.
// Машина не эмулируется: карты — готовые результаты прогона, листы настройки — копии code/setup-sheet.txt
// (id="sheet") и code/setup-sheet-cubes.txt (id="sheetCubes").
(function () {
    "use strict";

    // Строки листа настройки, которые меняются для 100 чисел (номер строки — как в файле листа).
    // Тест на настоящее содержание проверяет, что в строке line файла file есть текст from.
    var HUNDRED = [
        { file: "code/setup-sheet.txt", line: 1, from: "n = 0…9,", to: "n = 0…99," },
        { file: "code/setup-sheet.txt", line: 30, from: "стадия 1 — 10 проходов", to: "стадия 1 — 100 проходов" },
        { file: "code/setup-sheet-cubes.txt", line: 1, from: "n = 0…9,", to: "n = 0…99," },
        { file: "code/setup-sheet-cubes.txt", line: 37, from: "стадия 1 — 10 проходов", to: "стадия 1 — 100 проходов" }
    ];

    // Панели — то же, что на листе, сгруппированное по блокам. «mp» — переключатель мастер-программатора.
    var TASKS = {
        squares: {
            sheet: "sheet", file: "code/setup-sheet.txt",
            value: function (k) { return k * k; },
            program: "программный лоток 1: линии 1-1 … 1-7",
            digit: "цифровые лотки: 1 — константы → акк. 2; 2 — акк. 2 → акк. 1",
            panels: [
                ["Инициирующий блок", ["jack", "«пуск» → линия 1-1"], ["switch", "начальный сброс"]],
                ["Мастер-программатор, A", ["jack", "вход ← линии 1-2, 1-6"], ["jack", "стадия 1 → линия 1-3"],
                    ["jack", "стадия 2 → не подключена"], ["mp", ""]],
                ["Передатчик констант", ["jack", "J ← линия 1-1; K ← линия 1-5"], ["switch", "J = 1, K = 2"], ["", "выход → лоток 1"]],
                ["Аккумулятор 2 — разность", ["jack", "регулятор 5 ← 1-1, выход → 1-2"], ["jack", "регулятор 6 ← 1-4, выход → 1-5"],
                    ["jack", "регулятор 7 ← 1-5, выход → 1-6"], ["switch", "5: α · 6: A, без сброса · 7: α"],
                    ["", "вход α ← лоток 1; выход A → лоток 2"]],
                ["Аккумулятор 1 — квадрат", ["jack", "регулятор 5 ← 1-4"], ["switch", "5: α"], ["", "вход α ← лоток 2; → печать"]],
                ["Печать (перфоратор)", ["jack", "← линия 1-3, выход → 1-4"], ["", "аккумулятор 1 → карта"]]
            ]
        },
        cubes: {
            sheet: "sheetCubes", file: "code/setup-sheet-cubes.txt",
            value: function (k) { return k * k * k; },
            program: "программный лоток 1: линии 1-1 … 1-9",
            digit: "цифровые лотки: 1 — константы → акк. 2, акк. 3; 2 — акк. 2 → акк. 1; 3 — акк. 3 → акк. 2",
            panels: [
                ["Инициирующий блок", ["jack", "«пуск» → линия 1-1"], ["switch", "начальный сброс"]],
                ["Мастер-программатор, A", ["jack", "вход ← линии 1-3, 1-8"], ["jack", "стадия 1 → линия 1-4"],
                    ["jack", "стадия 2 → не подключена"], ["mp", ""]],
                ["Передатчик констант", ["jack", "J ← линия 1-1; K ← линии 1-2, 1-7"], ["switch", "J = 1, K = 6"], ["", "выход → лоток 1"]],
                ["Аккумулятор 3 — d2", ["jack", "регулятор 5 ← 1-2, выход → 1-3"], ["jack", "регулятор 6 ← 1-6, выход → 1-7"],
                    ["jack", "регулятор 7 ← 1-7, выход → 1-8"], ["switch", "5: α · 6: A, без сброса · 7: α"],
                    ["", "вход α ← лоток 1; выход A → лоток 3"]],
                ["Аккумулятор 2 — d1", ["jack", "регулятор 5 ← 1-1, выход → 1-2"], ["jack", "регулятор 6 ← 1-5, выход → 1-6"],
                    ["jack", "регулятор 7 ← 1-6"], ["switch", "5: α · 6: A, без сброса · 7: β"],
                    ["", "вход α ← лоток 1; вход β ← лоток 3; выход A → лоток 2"]],
                ["Аккумулятор 1 — куб", ["jack", "регулятор 5 ← 1-5"], ["switch", "5: α"], ["", "вход α ← лоток 2; → печать"]],
                ["Печать (перфоратор)", ["jack", "← линия 1-4, выход → 1-5"], ["", "аккумулятор 1 → карта"]]
            ]
        }
    };

    var view = document.getElementById("sheet-view");
    var panels = document.getElementById("panels");
    var cards = document.getElementById("cards");
    var summary = document.getElementById("summary");
    var trayProgram = document.getElementById("tray-program");
    var trayDigit = document.getElementById("tray-digit");

    function linesOf(task) {
        return document.getElementById(TASKS[task].sheet).textContent.replace(/\n$/, "").split("\n");
    }

    function checked(name) {
        return document.querySelector("input[name=" + name + "]:checked").value;
    }

    // У кубов отмечается всё, чего нет на листе квадратов: это кабели и переключатели, которые надо переставить.
    function render() {
        var task = checked("task"), n = Number(checked("n"));
        var hundred = n === 100, cubes = task === "cubes", t = TASKS[task];
        var squareLines = linesOf("squares");

        view.textContent = "";
        linesOf(task).forEach(function (text, i) {
            var span = document.createElement("span");
            if (cubes && text && squareLines.indexOf(text) < 0) span.className = "changed";
            var change = HUNDRED.filter(function (c) { return c.file === t.file && c.line === i + 1; })[0];
            if (change && hundred) {
                text = text.replace(change.from, change.to);
                span.className = "changed";
            }
            span.textContent = text || " ";
            view.appendChild(span);
        });

        var before = {};
        TASKS.squares.panels.forEach(function (p) { before[p[0]] = p.slice(1).map(function (item) { return item[1]; }); });
        panels.textContent = "";
        t.panels.forEach(function (p) {
            var div = document.createElement("div"), h = document.createElement("h3"), ul = document.createElement("ul");
            div.className = "panel";
            h.textContent = p[0];
            p.slice(1).forEach(function (item) {
                var li = document.createElement("li");
                if (item[0] === "mp") {
                    li.className = "switch";
                    li.textContent = "стадия 1: " + n + " проходов";
                    li.classList.toggle("changed", hundred);
                } else {
                    li.className = item[0];
                    li.textContent = item[1];
                    if (cubes && (before[p[0]] || []).indexOf(item[1]) < 0) li.classList.add("changed");
                }
                ul.appendChild(li);
            });
            div.appendChild(h);
            div.appendChild(ul);
            panels.appendChild(div);
        });
        trayProgram.textContent = t.program;
        trayProgram.classList.toggle("changed", cubes);
        trayDigit.textContent = t.digit;
        trayDigit.classList.toggle("changed", cubes);

        cards.textContent = "";
        for (var k = 0; k < n; k++) {
            var card = document.createElement("span");
            card.textContent = t.value(k);
            cards.appendChild(card);
        }

        var lines = HUNDRED.filter(function (c) { return c.file === t.file; }).map(function (c) { return c.line; }).join(" и ");
        summary.textContent = (cubes
            ? "Новая задача — новый лист настройки: отмечены строки, которых нет на листе квадратов; кабели снимаются и ставятся заново. "
            : "") + (hundred ? "Для 100 чисел меняются строки листа " + lines + " — переключатели мастер-программатора; кабели — те же." : "");
    }

    document.querySelectorAll("input[name=task], input[name=n]").forEach(function (radio) {
        radio.addEventListener("change", render);
    });
    render();
})();
