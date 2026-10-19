using Microsoft.AspNetCore.Html;

namespace TimeSlices.Web.Collection;

/// <summary>
/// Пометка «ЧЕРНОВИК» (CLAUDE.md). В файлах содержания тексты для обучаемого начинаются с префикса
/// <c>ЧЕРНОВИК:</c>, пока автор его не снимет. Оболочка при показе убирает префикс из текста и вместо
/// него ставит один значок «черновик» у блока (поле среза, кадр процесса, вопрос). Правило — только здесь.
/// </summary>
public static class DraftMark
{
    public const string Prefix = "ЧЕРНОВИК:";

    /// <summary>Текст помечен как черновик.</summary>
    public static bool IsDraft(string? text) =>
        text is not null && text.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Блок помечен как черновик, если помечен хотя бы один его текст.</summary>
    public static bool AnyDraft(params string?[] texts) => texts.Any(IsDraft);

    /// <summary>Текст без префикса; null — пустая строка. Префикс в середине текста не трогается.</summary>
    public static string Strip(string? text)
    {
        if (text is null)
            return "";
        return IsDraft(text) ? text.TrimStart()[Prefix.Length..].TrimStart() : text;
    }

    /// <summary>То же, что <see cref="Strip"/>, но пустой результат — null (для необязательных полей).</summary>
    public static string? StripOrNull(string? text) => Strip(text) is { Length: > 0 } s ? s : null;

    /// <summary>Значок у блока: пустой, если блок не черновик.</summary>
    public static IHtmlContent Badge(bool draft) =>
        draft ? new HtmlString("<span class=\"draft-badge\" title=\"текст не утверждён автором\">черновик</span> ") : HtmlString.Empty;

    /// <summary>Значок у блока из одного текста.</summary>
    public static IHtmlContent BadgeFor(params string?[] texts) => Badge(AnyDraft(texts));
}
