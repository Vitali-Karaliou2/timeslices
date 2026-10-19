using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Admin;

/// <summary>Список пользователей, их ролей и состояний; администратор назначает роль (ADR 0011, п. 6).</summary>
public class IndexModel(ContentCatalog catalog, SliceProgressService progress, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public List<(ApplicationUser User, IList<string> Roles, IReadOnlyDictionary<ThematicSliceKey, SliceStatus> Statuses)> Rows { get; } = [];

    public string CurrentUserId => users.GetUserId(User)!;

    public async Task OnGetAsync()
    {
        foreach (var user in await users.Users.OrderBy(u => u.UserName).ToListAsync())
            Rows.Add((user, await users.GetRolesAsync(user), await progress.GetStatusesAsync(user.Id)));
    }

    /// <summary>
    /// У пользователя ровно одна роль из трёх. Свою роль администратор не меняет:
    /// иначе последний администратор мог бы закрыть себе /admin.
    /// </summary>
    public async Task<IActionResult> OnPostSetRoleAsync(string userId, string role)
    {
        if (!Roles.All.Contains(role) || userId == CurrentUserId)
            return BadRequest();
        if (await users.FindByIdAsync(userId) is not { } user)
            return NotFound();
        var current = await users.GetRolesAsync(user);
        if (current.Count != 1 || current[0] != role)
        {
            Check(await users.RemoveFromRolesAsync(user, current));
            Check(await users.AddToRoleAsync(user, role));
            // Роли берутся из cookie входа. Новый штамп заставит открытую сессию пользователя перечитать роли
            // при ближайшей проверке штампа (по умолчанию — раз в 30 минут), до тех пор действуют старые.
            Check(await users.UpdateSecurityStampAsync(user));
        }
        return RedirectToPage();
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
