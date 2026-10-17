using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;
using TimeSlices.Web.Progress;

namespace TimeSlices.Tests;

/// <summary>Содержание коллекции во временной папке: тесты не зависят от реального Content/.</summary>
public sealed class TestContent : IDisposable
{
    private readonly List<object> _tracks = [];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "timeslices-tests", Guid.NewGuid().ToString("N"));

    public TestContent()
    {
        Directory.CreateDirectory(Path.Combine(Root, "slices"));
    }

    public TestContent Track(string id)
    {
        _tracks.Add(new { id, title = id, order = _tracks.Count + 1 });
        File.WriteAllText(Path.Combine(Root, "tracks.json"), JsonSerializer.Serialize(_tracks));
        return this;
    }

    /// <param name="prerequisites">Предшественники в виде "срез@трек".</param>
    public TestContent Slice(string id, string track, params string[] prerequisites) =>
        Slice(id, [track], prerequisites);

    public TestContent Slice(string id, string[] tracks, string[] prerequisites, string[]? examples = null)
    {
        var dir = Path.Combine(Root, "slices", id);
        Directory.CreateDirectory(dir);
        var slice = new
        {
            id,
            title = id,
            yearFrom = 1990,
            yearTo = 1991,
            tracks,
            introduces = "test",
            relevance = "b",
            prerequisites = prerequisites.Select(p => p.Split('@')).Select(p => new { slice = p[0], track = p[1], why = "test" }),
            linkBack = "",
            linkForward = "",
            whyNeededToReviewModelCode = "test",
            examples = examples ?? [],
            status = "draft",
        };
        File.WriteAllText(Path.Combine(dir, "slice.json"), JsonSerializer.Serialize(slice));
        return this;
    }

    /// <summary>Пример с реализациями-заглушками (index.html) в папке среза.</summary>
    public TestContent Example(string sliceId, string exampleId, params string[] implementationIds)
    {
        var dir = Path.Combine(Root, "slices", sliceId, "examples", exampleId);
        foreach (var impl in implementationIds)
        {
            Directory.CreateDirectory(Path.Combine(dir, "implementations", impl, "code"));
            File.WriteAllText(Path.Combine(dir, "implementations", impl, "index.html"), "<HTML></HTML>");
            File.WriteAllText(Path.Combine(dir, "implementations", impl, "code", "index.html"), "<HTML></HTML>");
        }
        var manifest = new
        {
            id = exampleId,
            title = exampleId,
            task = "test",
            introducedInSlice = sliceId,
            complexity = 1,
            implementations = implementationIds.Select(i => new
            {
                id = i, slice = sliceId, stack = "test", entry = "index.html", codeMode = "executable",
                codeFiles = new[] { new { path = "code/index.html", note = "" } },
                processes = Array.Empty<object>(),
                anachronismCheck = new { by = "pending", date = (string?)null, notes = "" },
            }),
        };
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(Root, "slices", sliceId, "questions.json"), "{\"questions\":[]}");
        return this;
    }

    public ContentCatalog Load() => ContentLoader.Load(Root);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Путь к реальному Content/ оболочки (ищется вверх от папки тестов до TimeSlices.sln).</summary>
    public static string RealContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TimeSlices.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("не найден TimeSlices.sln"),
            "src", "TimeSlices.Web", "Content");
    }
}

/// <summary>SQLite в памяти + SliceProgressService для одного пользователя.</summary>
public sealed class ProgressFixture : IDisposable
{
    public const string UserId = "user-1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ProgressFixture(ContentCatalog catalog)
    {
        _connection.Open();
        Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();
        Db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId });
        Db.SaveChanges();
        Service = new SliceProgressService(Db, catalog, TimeProvider.System);
    }

    public ApplicationDbContext Db { get; }
    public SliceProgressService Service { get; }

    public async Task<SliceStatus> StatusOf(string key)
    {
        var p = key.Split('@');
        return await Service.GetStatusAsync(UserId, new ThematicSliceKey(p[0], p[1]));
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
