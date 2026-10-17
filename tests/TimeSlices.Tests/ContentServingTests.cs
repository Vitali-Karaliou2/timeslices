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
    public void AccessToken_OpensOnlyItsImplementationAndExpires()
    {
        var catalog = _content.Load();
        var clock = new ManualClock();
        var access = new ContentAccess(new EphemeralDataProtectionProvider(), clock);
        var impl1 = catalog.FindServableFile(P("slices", "a", "examples", "ex", "implementations", "impl-1", "index.html"))!.Value;
        var impl2 = catalog.FindServableFile(P("slices", "a", "examples", "ex", "implementations", "impl-2", "code", "index.html"))!.Value;

        var token = access.IssueToken(impl1.Example, impl1.Implementation);

        Assert.Equal(("ex", "impl-1"), (impl1.Example.Id, impl1.Implementation.Id));
        Assert.True(access.Allows(token, impl1.Example, impl1.Implementation));
        Assert.False(access.Allows(token, impl2.Example, impl2.Implementation));
        Assert.False(access.Allows("forged", impl1.Example, impl1.Implementation));
        clock.Now += ContentAccess.Lifetime + TimeSpan.FromMinutes(1);
        Assert.False(access.Allows(token, impl1.Example, impl1.Implementation));
    }

    [Fact]
    public void ImplementationOfUnknownSlice_FailsAtLoad()
    {
        var manifest = P("slices", "a", "examples", "ex", "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"slice\":\"a\"", "\"slice\":\"later\""));

        var ex = Assert.Throws<ContentLoadException>(_content.Load);

        Assert.Contains(ex.Problems, p => p.Contains("'impl-1' — 'later' — не найден"));
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
