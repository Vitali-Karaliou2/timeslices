using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using TimeSlices.Web.Data;

namespace TimeSlices.Web.Areas.Identity.Pages.Account;

/// <summary>
/// Регистрация (заменяет страницу Identity UI, ADR 0011, п. 6). Новый пользователь — всегда Learner.
/// В Development RequireConfirmedAccount = false: сразу вход. Иначе — как раньше: письмо
/// с подтверждением (отправителя пока нет) и страница RegisterConfirmation.
/// </summary>
[AllowAnonymous]
public class RegisterModel(
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IEmailSender emailSender) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите почту.")]
        [EmailAddress(ErrorMessage = "Это не адрес почты.")]
        [Display(Name = "Почта")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль — от {2} до {1} символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Пароль ещё раз")]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
        var created = await users.CreateAsync(user, Input.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        var role = await users.AddToRoleAsync(user, Roles.Learner);
        if (!role.Succeeded)
        {
            await users.DeleteAsync(user); // пользователь без роли не остаётся
            throw new InvalidOperationException("Не удалось назначить роль Learner: " + string.Join("; ", role.Errors.Select(e => e.Description)));
        }

        if (users.Options.SignIn.RequireConfirmedAccount)
        {
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
            var callbackUrl = Url.Page("/Account/ConfirmEmail", pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code, returnUrl }, protocol: Request.Scheme)!;
            await emailSender.SendEmailAsync(Input.Email, "Подтверждение почты",
                $"Подтвердите почту: <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>ссылка</a>.");
            return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return LocalRedirect(returnUrl);
    }
}
