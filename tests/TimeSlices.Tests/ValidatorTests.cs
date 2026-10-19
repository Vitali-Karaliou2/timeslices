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

    private static (int, string) Validate(string path)
    {
        var script = Path.Combine(TestContent.RealContentRoot(), "..", "..", "..", "scripts", "validate_manifest.py");
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
