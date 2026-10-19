using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;

namespace TimeSlices.Web.Pages.Reference;

/// <summary>
/// Справочная статья: разделы с якорями, «См. также», «Источники», «Где упоминается» (ADR 0013, п. 8).
/// Открыта без входа; срез в «Где упоминается» показан названием, а его страница сама потребует входа.
/// </summary>
[AllowAnonymous]
public class ArticleModel(ContentCatalog catalog) : PageModel
{
    public ContentCatalog Catalog => catalog;
    public Article Article { get; private set; } = null!;
    public IReadOnlyList<ReferenceMention> Mentions { get; private set; } = [];

    public IActionResult OnGet(string id)
    {
        if (!catalog.Articles.TryGetValue(id, out var article))
            return NotFound();
        Article = article;
        Mentions = catalog.MentionsOf(id);
        return Page();
    }

    /// <summary>
    /// Срез, на странице которого открыть пример с кадром: срез реализации, если он перечисляет пример,
    /// иначе срез, где пример введён.
    /// </summary>
    public string ExampleSliceOf(ReferenceMention m) =>
        catalog.Slices.TryGetValue(m.SliceId, out var s) && s.Examples.Contains(m.ExampleId!)
            ? m.SliceId
            : catalog.Examples[m.ExampleId!].IntroducedInSlice;
}
