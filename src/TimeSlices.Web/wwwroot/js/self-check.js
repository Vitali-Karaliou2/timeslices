// Признаки самостоятельности на странице контроля знаний (ADR 0011, п. 2): активное время ввода
// и число вставок из буфера. Обучаемому не показываются; уходят на сервер скрытыми полями формы.
// Активное время — сумма промежутков между соседними событиями ввода, если промежуток не длиннее
// IDLE_MS: паузы на раздумье и уход со страницы не считаются.
(function () {
  "use strict";
  var IDLE_MS = 5000;

  document.querySelectorAll("form[data-signs]").forEach(function (form) {
    var text = form.querySelector("textarea");
    var active = form.querySelector("input[name=activeMs]");
    var pastes = form.querySelector("input[name=pasteCount]");
    if (!text || !active || !pastes) return;

    var activeMs = 0;
    var pasteCount = 0;
    var last = null;

    text.addEventListener("input", function () {
      var now = performance.now();
      if (last !== null && now - last <= IDLE_MS) activeMs += now - last;
      last = now;
    });
    text.addEventListener("paste", function () {
      pasteCount += 1;
    });
    form.addEventListener("submit", function () {
      active.value = String(Math.round(activeMs));
      pastes.value = String(pasteCount);
    });
  });
})();
