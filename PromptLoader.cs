using Microsoft.Extensions.Logging;
using System.Text;
using Thaum.Utils;

namespace Thaum.Core.Services;

public class PromptLoader {
	private readonly ILogger<PromptLoader>      _logger;
	private readonly string                     _promptsDirectory;
	private readonly Dictionary<string, string> _promptCache;

	public PromptLoader(string? directory = null) {
		_logger           = Logging.For<PromptLoader>();
		_promptsDirectory = directory ?? GLB.PromptsDir;
		_promptCache      = new Dictionary<string, string>();
	}

	public async Task<string> LoadPrompt(string promptName) {
		if (_promptCache.TryGetValue(promptName, out string? cached))
			return cached;

		string path = Path.Combine(_promptsDirectory, $"{promptName}.txt");

		if (!File.Exists(path)) {
			throw new FileNotFoundException($"Prompt file not found: {path}");
		}

		try {
			string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
			_promptCache[promptName] = content;
			_logger.LogDebug("Loaded prompt: {PromptName} from {Path}", promptName, path);
			return content;
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to load prompt: {PromptName}", promptName);
			throw;
		}
	}

	public async Task<string> FormatPrompt(string promptName, Dictionary<string, object> env) {
		string result = await LoadPrompt(promptName);
		return PromptUtil.FormatPrompt(env, result);
	}
}