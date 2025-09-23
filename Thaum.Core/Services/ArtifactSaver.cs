using System.Text;
using Thaum.Core.Crawling;
using Thaum.Core.Triads;
using Thaum.Core.Eval;

namespace Thaum.Core.Services;

public record SessionSaveResult(string PromptPath, string? ResponsePath, string TriadPath);

public static class ArtifactSaver {
	public static async Task<SessionSaveResult> SaveSessionAsync(CodeSymbol symbol, string filePath, string prompt, string? response, string? triadJsonPath, string? sessionRoot = null, string? fileSuffix = null) {
		sessionRoot ??= Path.Combine(GLB.CacheDir, "sessions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
		Directory.CreateDirectory(sessionRoot);

		string  safeName     = MakeSafe(symbol.Name);
		string  suffix       = string.IsNullOrWhiteSpace(fileSuffix) ? string.Empty : fileSuffix;
		string  promptPath   = Path.Combine(sessionRoot, $"{safeName}{suffix}.prompt.txt");
		string? responsePath = response != null ? Path.Combine(sessionRoot, $"{safeName}{suffix}.response.txt") : null;

		await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8);
		string triadPath = Path.Combine(sessionRoot, $"{safeName}{suffix}.triad.json");
		if (responsePath != null) {
			await File.WriteAllTextAsync(responsePath, response, Encoding.UTF8);
			// Attempt to parse triad and save JSON
			FunctionTriad triad = TriadSerializer.ParseTriadText(response, symbol, filePath, null);
			await TriadSerializer.SaveTriadAsync(triad, triadPath);
		}
		return new SessionSaveResult(promptPath, responsePath, triadPath);
	}

	public static async Task SaveFidelityAsync(CodeSymbol symbol, FidelityReport report) {
		string sessionRoot = Path.Combine(GLB.CacheDir, "sessions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
		Directory.CreateDirectory(sessionRoot);
		string safeName = MakeSafe(symbol.Name);
		string path     = Path.Combine(sessionRoot, $"{safeName}.fidelity.json");
		string json     = System.Text.Json.JsonSerializer.Serialize(report, GLB.JsonOptions);
		await File.WriteAllTextAsync(path, json, Encoding.UTF8);
	}

	static string MakeSafe(string name) {
		foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
		return name;
	}
}