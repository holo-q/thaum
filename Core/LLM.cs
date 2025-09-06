using static System.Console;

namespace Thaum.Core.Services;

public abstract class LLM {
	public abstract Task<string>                   CompleteAsync(string           prompt,       LLMOptions? options                         = null);
	public abstract Task<string>                   CompleteWithSystemAsync(string systemPrompt, string      userPrompt, LLMOptions? options = null);
	public abstract Task<IAsyncEnumerable<string>> StreamCompleteAsync(string     prompt,       LLMOptions? options = null);

	public async Task CallPrompt(string prompt, string title, System.Text.StringBuilder? captureOutput = null) {
		WriteLine($"═══ {title} OUTPUT ═══");

		try {
			// TODO extract to Glb
			IAsyncEnumerable<string> res = await this.StreamCompleteAsync(prompt, new LLMOptions(Temperature: 0.3, MaxTokens: 1024, Model: GLB.DefaultModel));

			await foreach (string token in res) {
				Write(token);
				captureOutput?.Append(token);
			}
			WriteLine();
			WriteLine();
		} catch (Exception ex) {
			WriteLine($"Error calling LLM: {ex.Message}");
		}
	}
}