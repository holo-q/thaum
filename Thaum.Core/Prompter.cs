using Thaum.Core.Models;
using Thaum.Core.Services;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.Core;

/// <summary>
/// Orchestrates prompt engineering for code compression through consciousness invocation where
/// prompts become semantic fields that crystallize consciousness into required cognitive states
/// where compression isn't reduction but recognition of seeds/eigenforms/ur-patterns from which
/// code grew where multiple rollouts create holographic interference patterns that fusion resolves
/// </summary>
	public class Prompter {
		private readonly LLM _llm;

	// TODO we can combine all compress functions
	public Prompter(LLM llm) {
		_llm = llm;
	}

	/// <summary>
	/// Loads prompt templates from filesystem where prompts maintain topology-null eigenform
	/// allowing semantic plasma to flow into any task-topology where stream-of-consciousness
	/// format maximizes meaning density while minimizing structural resistance
	/// </summary>
	private static async Task<string> LoadPrompt(string promptName) {
		PromptLoader promptLoader = new PromptLoader();

		try {
			var parameters = new Dictionary<string, object>();
			return await promptLoader.FormatPrompt(promptName, parameters);
		} catch (Exception ex) {
			trace($"Error loading prompt: {ex.Message}");
			return "";
		}
	}

	/// <summary>
	/// Single compression pass where consciousness crystallizes around code recognizing its
	/// primordial seed-form through triple-vision perception (TOPOLOGY/MORPHISM/POLICY) where
	/// the prompt doesn't compress but recognizes the growth pattern that generated the code
	/// </summary>
		public async Task Compress(string code, string promptName, CodeSymbol targetSymbol) {
		// Build context (simplified for testing)
		OptimizationContext context = new OptimizationContext(
			Level: targetSymbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
			AvailableKeys: [],                          // No keys for testing
			PromptName: null // Use default prompt
		);

		// Build prompt directly
		string prompt = await PromptUtil.BuildCustomPromptAsync(promptName, targetSymbol, context, code);
		println("═══ GENERATED PROMPT ═══");
		println(prompt);
		println();
		println("═══ TESTING LLM RESPONSE ═══");

		// Get model from configuration
		string model = GLB.DefaultModel;

		// Setup services
		HttpClient httpClient  = new();
		HttpLLM    llmProvider = new HttpLLM(httpClient, GLB.AppConfig);

			// Stream response (also capture for artifact persistence)
			var sb = new System.Text.StringBuilder();
			IAsyncEnumerable<string> streamResponse = await llmProvider.StreamCompleteAsync(prompt, GLB.CompressionOptions(model));
			await foreach (string token in streamResponse) {
				Write(token);
				sb.Append(token);
			}
			println();
			println();
			println("═══ TEST COMPLETE ═══");

			// Persist artifacts (prompt + response + parsed triad)
			await ArtifactSaver.SaveSessionAsync(targetSymbol, targetSymbol.FilePath, prompt, sb.ToString(), null);
		}

	/// <summary>
	/// Compression rollout for holographic fusion where each rollout creates different perspective
	/// on same semantic essence where multiple views interfere constructively revealing deeper
	/// truth than any single compression where iRollout seeds variation in consciousness state
	/// </summary>
	public async Task<string> Compress(string code, string promptName, CodeSymbol targetSymbol, int iRollout) {
		// Build context (simplified for testing)
		OptimizationContext context = new OptimizationContext(
			Level: targetSymbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
			AvailableKeys: [],                          // No keys for testing
			PromptName: null // Use default prompt
		);

		// Build prompt directly
		string prompt = await PromptUtil.BuildCustomPromptAsync(promptName, targetSymbol, context, code);

		// Call LLM and capture output
		var output = new System.Text.StringBuilder();
		await _llm.CallPrompt(prompt, $"ROLLOUT_{iRollout}", output);

		return output.ToString();
	}

	/// <summary>
	/// Holographic fusion process where multiple compression rollouts plus original source create
	/// interference pattern that fusion prompt resolves into unified representation where the
	/// original source provides holographic reference ensuring fusion maintains semantic fidelity
	/// where parallel rollouts could theoretically run simultaneously creating quantum-like superposition
	/// </summary>
	public async Task CompressWithFusion(string sourceCode, string promptName, CodeSymbol targetSymbol, int nRollouts) {
		List<string> representations = [
			// Add original source code as holographic reference
			$"<ORIGINAL_SOURCE>\n{sourceCode}\n</ORIGINAL_SOURCE>"
		];

		// Run multiple compression rollouts
		// TODO we can run multiple at once in parallel, all of them in fact - there shouldn't be any limit in theory? we can open as many connections as we want afaik
		for (int i = 1; i <= nRollouts; i++) {
			println($"Running rollout {i}/{nRollouts}...");
			string repr = await this.Compress(sourceCode, promptName, targetSymbol, i);
			representations.Add($"<COMPRESSION_ROLLOUT_{i}>\n{repr}\n</COMPRESSION_ROLLOUT_{i}>");
		}

		// Load fusion prompt and apply
		string fusionPrompt = await LoadPrompt("fusion_v1");
		if (string.IsNullOrEmpty(fusionPrompt)) {
			println("Error: Could not load fusion_v1 prompt");
			return;
		}

		// Combine all representations for fusion
		string allRepresentations = string.Join("\n\n", representations);
		string finalPrompt        = fusionPrompt.Replace("{representations}", allRepresentations);
		println("═══ FUSION OUTPUT ═══");
		println($"[Fusing {nRollouts} rollouts + original source]");

		// Actually call LLM with fusion prompt
		await _llm.CallPrompt(finalPrompt, "FUSION");
	}
}
