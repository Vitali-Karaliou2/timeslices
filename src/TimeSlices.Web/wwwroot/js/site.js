// Немного JS оболочки, без библиотек.
// Выпадающий список реализаций: смена значения сразу отправляет форму (без JS — кнопка «Показать»).
document.querySelectorAll("select[data-autosubmit]").forEach(function (select) {
    select.addEventListener("change", function () { select.form.submit(); });
});
