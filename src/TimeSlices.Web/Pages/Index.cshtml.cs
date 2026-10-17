using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages;

public class IndexModel(ContentCatalog catalog, SliceProgressService progress, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public IReadOnlyDictionary<ThematicSliceKey, SliceStatus> Statuses { get; private set; } = new Dictionary<ThematicSliceKey, SliceStatus>();

    public async Task OnGetAsync()
    {
        Statuses = await progress.GetStatusesAsync(users.GetUserId(User)!);
    }
}
