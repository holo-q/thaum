using Microsoft.Extensions.Logging;
using System.Text;

namespace Thaum.Core.Services;

public class PromptLoader : IPromptLoader {
	private readonly ILogger<PromptLoader>      _logger;
	private readonly string                     _promptsDirectory;
	private readonly Dictionary<string, string> _promptCache;

	public PromptLoader(ILogger<PromptLoader> logger, string? promptsDirectory = null) {
		_logger           = logger;
		_promptsDirectory = promptsDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "prompts");
		_promptCache      = new Dictionary<string, string>();
	}

	public async Task<string> LoadPromptAsync(string promptName) {
		if (_promptCache.TryGetValue(promptName, out string? cached)) {
			return cached;
		}

		string promptPath = Path.Combine(_promptsDirectory, $"{promptName}.txt");

		if (!File.Exists(promptPath)) {
			throw new FileNotFoundException($"Prompt file not found: {promptPath}");
		}

		try {
			string content = await File.ReadAllTextAsync(promptPath, Encoding.UTF8);
			_promptCache[promptName] = content;
			_logger.LogDebug("Loaded prompt: {PromptName} from {Path}", promptName, promptPath);
			return content;
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to load prompt: {PromptName}", promptName);
			throw;
		}
	}

	public async Task<string> FormatPromptAsync(string promptName, Dictionary<string, object> parameters) {
		string template = await LoadPromptAsync(promptName);
		string result   = template;

		foreach (KeyValuePair<string, object> kvp in parameters) {
			string placeholder = $"{{{kvp.Key}}}";
			string value       = kvp.Value?.ToString() ?? string.Empty;
			result = result.Replace(placeholder, value);
		}

		return result;
	}
}