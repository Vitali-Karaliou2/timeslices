using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Web.Pages.Examiner;

/// <summary>Демо-экзаменатор (brief 8.3, ADR 0011, п. 5): все отправленные попытки. Роли Examiner и Admin.</summary>
public class IndexModel(ContentCatalog catalog, SelfCheckService selfCheck, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public IReadOnlyList<Attempt> Attempts { get; private set; } = [];
    public IReadOnlyDictionary<string, string> UserNames { get; private set; } = new Dictionary<string, string>();

    public async Task OnGetAsync()
    {
        Attempts = await selfCheck.SubmittedAttemptsAsync();
        var ids = Attempts.Select(a => a.UserId).Distinct().ToList();
        UserNames = await users.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Id);
    }

    public string SliceTitle(string sliceId) => catalog.Slices.TryGetValue(sliceId, out var s) ? s.Title : sliceId;
}
