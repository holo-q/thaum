using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ratatui;

namespace Thaum.Core;

// TODO there seems to be a lot of code duplication here

[LoggingIntrinsics]
public partial class HttpLLM : LLM {
	private readonly HttpClient       _client;
	private readonly IConfiguration   _configuration;
	private readonly ILogger<HttpLLM> _logger;

	public HttpLLM(HttpClient client, IConfiguration configuration) {
		_client        = client;
		_configuration = configuration;
		_logger        = RatLog.Get<HttpLLM>();
	}

	public override async Task<string> CompleteAsync(string prompt, LLMOptions? options = null) {
		options ??= new LLMOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await CompleteOpenAI(prompt, options),
			"anthropic"  => await CompleteAnthropic(prompt, options),
			"ollama"     => await CompleteOllamaAsync(prompt, options),
			"openrouter" => await CompleteOpenRouterAsync(prompt, options),
			_            => throw new NotSupportedException($"Provider {provider} not supported")
		};
	}

	public override async Task<string> CompleteWithSystemAsync(string systemPrompt, string userPrompt, LLMOptions? options = null) {
		options ??= new LLMOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await CompleteOpenAIWithSystem(systemPrompt, userPrompt, options),
			"anthropic"  => await CompleteAnthropicWithSystemAsync(systemPrompt, userPrompt, options),
			"ollama"     => await CompleteOllamaWithSystemAsync(systemPrompt, userPrompt, options),
			"openrouter" => await CompleteOpenRouterWithSystemAsync(systemPrompt, userPrompt, options),
			_            => throw new NotSupportedException($"Provider {provider} not supported")
		};
	}

	public override async Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, LLMOptions? options = null) {
		options ??= new LLMOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await StreamOpenAIAsync(prompt, options),
			"anthropic"  => await StreamAnthropicAsync(prompt, options),
			"ollama"     => await StreamOllamaAsync(prompt, options),
			"openrouter" => await StreamOpenRouterAsync(prompt, options),
			_            => throw new NotSupportedException($"Provider {provider} streaming not supported")
		};
	}

	private async Task<string> CompleteOpenAI(string prompt, LLMOptions options) {
		OpenAIRequest request = new OpenAIRequest {
			Model = options.Model,
			Messages = [
				new OpenAIMessage { Role = "user", Content = prompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenAIRequest(request);
	}

	private async Task<string> CompleteOpenAIWithSystem(string systemPrompt, string userPrompt, LLMOptions options) {
		OpenAIRequest request = new OpenAIRequest {
			Model = options.Model,
			Messages = [
				new OpenAIMessage { Role = "system", Content = systemPrompt },
				new OpenAIMessage { Role = "user", Content   = userPrompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenAIRequest(request);
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
	private async Task<string> SendOpenAIRequest(OpenAIRequest request) {
		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenAI provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Set OpenAI-specific headers
		_client.DefaultRequestHeaders.Remove("Authorization");
		string? apiKey = GLB.API_KEY_OPENAI;
		if (!string.IsNullOrEmpty(apiKey)) {
			_client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
		}

		try {
			HttpResponseMessage response = await _client.PostAsync($"{baseUrl}/chat/completions", content);
			response.EnsureSuccessStatusCode();

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OpenAIResponse? openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

			return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
		} catch (Exception ex) {
			err(ex, "Failed to complete OpenAI request");
			throw;
		}
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
	private async Task<string> CompleteAnthropic(string prompt, LLMOptions options) {
		AnthropicRequest request = new AnthropicRequest {
			Model = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
			Messages = [
				new AnthropicMessage { Role = "user", Content = prompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Anthropic provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add Anthropic-specific headers
		_client.DefaultRequestHeaders.Remove("x-api-key");
		string? apiKey = GLB.API_KEY_ANTHROPIC;
		if (!string.IsNullOrEmpty(apiKey)) {
			_client.DefaultRequestHeaders.Add("x-api-key", apiKey);
			_client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
		}

		try {
			HttpResponseMessage response = await _client.PostAsync($"{baseUrl}/messages", content);
			response.EnsureSuccessStatusCode();

			string             responseJson      = await response.Content.ReadAsStringAsync();
			AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

			return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
		} catch (Exception ex) {
			err(ex, "Failed to complete Anthropic request");
			throw;
		}
	}

	private async Task<string> CompleteAnthropicWithSystemAsync(string systemPrompt, string userPrompt, LLMOptions options) {
		AnthropicRequest request = new AnthropicRequest {
			Model  = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
			System = systemPrompt,
			Messages = [
				new AnthropicMessage { Role = "user", Content = userPrompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Anthropic provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add Anthropic-specific headers
		_client.DefaultRequestHeaders.Remove("x-api-key");
		string? apiKey = GLB.API_KEY_ANTHROPIC;
		if (!string.IsNullOrEmpty(apiKey)) {
			_client.DefaultRequestHeaders.Add("x-api-key", apiKey);
			_client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
		}

		try {
			HttpResponseMessage response = await _client.PostAsync($"{baseUrl}/messages", content);
			response.EnsureSuccessStatusCode();

			string             responseJson      = await response.Content.ReadAsStringAsync();
			AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

			return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
		} catch (Exception ex) {
			err(ex, "Failed to complete Anthropic request");
			throw;
		}
	}

	private async Task<string> CompleteOllamaAsync(string prompt, LLMOptions options) {
		OllamaRequest request = new OllamaRequest {
			Model  = options.Model,
			Prompt = prompt,
			Stream = false,
			Options = new OllamaOptions {
				Temperature = options.Temperature,
				NumPredict  = options.MaxTokens
			}
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Ollama provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		try {
			HttpResponseMessage response = await _client.PostAsync($"{baseUrl}/api/generate", content);
			response.EnsureSuccessStatusCode();

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OllamaResponse? ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, JsonOptions.Default);

			return ollamaResponse?.Response ?? "";
		} catch (Exception ex) {
			err(ex, "Failed to complete Ollama request");
			throw;
		}
	}

	private async Task<string> CompleteOllamaWithSystemAsync(string systemPrompt, string userPrompt, LLMOptions options) {
		string fullPrompt = $"System: {systemPrompt}\n\nUser: {userPrompt}";
		return await CompleteOllamaAsync(fullPrompt, options);
	}

	private async Task<string> CompleteOpenRouterAsync(string prompt, LLMOptions options) {
		string? model = options.Model;
		OpenAIRequest request = new OpenAIRequest {
			Model = model,
			Messages = [
				new OpenAIMessage { Role = "user", Content = prompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenRouterRequestAsync(request);
	}

	private async Task<string> CompleteOpenRouterWithSystemAsync(string systemPrompt, string userPrompt, LLMOptions options) {
		string? model = options.Model;
		OpenAIRequest request = new OpenAIRequest {
			Model = model,
			Messages = [
				new OpenAIMessage { Role = "system", Content = systemPrompt },
				new OpenAIMessage { Role = "user", Content   = userPrompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenRouterRequestAsync(request);
	}

	private async Task<string> SendOpenRouterRequestAsync(OpenAIRequest request) {
		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenRouter provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add OpenRouter-specific headers
		string? apiKey  = GLB.API_KEY_OPENROUTER;
		string  appName = _configuration["LLM:AppName"] ?? "Thaum";
		string  siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";

		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Remove("HTTP-Referer");
		_client.DefaultRequestHeaders.Remove("X-Title");

		if (!string.IsNullOrEmpty(apiKey)) {
			_client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
		}
		_client.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
		_client.DefaultRequestHeaders.Add("X-Title", appName);

		try {
			HttpResponseMessage response = await _client.PostAsync($"{baseUrl}/chat/completions", content);

			if (!response.IsSuccessStatusCode) {
				string errorContent = await response.Content.ReadAsStringAsync();
				err("OpenRouter API error {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
				throw new HttpRequestException($"OpenRouter API error {response.StatusCode}: {errorContent}");
			}

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OpenAIResponse? openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

			// Try to emit token usage and cost estimation if available
			if (openAIResponse?.Usage is { } u) {
				(double cost, bool havePrice) = TryEstimateOpenRouterCost(request.Model, u.PromptTokens, u.CompletionTokens);
				if (havePrice) {
					info("OpenRouter usage model={Model} prompt={Prompt} completion={Completion} total={Total} estCost=${Cost:F6}", request.Model, u.PromptTokens, u.CompletionTokens, u.TotalTokens, cost);
				} else {
					info("OpenRouter usage model={Model} prompt={Prompt} completion={Completion} total={Total}", request.Model, u.PromptTokens, u.CompletionTokens, u.TotalTokens);
				}
			} else {
				// Fallback to any cost-related headers if OpenRouter provided them
				foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers) {
					if (h.Key.StartsWith("x-openrouter", StringComparison.OrdinalIgnoreCase)) {
						info("{Header}: {Value}", h.Key, string.Join(",", h.Value));
					}
				}
			}

			return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
		} catch (Exception ex) {
			err(ex, "Failed to complete OpenRouter request");
			throw;
		}
	}

	private async Task<IAsyncEnumerable<string>> StreamOpenAIAsync(string prompt, LLMOptions options) {
		OpenAIStreamRequest request = new OpenAIStreamRequest {
			Model = options.Model,
			Messages = [
				new OpenAIMessage { Role = "user", Content = prompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stream      = true
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenAI provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions") {
			Content = content
		};

		// Add OpenAI authorization header
		string? apiKey = GLB.API_KEY_OPENAI;
		if (!string.IsNullOrEmpty(apiKey)) {
			httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
		}

		HttpResponseMessage response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		return StreamResponseTokens(response);
	}

	private async Task<IAsyncEnumerable<string>> StreamAnthropicAsync(string prompt, LLMOptions options) {
		// Anthropic streaming - fallback to batch for now
		string result = await CompleteAnthropic(prompt, options);
		return AsyncEnumerableFromSingle(result);
	}

	private async Task<IAsyncEnumerable<string>> StreamOllamaAsync(string prompt, LLMOptions options) {
		OllamaRequest request = new OllamaRequest {
			Model  = options.Model,
			Prompt = prompt,
			Stream = true,
			Options = new OllamaOptions {
				Temperature = options.Temperature,
				NumPredict  = options.MaxTokens
			}
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Ollama provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate") {
			Content = content
		};
		HttpResponseMessage response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		return StreamOllamaTokens(response);
	}

	private async Task<IAsyncEnumerable<string>> StreamOpenRouterAsync(string prompt, LLMOptions options) {
		string? model = options.Model;
		OpenAIStreamRequest request = new OpenAIStreamRequest {
			Model = model,
			Messages = [
				new OpenAIMessage { Role = "user", Content = prompt }
			],
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stream      = true
		};

		string        baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenRouter provider");
		string        json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add OpenRouter-specific headers to the request
		string? apiKey  = GLB.API_KEY_OPENROUTER;
		string  appName = _configuration["LLM:AppName"] ?? "Thaum";
		string  siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions") {
			Content = content
		};

		if (!string.IsNullOrEmpty(apiKey)) {
			httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
		}
		httpRequest.Headers.Add("HTTP-Referer", siteUrl);
		httpRequest.Headers.Add("X-Title", appName);

		HttpResponseMessage response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

		if (!response.IsSuccessStatusCode) {
			string errorContent = await response.Content.ReadAsStringAsync();
			err("OpenRouter streaming API error {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
			throw new HttpRequestException($"OpenRouter streaming API error {response.StatusCode}: {errorContent}");
		}

		// Headers may contain OpenRouter-specific usage/cost info; log if present
		foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers) {
			if (h.Key.StartsWith("x-openrouter", StringComparison.OrdinalIgnoreCase)) {
				info("{Header}: {Value}", h.Key, string.Join(",", h.Value));
			}
		}

		return StreamResponseTokens(response);
	}

	/// <summary>
	/// Attempts to estimate OpenRouter request cost in USD using either configured pricing or defaults.
	/// Pricing source (in order): environment OPENROUTER_PRICING_JSON, appsettings LLM:OpenRouterPrices, otherwise unknown.
	/// Expected pricing units: USD per 1K tokens for input/output.
	/// TODO we could cache parsed pricing for the process lifetime.
	/// </summary>
	private (double cost, bool havePrice) TryEstimateOpenRouterCost(string model, int promptTokens, int completionTokens) {
		try {
			// 1) Env JSON
			string? env = Environment.GetEnvironmentVariable("OPENROUTER_PRICING_JSON");
			if (!string.IsNullOrWhiteSpace(env)) {
				Dictionary<string, Dictionary<string, double>>? doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(env);
				if (doc != null && doc.TryGetValue(model, out Dictionary<string, double>? mm)) {
					double inPerK  = mm.TryGetValue("input", out double vi) ? vi : 0;
					double outPerK = mm.TryGetValue("output", out double vo) ? vo : 0;
					double cost    = (promptTokens / 1000.0) * inPerK + (completionTokens / 1000.0) * outPerK;
					return (cost, inPerK > 0 || outPerK > 0);
				}
			}

			// 2) appsettings.json section: LLM:OpenRouterPrices:{model}:{input|output}
			string inKey      = $"LLM:OpenRouterPrices:{model}:Input";
			string outKey     = $"LLM:OpenRouterPrices:{model}:Output";
			double inPerKConf = 0, outPerKConf = 0;
			bool   haveIn     = double.TryParse(_configuration[inKey], out inPerKConf);
			bool   haveOut    = double.TryParse(_configuration[outKey], out outPerKConf);
			if (haveIn || haveOut) {
				double cost = (promptTokens / 1000.0) * inPerKConf + (completionTokens / 1000.0) * outPerKConf;
				return (cost, inPerKConf > 0 || outPerKConf > 0);
			}
		} catch {
			// ignore and fall through
		}
		return (0.0, false);
	}

	private static async IAsyncEnumerable<string> StreamResponseTokens(HttpResponseMessage response) {
		using Stream       stream = await response.Content.ReadAsStreamAsync();
		using StreamReader reader = new StreamReader(stream);

		string? line;
		while ((line = await reader.ReadLineAsync()) != null) {
			if (line.StartsWith("data: ")) {
				string data = line[6..]; // Remove "data: " prefix
				if (data == "[DONE]") break;

				OpenAIStreamChunk? chunk = null;
				try {
					chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, JsonOptions.Default);
				} catch (JsonException) {
					// Skip invalid JSON lines
					continue;
				}

				string? delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
				if (!string.IsNullOrEmpty(delta)) {
					yield return delta;
				}
			}
		}
	}

	private static async IAsyncEnumerable<string> StreamOllamaTokens(HttpResponseMessage response) {
		using Stream       stream = await response.Content.ReadAsStreamAsync();
		using StreamReader reader = new StreamReader(stream);

		string? line;
		while ((line = await reader.ReadLineAsync()) != null) {
			OllamaStreamChunk? chunk = null;
			try {
				chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOptions.Default);
			} catch (JsonException) {
				// Skip invalid JSON lines
				continue;
			}

			if (!string.IsNullOrEmpty(chunk?.Response)) {
				yield return chunk.Response;
			}
			if (chunk?.Done == true) break;
		}
	}

	private static async IAsyncEnumerable<string> AsyncEnumerableFromSingle(string value) {
		yield return value;
		await Task.CompletedTask;
	}

	// HttpClient is managed by DI container, don't dispose
}

// OpenAI API Models
internal record OpenAIRequest {
	[JsonPropertyName("model")]
	public string Model { get; init; } = "";

	[JsonPropertyName("messages")]
	public OpenAIMessage[] Messages { get; init; } = [];

	[JsonPropertyName("temperature")]
	public double Temperature { get; init; }

	[JsonPropertyName("max_tokens")]
	public int MaxTokens { get; init; }

	[JsonPropertyName("stop")]
	public string[]? Stop { get; init; }
}

internal record OpenAIStreamRequest {
	[JsonPropertyName("model")]
	public string Model { get; init; } = "";

	[JsonPropertyName("messages")]
	public OpenAIMessage[] Messages { get; init; } = [];

	[JsonPropertyName("temperature")]
	public double Temperature { get; init; }

	[JsonPropertyName("max_tokens")]
	public int MaxTokens { get; init; }

	[JsonPropertyName("stream")]
	public bool Stream { get; init; }

	[JsonPropertyName("stop")]
	public string[]? Stop { get; init; }
}

internal record OpenAIMessage {
	[JsonPropertyName("role")]
	public string Role { get; init; } = "";

	[JsonPropertyName("content")]
	public string Content { get; init; } = "";
}

internal record OpenAIResponse {
	[JsonPropertyName("choices")]
	public OpenAIChoice[]? Choices { get; init; }

	// OpenRouter/OpenAI-compatible usage block
	[JsonPropertyName("usage")]
	public OpenAIUsage? Usage { get; init; }
}

internal record OpenAIChoice {
	[JsonPropertyName("message")]
	public OpenAIMessage? Message { get; init; }
}

internal record OpenAIUsage {
	[JsonPropertyName("prompt_tokens")]
	public int PromptTokens { get; init; }

	[JsonPropertyName("completion_tokens")]
	public int CompletionTokens { get; init; }

	[JsonPropertyName("total_tokens")]
	public int TotalTokens { get; init; }
}

internal record OpenAIStreamChunk {
	[JsonPropertyName("choices")]
	public OpenAIStreamChoice[]? Choices { get; init; }
}

internal record OpenAIStreamChoice {
	[JsonPropertyName("delta")]
	public OpenAIStreamDelta? Delta { get; init; }
}

internal record OpenAIStreamDelta {
	[JsonPropertyName("content")]
	public string? Content { get; init; }
}

// Anthropic API Models
internal record AnthropicRequest {
	[JsonPropertyName("model")]
	public string Model { get; init; } = "";

	[JsonPropertyName("system")]
	public string? System { get; init; }

	[JsonPropertyName("messages")]
	public AnthropicMessage[] Messages { get; init; } = [];

	[JsonPropertyName("temperature")]
	public double Temperature { get; init; }

	[JsonPropertyName("max_tokens")]
	public int MaxTokens { get; init; }
}

internal record AnthropicMessage {
	[JsonPropertyName("role")]
	public string Role { get; init; } = "";

	[JsonPropertyName("content")]
	public string Content { get; init; } = "";
}

internal record AnthropicResponse {
	[JsonPropertyName("content")]
	public AnthropicContent[]? Content { get; init; }
}

internal record AnthropicContent {
	[JsonPropertyName("text")]
	public string? Text { get; init; }
}

// Ollama API Models
internal record OllamaRequest {
	[JsonPropertyName("model")]
	public string Model { get; init; } = "";

	[JsonPropertyName("prompt")]
	public string Prompt { get; init; } = "";

	[JsonPropertyName("stream")]
	public bool Stream { get; init; }

	[JsonPropertyName("options")]
	public OllamaOptions? Options { get; init; }
}

internal record OllamaOptions {
	[JsonPropertyName("temperature")]
	public double Temperature { get; init; }

	[JsonPropertyName("num_predict")]
	public int NumPredict { get; init; }
}

internal record OllamaResponse {
	[JsonPropertyName("response")]
	public string? Response { get; init; }
}

internal record OllamaStreamChunk {
	[JsonPropertyName("response")]
	public string? Response { get; init; }

	[JsonPropertyName("done")]
	public bool Done { get; init; }
}

internal static class JsonOptions {
	public static readonly JsonSerializerOptions Default = new() {
		PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		TypeInfoResolver       = JsonContext.Default
	};
}