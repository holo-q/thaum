namespace Thaum.Core.Services;

public interface IPromptLoader {
	Task<string> LoadPromptAsync(string   promptName);
	Task<string> FormatPromptAsync(string promptName, Dictionary<string, object> parameters);
}