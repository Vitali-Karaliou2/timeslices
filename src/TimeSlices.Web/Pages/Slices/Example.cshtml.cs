using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Slices;

public class ExampleModel(ContentCatalog catalog, SliceProgressService progress, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public Slice Slice { get; private set; } = null!;
    public string TrackId { get; private set; } = "";
    public Example Example { get; private set; } = null!;
    public Implementation Implementation { get; private set; } = null!;
    public SliceStatus Status { get; private set; }

    /// <summary>Адрес статической сборки реализации для iframe.</summary>
    public string EntryUrl { get; private set; } = "";

    /// <summary>Код для показа: путь, пометка и строки файла.</summary>
    public List<(CodeFile File, string[] Lines)> Code { get; } = [];

    public async Task<IActionResult> OnGetAsync(string id, string exampleId, string? track, string? impl)
    {
        if (!catalog.Slices.TryGetValue(id, out var slice) || !slice.Examples.Contains(exampleId))
            return NotFound();
        track ??= slice.Tracks[0];
        if (!slice.Tracks.Contains(track))
            return NotFound();

        Slice = slice;
        TrackId = track;
        Example = catalog.Examples[exampleId];
        Status = await progress.GetStatusAsync(users.GetUserId(User)!, new ThematicSliceKey(id, track));
        if (Status.State == SliceState.Unavailable)
            return RedirectToPage("/Slices/Slice", new { id, track });

        // По умолчанию — реализация, привязанная к этому срезу; иначе первая.
        var chosen = impl is null
            ? Example.Implementations.FirstOrDefault(i => i.Slice == id) ?? Example.Implementations.FirstOrDefault()
            : Example.Implementations.FirstOrDefault(i => i.Id == impl);
        if (chosen is null)
            return NotFound();
        Implementation = chosen;

        EntryUrl = ContentUrl(Path.Combine(chosen.Directory, chosen.Entry));
        var implDir = Path.GetFullPath(chosen.Directory) + Path.DirectorySeparatorChar;
        foreach (var file in chosen.CodeFiles)
        {
            var full = Path.GetFullPath(Path.Combine(chosen.Directory, file.Path));
            var lines = full.StartsWith(implDir, StringComparison.Ordinal) && System.IO.File.Exists(full)
                ? await System.IO.File.ReadAllLinesAsync(full)
                : ["(файл не найден)"];
            Code.Add((file, lines));
        }
        return Page();
    }

    private string ContentUrl(string fullPath)
    {
        var relative = Path.GetRelativePath(catalog.Root, fullPath)
            .Split(Path.DirectorySeparatorChar)
            .Select(Uri.EscapeDataString);
        return Url.Content("~/content/" + string.Join('/', relative));
    }
}
