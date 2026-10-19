using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TimeSlices.Web.Collection;

namespace TimeSlices.Web.Pages.Reference;

/// <summary>Список справочных статей. Справка открыта без входа (ADR 0013, п. 8).</summary>
[AllowAnonymous]
public class IndexModel(ContentCatalog catalog) : PageModel
{
    public ContentCatalog Catalog => catalog;
}
