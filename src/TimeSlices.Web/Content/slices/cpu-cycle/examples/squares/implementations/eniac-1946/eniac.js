// Средство визуализации, не код эпохи: переключатель «10 / 100 квадратов» на схеме панелей ENIAC.
// Машина не эмулируется: карты — готовые результаты прогона, лист настройки — копия code/setup-sheet.txt (id="sheet").
(function () {
    "use strict";

    // Строки листа настройки, которые меняются для 100 квадратов (номер строки — как в code/setup-sheet.txt).
    // Тест на настоящее содержание проверяет, что в строке line есть текст from.
    var CHANGES = [
        { line: 1, from: "n = 0…9,", to: "n = 0…99," },
        { line: 30, from: "стадия 1 — 10 проходов", to: "стадия 1 — 100 проходов" }
    ];

    var lines = document.getElementById("sheet").textContent.replace(/\n$/, "").split("\n");
    var view = document.getElementById("sheet-view");
    var cards = document.getElementById("cards");
    var summary = document.getElementById("summary");
    var mpSwitch = document.getElementById("mp-switch");

    function render(n) {
        var hundred = n === 100;
        view.textContent = "";
        lines.forEach(function (text, i) {
            var span = document.createElement("span");
            var change = CHANGES.filter(function (c) { return c.line === i + 1; })[0];
            if (change && hundred) {
                text = text.replace(change.from, change.to);
                span.className = "changed";
            }
            span.textContent = text || " ";
            view.appendChild(span);
        });
        mpSwitch.textContent = "стадия 1: " + n + " проходов";
        mpSwitch.classList.toggle("changed", hundred);
        cards.textContent = "";
        for (var k = 0; k < n; k++) {
            var card = document.createElement("span");
            card.textContent = k * k;
            cards.appendChild(card);
        }
        summary.textContent = hundred
            ? "Для 100 квадратов меняются строки листа " + CHANGES.map(function (c) { return c.line; }).join(" и ") + "; кабели — те же."
            : "";
    }

    document.querySelectorAll("input[name=n]").forEach(function (radio) {
        radio.addEventListener("change", function () { render(Number(radio.value)); });
    });
    render(10);
})();
