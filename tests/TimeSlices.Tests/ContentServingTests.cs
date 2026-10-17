using Microsoft.AspNetCore.DataProtection;
using TimeSlices.Web.Collection;

namespace TimeSlices.Tests;

/// <summary>Статические сборки для iframe (docs/acceptance.md, п. 6): отдаются только папки реализаций.</summary>
public class ContentServingTests : IDisposable
{
    private readonly TestContent _content = new TestContent().Track("t")
        .Slice("a", ["t"], [], ["ex"])
        .Example("a", "ex", "impl-1", "impl-2");

    public void Dispose() => _content.Dispose();

    private string P(params string[] parts) => Path.Combine([_content.Root, .. parts]);

    [Fact]
    public void ExampleWithSeveralImplementations_IsLoadedWithDirectories()
    {
        var catalog = _content.Load();

        var example = catalog.Examples["ex"];
        Assert.Equal(["impl-1", "impl-2"], example.Implementations.Select(i => i.Id));
        Assert.Equal(["ex"], catalog.ExamplesOf(catalog.Slices["a"]).Select(e => e.Id));
        Assert.True(File.Exists(Path.Combine(example.Implementations[1].Directory, "index.html")));
    }

    [Fact]
    public void OnlyFilesInsideImplementationFolders_AreServable()
    {
        var catalog = _content.Load();
        var impl = P("slices", "a", "examples", "ex", "implementations", "impl-1");

        Assert.True(catalog.IsServable(Path.Combine(impl, "index.html")));
        Assert.True(catalog.IsServable(Path.Combine(impl, "code", "index.html")));
        Assert.False(catalog.IsServable(P("slices", "a", "questions.json")));
        Assert.False(catalog.IsServable(P("slices", "a", "examples", "ex", "manifest.json")));
        Assert.False(catalog.IsServable(Path.Combine(impl, "..", "..", "manifest.json")));
        Assert.False(catalog.IsServable(Path.Combine(impl, "missing.html")));
    }

    [Fact]
    public void ImplementationWithoutEntry_FailsAtLoad()
    {
        File.Delete(P("slices", "a", "examples", "ex", "implementations", "impl-2", "index.html"));

        var ex = Assert.Throws<TimeSlices.Web.Collection.ContentLoadException>(_content.Load);

        Assert.Contains(ex.Problems, p => p.Contains("'impl-2' не найден entry"));
    }

    [Theory]
    [InlineData("\"id\":\"impl-1\"", "\"id\":\"..\"", "должен быть kebab-case")]
    [InlineData("\"entry\":\"index.html\"", "\"entry\":\"../../manifest.json\"", "(entry) '../../manifest.json' выходит за папку")]
    [InlineData("\"path\":\"code/index.html\"", "\"path\":\"../../../questions.json\"", "(код) '../../../questions.json' выходит за папку")]
    public void ManifestPathsOutsideImplementationFolder_FailAtLoad(string from, string to, string expected)
    {
        var manifest = P("slices", "a", "examples", "ex", "manifest.json");
        var json = File.ReadAllText(manifest);
        var at = json.IndexOf(from, StringComparison.Ordinal);
        File.WriteAllText(manifest, json[..at] + to + json[(at + from.Length)..]); // только первое вхождение

        var ex = Assert.Throws<TimeSlices.Web.Collection.ContentLoadException>(_content.Load);

        Assert.Contains(ex.Problems, p => p.Contains(expected));
    }

    [Fact]
    public void AccessToken_OpensOnlyItsExampleAndExpires()
    {
        using var content = new TestContent().Track("t")
            .Slice("a", ["t"], [], ["ex", "other"])
            .Example("a", "ex", "impl")
            .Example("a", "other", "impl");
        var catalog = content.Load();
        var clock = new ManualClock();
        var access = new ContentAccess(new EphemeralDataProtectionProvider(), clock);
        var file = Path.Combine(content.Root, "slices", "a", "examples", "ex", "implementations", "impl", "index.html");
        var example = catalog.FindServableFile(file)!;

        var token = access.IssueToken(example);

        Assert.Equal("ex", example.Id);
        Assert.True(access.Allows(token, example));
        Assert.False(access.Allows(token, catalog.Examples["other"]));
        Assert.False(access.Allows("forged", example));
        clock.Now += ContentAccess.Lifetime + TimeSpan.FromMinutes(1);
        Assert.False(access.Allows(token, example));
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
