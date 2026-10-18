using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

var builder = WebApplication.CreateBuilder(args);

// Содержание коллекции читается один раз при старте; ошибка в нём (в т. ч. цикл в графе) — отказ стартовать.
var contentRoot = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Content:Root"] ?? "Content");
builder.Services.AddSingleton(ContentLoader.Load(contentRoot));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Всё, кроме страниц входа/регистрации и статических сборок примеров, — только после входа.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("AdminOnly", p => p.RequireRole(Roles.Admin));
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Admin", "AdminOnly"));
// Кириллица в разметке как есть, а не &#x...; (экранирование <, >, & и кавычек сохраняется).
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SliceProgressService>();
builder.Services.AddSingleton<ContentAccess>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await IdentitySeeder.SeedRolesAsync(services.GetRequiredService<RoleManager<IdentityRole>>());
    if (app.Environment.IsDevelopment())
    {
        var devUsers = app.Configuration.GetSection("DevUsers").Get<List<DevUser>>() ?? [];
        await IdentitySeeder.SeedDevUsersAsync(services.GetRequiredService<UserManager<ApplicationUser>>(), devUsers);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Статические сборки реализаций для iframe. Оболочка отдаёт файлы как есть и не знает их стек;
// наружу — только содержимое папок implementations/<impl-id>/ (не questions.json и не манифесты)
// и только по токену, который страница примера выдаёт, проверив, что срез реализации открыт (ContentAccess).
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/content/{token}/{**path}", (string token, string path, ContentCatalog catalog, ContentAccess access) =>
{
    var full = Path.GetFullPath(Path.Combine(catalog.Root, path));
    if (catalog.FindServableFile(full) is not var (example, implementation) || !access.Allows(token, example, implementation))
        return Results.NotFound();
    if (!contentTypes.TryGetContentType(full, out var type))
        type = "application/octet-stream";
    // Файлы содержания хранятся в UTF-8; без charset браузер прочёл бы кириллицу в однобайтовой кодировке.
    else if (type.StartsWith("text/", StringComparison.Ordinal) || type == "application/javascript")
        type += "; charset=utf-8";
    return Results.File(full, type);
}).AllowAnonymous();

app.Run();

// Точка входа для HTTP-тестов (WebApplicationFactory<Program>).
public partial class Program;
