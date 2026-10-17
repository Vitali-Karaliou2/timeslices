using Microsoft.AspNetCore.DataProtection;

namespace TimeSlices.Web.Collection;

/// <summary>
/// Доступ к статическим сборкам примеров для iframe.
/// iframe с sandbox без allow-same-origin живёт в «непрозрачном» источнике, и браузер не шлёт
/// cookie входа на запросы его ресурсов (CSS, картинки, скрипты). Поэтому страница примера,
/// проверив, что срез открыт пользователю, выдаёт короткоживущий токен на этот пример;
/// токен — сегмент пути, и относительные ссылки внутри сборки наследуют его.
/// </summary>
public sealed class ContentAccess(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("TimeSlices.ContentAccess").ToTimeLimitedDataProtector();

    public string IssueToken(Example example) =>
        _protector.Protect(example.Id, clock.GetUtcNow() + Lifetime);

    /// <summary>Токен действителен и выдан на этот пример.</summary>
    public bool Allows(string token, Example example)
    {
        try
        {
            return _protector.Unprotect(token, out var expiration) == example.Id && expiration > clock.GetUtcNow();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
