using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TimeSlices.Tests;

/// <summary>
/// scripts/validate_manifest.py на slice.json: необязательное поле relevanceNote (ADR 0010).
/// Валидатор — Python без зависимостей; тест запускает его так же, как хук и validate_all.py.
/// </summary>
public sealed class ValidatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timeslices-tests", $"{Guid.NewGuid():N}", "test-slice");

    public ValidatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

    [Theory]
    [InlineData(null, 0)]                                     // поля нет — только пометка
    [InlineData("\"ЧЕРНОВИК: пояснение\"", 0)]
    [InlineData("\"\"", 1)]
    [InlineData("\"   \"", 1)]
    [InlineData("42", 1)]
    public void SliceJson_RelevanceNote_IfPresent_IsNonEmptyString(string? noteJson, int exitCode)
    {
        var slice = new JsonObject
        {
            ["id"] = "test-slice", ["title"] = "t", ["yearFrom"] = 1990, ["yearTo"] = 1991,
            ["tracks"] = new JsonArray("frontend"), ["introduces"] = "t", ["relevance"] = "b",
            ["prerequisites"] = new JsonArray(), ["linkBack"] = "t", ["linkForward"] = "t",
            ["whyNeededToReviewModelCode"] = new string('x', 50), ["examples"] = new JsonArray(), ["status"] = "draft",
        };
        if (noteJson is not null)
            slice["relevanceNote"] = JsonNode.Parse(noteJson);
        var path = Path.Combine(_dir, "slice.json");
        File.WriteAllText(path, slice.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var (code, output) = Validate(path);

        Assert.True(code == exitCode, output);
        if (exitCode == 1)
            Assert.Contains("relevanceNote", output);
    }

    [Theory]
    [InlineData("{\"linkBack\": \"С чего начинаем\"}", 0)]
    [InlineData("{\"linkBack\": \"ЧЕРНОВИК: С чего начинаем\", \"relevance\": \"x\"}", 0)]
    [InlineData("{\"examples\": \"Примеры\"}", 1)]      // неизвестный ключ
    [InlineData("{\"introduces\": \"\"}", 1)]
    [InlineData("{\"introduces\": \"  \"}", 1)]
    [InlineData("[\"С чего начинаем\"]", 1)]
    public void SliceJson_Headings_OnlyKnownKeysWithNonEmptyStrings(string headingsJson, int exitCode)
    {
        var slice = MinimalSlice();
        slice["headings"] = JsonNode.Parse(headingsJson);
        var path = Path.Combine(_dir, "slice.json");
        File.WriteAllText(path, slice.ToJsonString());

        var (code, output) = Validate(path);

        Assert.True(code == exitCode, output);
        if (exitCode == 1)
            Assert.Contains("headings", output);
    }

    [Fact]
    public void SliceJson_Headings_LongerThan80_IsError()
    {
        var slice = MinimalSlice();
        slice["headings"] = new JsonObject { ["linkForward"] = new string('я', 81) };
        var path = Path.Combine(_dir, "slice.json");
        File.WriteAllText(path, slice.ToJsonString());

        var (code, output) = Validate(path);

        Assert.True(code == 1, output);
        Assert.Contains("80", output);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void ArticleJson_WithoutSections_IsError(bool withSections, int exitCode)
    {
        var dir = Path.Combine(Path.GetDirectoryName(_dir)!, "reference", "dns");
        Directory.CreateDirectory(dir);
        var article = new JsonObject
        {
            ["id"] = "dns", ["title"] = "t", ["summary"] = "s", ["see"] = new JsonArray(),
            ["sources"] = new JsonArray(new JsonObject { ["title"] = "RFC 1034", ["url"] = "https://www.rfc-editor.org/rfc/rfc1034" }),
            ["status"] = "draft",
        };
        if (withSections)
            article["sections"] = new JsonArray(new JsonObject
            {
                ["id"] = "1992", ["heading"] = "h", ["asOf"] = 1992, ["paragraphs"] = new JsonArray("ЧЕРНОВИК: p"),
            });
        var path = Path.Combine(dir, "article.json");
        File.WriteAllText(path, article.ToJsonString());

        var (code, output) = Validate(path);

        Assert.True(code == exitCode, output);
        if (exitCode == 1)
            Assert.Contains("sections", output);
    }

    private static JsonObject MinimalSlice() => new()
    {
        ["id"] = "test-slice", ["title"] = "t", ["yearFrom"] = 1990, ["yearTo"] = 1991,
        ["tracks"] = new JsonArray("frontend"), ["introduces"] = "t", ["relevance"] = "b",
        ["prerequisites"] = new JsonArray(), ["linkBack"] = "t", ["linkForward"] = "t",
        ["whyNeededToReviewModelCode"] = new string('x', 50), ["examples"] = new JsonArray(), ["status"] = "draft",
    };

    private static (int, string) Validate(string path)
    {
        var script = Path.Combine(TestContent.RealContentRoot(), "..", "..", "..", "scripts", "validate_manifest.py");
        // Установщик с python.org на Windows даёт команду python, но не python3 (а python3 там — заглушка Microsoft Store).
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        var start = new ProcessStartInfo(python)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        // На Windows перенаправленный вывод Python идёт в кодировке системы, и русские сообщения валидатора
        // падают с UnicodeEncodeError; UTF-8 задаётся явно.
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(path);
        Process process;
        try { process = Process.Start(start)!; }
        catch (System.ComponentModel.Win32Exception e) { throw new InvalidOperationException($"для теста валидатора нужен {python} (Python 3) в PATH", e); }
        using var _ = process;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
