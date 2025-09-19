using Thaum.Core.Crawling;
using Thaum.Core.Models;
using Thaum.Core.Services;

namespace Thaum.Core;

public static class StringUtil {
	private static string WrapText(string text, int maxWidth, string indent) {
		if (text.Length <= maxWidth) return text;

		string[]     words = text.Split(' ');
		string       cLine = "";
		List<string> lines = [];

		foreach (string word in words) {
			if (cLine.Length + word.Length + 1 <= maxWidth) {
				cLine += (cLine.Length > 0 ? " " : "") + word;
			} else {
				if (cLine.Length > 0) lines.Add(cLine);
				cLine = word;
			}
		}

		if (cLine.Length > 0)
			lines.Add(cLine);

		return string.Join($"\n{indent}", lines);
	}
}

public static class PromptUtil {
	public static string FormatPrompt(Dictionary<string, object> env, string result) {
		foreach (KeyValuePair<string, object> kvp in env) {
			string placeholder = $"{{{kvp.Key}}}";
			string value       = kvp.Value.ToString() ?? string.Empty;
			result = result.Replace(placeholder, value);
		}

		return result;
	}

	public static async Task<string> BuildCustomPromptAsync(string promptName, CodeSymbol symbol, OptimizationContext context, string sourceCode) {
		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = context.AvailableKeys.Any()
				? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}"))
				: "None"
		};

		return await new PromptLoader().FormatPrompt(promptName, parameters);
	}
}