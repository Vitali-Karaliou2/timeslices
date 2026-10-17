using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Web.Pages.Slices;

public class ExampleModel(ContentCatalog catalog, SliceProgressService progress, ContentAccess access, UserManager<ApplicationUser> users) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public Slice Slice { get; private set; } = null!;
    public string TrackId { get; private set; } = "";
    public Example Example { get; private set; } = null!;
    public Implementation Implementation { get; private set; } = null!;

    /// <summary>Реализации, которые можно выбрать: их срезы открыты пользователю.</summary>
    public List<Implementation> Implementations { get; private set; } = [];
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
        var statuses = await progress.GetStatusesAsync(users.GetUserId(User)!);
        Status = statuses[new ThematicSliceKey(id, track)];
        if (Status.State == SliceState.Unavailable)
            return RedirectToPage("/Slices/Slice", new { id, track });

        // Реализации, привязанные к недоступным срезам («отыгранные вперёд»), не показываются.
        Implementations = Example.Implementations
            .Where(i => catalog.Slices[i.Slice].Tracks.Any(t => statuses[new ThematicSliceKey(i.Slice, t)].State != SliceState.Unavailable))
            .ToList();

        // По умолчанию — реализация, привязанная к этому срезу; иначе первая из открытых.
        var chosen = impl is null
            ? Implementations.FirstOrDefault(i => i.Slice == id) ?? Implementations.FirstOrDefault()
            : Implementations.FirstOrDefault(i => i.Id == impl);
        if (chosen is null)
            return NotFound();
        Implementation = chosen;

        EntryUrl = ContentUrl(Path.Combine(chosen.Directory, chosen.Entry));
        foreach (var file in chosen.CodeFiles)
        {
            var full = Path.Combine(chosen.Directory, file.Path); // что путь внутри папки реализации, проверил ContentLoader
            var lines = System.IO.File.Exists(full)
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
        var token = Uri.EscapeDataString(access.IssueToken(Example, Implementation));
        return Url.Content($"~/content/{token}/" + string.Join('/', relative));
    }
}
