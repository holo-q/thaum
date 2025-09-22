using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Utils;
using Thaum.Meta;

namespace Thaum.Core;

[LoggingIntrinsics]
public partial class PromptLoader {
	private readonly ILogger<PromptLoader>      _logger;
	private readonly string                     _promptsDirectory;
	private readonly Dictionary<string, string> _promptCache;
    private static readonly ConcurrentDictionary<string, string> _sharedCache = new();

	public PromptLoader(string? directory = null) {
		_logger           = Logging.Get<PromptLoader>();
		_promptsDirectory = directory ?? GLB.PromptsDir;
		_promptCache      = new Dictionary<string, string>();
	}

	public async Task<string> LoadPrompt(string promptName) {
		if (_promptCache.TryGetValue(promptName, out string? cached))
			return cached;

		if (_sharedCache.TryGetValue(promptName, out string? globalCached)) {
			_promptCache[promptName] = globalCached;
			return globalCached;
		}

		string path = Path.Combine(_promptsDirectory, $"{promptName}.txt");

		if (!File.Exists(path)) {
			throw new FileNotFoundException($"Prompt file not found: {path}");
		}

        try {
            string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
            _promptCache[promptName] = content;
            // Only log if we won the race to add to shared cache
            if (_sharedCache.TryAdd(promptName, content)) {
				trace("Loaded prompt: {PromptName} from {Path}", promptName, path);
            }
            return _sharedCache[promptName];
		} catch (Exception ex) {
			err(ex, "Failed to load prompt: {PromptName}", promptName);
			throw;
		}
	}

	public async Task<string> FormatPrompt(string promptName, Dictionary<string, object> env) {
		string result = await LoadPrompt(promptName);
		return PromptUtil.FormatPrompt(env, result);
	}
}
