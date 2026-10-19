using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages;

public class IndexModel(ContentCatalog catalog, SliceProgressService progress, SelfCheckService selfCheck, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public IReadOnlyDictionary<ThematicSliceKey, SliceStatus> Statuses { get; private set; } = new Dictionary<ThematicSliceKey, SliceStatus>();

    /// <summary>Последние попытки сдачи по тематическим срезам (ADR 0011, п. 4).</summary>
    public IReadOnlyDictionary<ThematicSliceKey, Attempt> Attempts { get; private set; } = new Dictionary<ThematicSliceKey, Attempt>();

    public async Task OnGetAsync()
    {
        var userId = users.GetUserId(User)!;
        Statuses = await progress.GetStatusesAsync(userId);
        Attempts = await selfCheck.LatestByKeyAsync(userId);
    }
}
