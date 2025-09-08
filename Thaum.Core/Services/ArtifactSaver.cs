using System.Text;
using Thaum.Core.Models;
using Thaum.Core.Triads;
using Thaum.Core.Eval;

namespace Thaum.Core.Services;

public static class ArtifactSaver {
    public static async Task SaveSessionAsync(CodeSymbol symbol, string filePath, string prompt, string? response, string? triadJsonPath) {
        string sessionRoot = Path.Combine(GLB.CacheDir, "sessions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(sessionRoot);

        string safeName = MakeSafe(symbol.Name);
        string promptPath   = Path.Combine(sessionRoot, $"{safeName}.prompt.txt");
        string? responsePath = response != null ? Path.Combine(sessionRoot, $"{safeName}.response.txt") : null;

        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8);
        if (responsePath != null) {
            await File.WriteAllTextAsync(responsePath, response, Encoding.UTF8);
            // Attempt to parse triad and save JSON
            var triad = TriadSerializer.ParseTriadText(response, symbol, filePath, null);
            string triadPath = Path.Combine(sessionRoot, $"{safeName}.triad.json");
            await TriadSerializer.SaveTriadAsync(triad, triadPath);
        }
    }

    public static async Task SaveFidelityAsync(CodeSymbol symbol, FidelityReport report) {
        string sessionRoot = Path.Combine(GLB.CacheDir, "sessions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(sessionRoot);
        string safeName = MakeSafe(symbol.Name);
        string path = Path.Combine(sessionRoot, $"{safeName}.fidelity.json");
        var json = System.Text.Json.JsonSerializer.Serialize(report, GLB.JsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    static string MakeSafe(string name) {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
