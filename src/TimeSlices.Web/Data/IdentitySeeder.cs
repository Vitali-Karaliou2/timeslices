using Microsoft.AspNetCore.Identity;

namespace TimeSlices.Web.Data;

public static class Roles
{
    public const string Learner = "Learner";
    public const string Examiner = "Examiner";
    public const string Admin = "Admin";

    public static readonly string[] All = [Learner, Examiner, Admin];
}

/// <summary>Dev-пользователь из конфигурации (раздел DevUsers в appsettings.Development.json).</summary>
public sealed class DevUser
{
    public string UserName { get; init; } = "";
    public string Password { get; init; } = "";
    public string Role { get; init; } = "";
}

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole(role)), $"роль {role}");
    }

    /// <summary>Создаёт недостающих dev-пользователей. Существующих не трогает (пароль не перезаписывается).</summary>
    public static async Task SeedDevUsersAsync(UserManager<ApplicationUser> users, IEnumerable<DevUser> devUsers)
    {
        foreach (var dev in devUsers)
        {
            if (!Roles.All.Contains(dev.Role))
                throw new InvalidOperationException($"DevUsers: неизвестная роль '{dev.Role}' у {dev.UserName}");

            var user = await users.FindByNameAsync(dev.UserName);
            if (user is null)
            {
                user = new ApplicationUser { UserName = dev.UserName, Email = dev.UserName, EmailConfirmed = true };
                Check(await users.CreateAsync(user, dev.Password), $"пользователь {dev.UserName}");
            }
            if (!await users.IsInRoleAsync(user, dev.Role))
                Check(await users.AddToRoleAsync(user, dev.Role), $"роль {dev.Role} для {dev.UserName}");
        }
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Не удалось создать {what}: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
