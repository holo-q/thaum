using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.Core;

public abstract class LLM {
	public abstract Task<string>                   CompleteAsync(string           prompt,       LLMOptions? options                         = null);
	public abstract Task<string>                   CompleteWithSystemAsync(string systemPrompt, string      userPrompt, LLMOptions? options = null);
	public abstract Task<IAsyncEnumerable<string>> StreamCompleteAsync(string     prompt,       LLMOptions? options = null);

	public async Task CallPrompt(string prompt, string title, System.Text.StringBuilder? captureOutput = null) {
		println($"═══ {title} OUTPUT ═══");

		try {
			// TODO extract to Glb
			IAsyncEnumerable<string> res = await this.StreamCompleteAsync(prompt, GLB.CompressionOptions());

			await foreach (string token in res) {
				Write(token);
				captureOutput?.Append(token);
			}
			println();
			println();
		} catch (Exception ex) {
			println($"Error calling LLM: {ex.Message}");
		}
	}
}