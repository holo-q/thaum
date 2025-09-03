using Microsoft.Extensions.Logging;
using System.Text;

namespace Thaum.Core.Services;

public class PromptLoader : IPromptLoader
{
    private readonly ILogger<PromptLoader> _logger;
    private readonly string _promptsDirectory;
    private readonly Dictionary<string, string> _promptCache;

    public PromptLoader(ILogger<PromptLoader> logger, string? promptsDirectory = null)
    {
        _logger = logger;
        _promptsDirectory = promptsDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "prompts");
        _promptCache = new Dictionary<string, string>();
    }

    public async Task<string> LoadPromptAsync(string promptName)
    {
        if (_promptCache.TryGetValue(promptName, out var cached))
        {
            return cached;
        }

        var promptPath = Path.Combine(_promptsDirectory, $"{promptName}.txt");
        
        if (!File.Exists(promptPath))
        {
            throw new FileNotFoundException($"Prompt file not found: {promptPath}");
        }

        try
        {
            var content = await File.ReadAllTextAsync(promptPath, Encoding.UTF8);
            _promptCache[promptName] = content;
            _logger.LogDebug("Loaded prompt: {PromptName} from {Path}", promptName, promptPath);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prompt: {PromptName}", promptName);
            throw;
        }
    }

    public async Task<string> FormatPromptAsync(string promptName, Dictionary<string, object> parameters)
    {
        var template = await LoadPromptAsync(promptName);
        var result = template;

        foreach (var kvp in parameters)
        {
            var placeholder = $"{{{kvp.Key}}}";
            var value = kvp.Value?.ToString() ?? string.Empty;
            result = result.Replace(placeholder, value);
        }

        return result;
    }
}