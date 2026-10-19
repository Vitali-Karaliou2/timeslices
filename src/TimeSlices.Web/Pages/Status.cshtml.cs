using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TimeSlices.Web.Pages;

/// <summary>Страница для ответов с кодом ошибки без тела (UseStatusCodePagesWithReExecute). Код ответа не меняется.</summary>
/// При повторном выполнении метод запроса сохраняется, поэтому есть и OnPost; токен формы проверил исходный запрос.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class StatusModel : PageModel
{
    public string Title { get; private set; } = "";

    public void OnGet(int code)
    {
        Title = code switch
        {
            403 => "Доступ запрещён",
            404 => "Страница не найдена",
            409 => "Действие уже выполнено или сейчас недоступно",
            400 => "Неверный запрос",
            _ => $"Ошибка {code}",
        };
    }

    public void OnPost(int code) => OnGet(code);
}
