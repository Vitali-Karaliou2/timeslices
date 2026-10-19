using Microsoft.AspNetCore.Mvc;
using TimeSlices.Web.Collection;

namespace TimeSlices.Web.Pages.Reference;

/// <summary>Адреса справки (ADR 0013): /reference/{id} и якорь раздела.</summary>
public static class ReferenceLinks
{
    public static string ReferenceHref(this IUrlHelper url, ReferenceTarget target) =>
        url.Page("/Reference/Article", pageHandler: null, values: new { id = target.Article.Id },
            protocol: null, host: null, fragment: target.Section?.Id)!;
}
