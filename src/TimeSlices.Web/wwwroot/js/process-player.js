// Проигрыватель процессов на странице примера — средство визуализации, не код эпохи.
// Без библиотек. Расписание (кадры, задержки, подписи промежутков) считает сервер (ProcessSchedule)
// и кладёт в <script type="application/json" class="process-schedule">; здесь — только ход по шагам.
// Без JS проигрыватель скрыт, а список кадров виден целиком.
(function () {
    "use strict";

    // Сколько строк вокруг строк стоп-кадра показывать во фрагменте кода.
    var CONTEXT_LINES = 2;

    document.querySelectorAll("[data-process-player]").forEach(function (section) {
        var data = section.querySelector("script.process-schedule");
        if (!data) return;
        var steps = JSON.parse(data.textContent);
        if (steps.length === 0) return;
        setUp(section, steps);
    });

    function setUp(section, steps) {
        var player = section.querySelector(".player");
        var items = section.querySelectorAll("ol.frames > li");
        var time = player.querySelector(".player-time");
        var gap = player.querySelector(".player-gap");
        var bar = player.querySelector(".player-progress-bar");
        var status = player.querySelector(".player-status");
        var stopPanel = player.querySelector(".player-stop");
        var buttons = {
            play: player.querySelector("[data-action=play]"),
            pause: player.querySelector("[data-action=pause]"),
            back: player.querySelector("[data-action=back]"),
            forward: player.querySelector("[data-action=forward]"),
            next: player.querySelector("[data-action=continue]")
        };
        var speed = player.querySelector("[data-action=speed]");

        var current = -1;   // номер показанного шага; -1 — ещё ничего не показано
        var playing = false;
        var timer = null;

        player.hidden = false;
        // С проигрывателем пояснение стоп-кадра видно один раз — в панели; в списке кадров его прячет CSS.
        section.classList.add("player-on");
        updateButtons();

        buttons.play.addEventListener("click", function () {
            playing = true;
            if (current < 0 || current === steps.length - 1) show(0);
            else if (steps[current].stop && !stopPanel.hidden) resume();
            else advanceAfterDelay();
            updateButtons();
        });
        buttons.pause.addEventListener("click", function () { pause(); });
        buttons.back.addEventListener("click", function () { pause(); show(Math.max(current - 1, 0)); });
        buttons.forward.addEventListener("click", function () { pause(); show(Math.min(current + 1, steps.length - 1)); });
        buttons.next.addEventListener("click", function () { playing = true; resume(); updateButtons(); });
        // Новая скорость действует сразу: идущее ожидание начинается заново с новой задержкой.
        speed.addEventListener("change", function () { if (playing && timer !== null) advanceAfterDelay(); });

        // Показать шаг i: подсветить кадр, время сюжета, объявить; на стоп-кадре — остановиться.
        function show(i) {
            var manual = !playing; // переход кнопками «Назад» / «Вперёд», а не проигрывание
            cancelTimer();
            current = i;
            var step = steps[i];
            items.forEach(function (li, n) {
                li.classList.toggle("frame-current", n === i);
                if (n === i) li.setAttribute("aria-current", "step");
                else li.removeAttribute("aria-current");
            });
            time.textContent = step.atDisplay || "время не указано";
            gap.textContent = i > 0 && steps[i - 1].gapLabel ? steps[i - 1].gapLabel : "";
            bar.style.transition = "none";
            bar.style.width = "0";
            hideStop();

            var text = "Кадр " + (i + 1) + " из " + steps.length + ". " + step.text;
            // На скорости ×2 и ×4 обычные кадры сменяются быстрее, чем их успевает прочесть экранный диктор:
            // объявляются только стоп-кадры и конец процесса.
            var announce = step.stop || !playing || Number(speed.value) === 1 || i === steps.length - 1;
            if (step.stop) {
                playing = false;
                showStop(step);
                // Пояснение — только в панели стоп-кадра; кнопка «Дальше» ссылается на него (aria-describedby).
                text = "Стоп-кадр " + (i + 1) + " из " + steps.length + ".";
            } else if (playing) {
                advanceAfterDelay();
            }
            if (i === steps.length - 1 && !step.stop) {
                playing = false;
                text += (/[.!?…]$/.test(text) ? "" : ".") + " Конец процесса.";
            }
            if (announce) status.textContent = text;
            // При ручном переходе фокус остаётся на «Вперёд», и aria-describedby у «Дальше» не сработает:
            // пояснение объявляется скрытым текстом, а на экране остаётся одно — в панели стоп-кадра.
            if (step.stop && manual && step.detail && step.detail.explanation) {
                var hidden = document.createElement("span");
                hidden.className = "visually-hidden";
                hidden.textContent = " " + step.detail.explanation;
                status.appendChild(hidden);
            }
            updateButtons();
        }

        function resume() {
            hideStop();
            advanceAfterDelay();
        }

        // Ждать экранную задержку шага (при выбранной скорости) и перейти к следующему кадру.
        function advanceAfterDelay() {
            cancelTimer();
            if (current >= steps.length - 1) {
                // «Дальше» на последнем стоп-кадре: идти некуда, процесс закончен.
                playing = false;
                status.textContent = "Конец процесса.";
                updateButtons();
                return;
            }
            var step = steps[current];
            var delay = step.delayMs / Number(speed.value);
            if (step.gapLabel) gap.textContent = step.gapLabel + "…";
            // Полоса показывает, сколько осталось ждать до следующего кадра.
            bar.style.transition = "none";
            bar.style.width = "0";
            void bar.offsetWidth;
            bar.style.transition = "width " + delay + "ms linear";
            bar.style.width = "100%";
            timer = setTimeout(function () { timer = null; show(current + 1); }, delay);
        }

        function pause() {
            playing = false;
            cancelTimer();
            // Зафиксировать полосу там, где её застала пауза (без этого она прыгнула бы в конец).
            bar.style.width = getComputedStyle(bar).width;
            bar.style.transition = "none";
            updateButtons();
        }

        function cancelTimer() {
            if (timer !== null) { clearTimeout(timer); timer = null; }
        }

        // Пояснение и фрагмент кода берутся из уже показанного на странице кода (pre[data-code-file]).
        function showStop(step) {
            var d = step.detail;
            if (!d) return;
            var li = items[current];
            li.appendChild(stopPanel);
            stopPanel.querySelector(".player-stop-explanation").textContent = d.explanation || "";
            var file = stopPanel.querySelector(".player-stop-file");
            var code = stopPanel.querySelector(".player-stop-code");
            code.textContent = "";
            file.textContent = d.codeFile ? d.codeFile + (d.lines ? ", строки " + d.lines : "") : "";
            var source = d.codeFile ? findCode(d.codeFile) : null;
            if (source && d.lineFrom) {
                var lines = source.querySelectorAll(".line");
                var from = Math.max(d.lineFrom - CONTEXT_LINES, 1);
                var to = Math.min(d.lineTo + CONTEXT_LINES, lines.length);
                for (var n = from; n <= to; n++) {
                    var line = lines[n - 1].cloneNode(true);
                    line.removeAttribute("id");
                    if (n >= d.lineFrom && n <= d.lineTo) line.classList.add("line-hl");
                    code.appendChild(line);
                }
            }
            code.hidden = code.childNodes.length === 0;
            stopPanel.hidden = false;
        }

        function hideStop() {
            stopPanel.hidden = true;
        }

        function findCode(path) {
            var all = document.querySelectorAll("pre[data-code-file]");
            for (var n = 0; n < all.length; n++)
                if (all[n].getAttribute("data-code-file") === path) return all[n];
            return null;
        }

        function updateButtons() {
            var focused = document.activeElement;
            buttons.play.disabled = playing;
            buttons.pause.disabled = !playing;
            buttons.back.disabled = current <= 0;
            buttons.forward.disabled = current >= steps.length - 1;
            // Фокус не должен пропасть на кнопке, которая стала недоступной или скрылась: клавиатура
            // продолжает с той кнопкой, что нужна сейчас («Дальше» на стоп-кадре, иначе «Пауза» или «Проиграть»).
            if (focused && section.contains(focused) && (focused.disabled || focused.closest("[hidden]")))
                (!stopPanel.hidden ? buttons.next : playing ? buttons.pause : buttons.play).focus();
        }
    }
})();
