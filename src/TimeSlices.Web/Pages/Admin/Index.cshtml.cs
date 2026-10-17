using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Admin;

/// <summary>Список пользователей и их состояний. Демо-экзаменатор появится здесь же позже.</summary>
public class IndexModel(ContentCatalog catalog, SliceProgressService progress, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public List<(ApplicationUser User, IList<string> Roles, IReadOnlyDictionary<ThematicSliceKey, SliceStatus> Statuses)> Rows { get; } = [];

    public async Task OnGetAsync()
    {
        foreach (var user in await users.Users.OrderBy(u => u.UserName).ToListAsync())
            Rows.Add((user, await users.GetRolesAsync(user), await progress.GetStatusesAsync(user.Id)));
    }
}
