using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Slices;

public class SliceModel(ContentCatalog catalog, SliceProgressService progress, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public Slice Slice { get; private set; } = null!;
    public ThematicSliceKey Key { get; private set; }
    public SliceStatus Status { get; private set; }
    public IReadOnlyDictionary<ThematicSliceKey, SliceStatus> Statuses { get; private set; } = new Dictionary<ThematicSliceKey, SliceStatus>();

    /// <summary>Пройденный срез, после которого пользователь продвинулся дальше: нужен выбор при повторе.</summary>
    public bool HasAdvanced { get; private set; }

    /// <summary>Несколько вариантов «Далее» — пользователь выбирает (null — выбор не показывается).</summary>
    public IReadOnlyList<ThematicSliceKey>? NextChoices { get; private set; }

    private string UserId => users.GetUserId(User)!;

    public async Task<IActionResult> OnGetAsync(string id, string? track)
    {
        if (!Resolve(id, track))
            return NotFound();
        Status = await progress.OpenAsync(UserId, Key);
        return await ShowAsync();
    }

    public async Task<IActionResult> OnPostDoneAsync(string id, string? track)
    {
        if (!Resolve(id, track))
            return NotFound();
        if (!await progress.MarkDoneAsync(UserId, Key))
            return Forbid();
        return RedirectToPage("/Slices/Slice", new { id, track = Key.TrackId });
    }

    public async Task<IActionResult> OnGetNextAsync(string id, string? track)
    {
        if (!Resolve(id, track))
            return NotFound();
        var next = await progress.GetNextAsync(UserId, Key);
        if (next.Count == 1)
            return RedirectToPage("/Slices/Slice", new { id = next[0].SliceId, track = next[0].TrackId });
        NextChoices = next;
        Status = await progress.GetStatusAsync(UserId, Key);
        return await ShowAsync();
    }

    public async Task<IActionResult> OnPostRepeatAsync(string id, string? track, bool resetFollowing)
    {
        if (!Resolve(id, track))
            return NotFound();
        if (!await progress.RepeatAsync(UserId, Key, resetFollowing))
            return BadRequest();
        return RedirectToPage("/Slices/Slice", new { id, track = Key.TrackId });
    }

    private bool Resolve(string id, string? track)
    {
        if (!catalog.Slices.TryGetValue(id, out var slice))
            return false;
        track ??= slice.Tracks[0];
        if (!slice.Tracks.Contains(track))
            return false;
        Slice = slice;
        Key = new ThematicSliceKey(id, track);
        return true;
    }

    private async Task<IActionResult> ShowAsync()
    {
        Statuses = await progress.GetStatusesAsync(UserId);
        if (Status.State == SliceState.Unavailable)
        {
            // Содержание недоступного среза не показываем, даже при прямом переходе по URL.
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }
        if (Status.State == SliceState.Done)
            HasAdvanced = await progress.HasAdvancedAsync(UserId, Key);
        return Page();
    }
}
