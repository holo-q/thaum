using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thaum.Core.Services;

public class HttpLLM : ILLM {
	private readonly HttpClient               _httpClient;
	private readonly IConfiguration           _configuration;
	private readonly ILogger<HttpLLM> _logger;

	public HttpLLM(HttpClient httpClient, IConfiguration configuration, ILogger<HttpLLM> logger) {
		_httpClient    = httpClient;
		_configuration = configuration;
		_logger        = logger;

		// Note: Headers are set per-provider in individual methods to avoid conflicts
	}

	public async Task<string> CompleteAsync(string prompt, LlmOptions? options = null) {
		options ??= new LlmOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await CompleteOpenAIAsync(prompt, options),
			"anthropic"  => await CompleteAnthropicAsync(prompt, options),
			"ollama"     => await CompleteOllamaAsync(prompt, options),
			"openrouter" => await CompleteOpenRouterAsync(prompt, options),
			_            => throw new NotSupportedException($"Provider {provider} not supported")
		};
	}

	public async Task<string> CompleteWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions? options = null) {
		options ??= new LlmOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await CompleteOpenAIWithSystemAsync(systemPrompt, userPrompt, options),
			"anthropic"  => await CompleteAnthropicWithSystemAsync(systemPrompt, userPrompt, options),
			"ollama"     => await CompleteOllamaWithSystemAsync(systemPrompt, userPrompt, options),
			"openrouter" => await CompleteOpenRouterWithSystemAsync(systemPrompt, userPrompt, options),
			_            => throw new NotSupportedException($"Provider {provider} not supported")
		};
	}

	public async Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, LlmOptions? options = null) {
		options ??= new LlmOptions();

		string provider = _configuration["LLM:Provider"] ?? throw new InvalidOperationException("LLM:Provider configuration is required");

		return provider.ToLowerInvariant() switch {
			"openai"     => await StreamOpenAIAsync(prompt, options),
			"anthropic"  => await StreamAnthropicAsync(prompt, options),
			"ollama"     => await StreamOllamaAsync(prompt, options),
			"openrouter" => await StreamOpenRouterAsync(prompt, options),
			_            => throw new NotSupportedException($"Provider {provider} streaming not supported")
		};
	}

	private async Task<string> CompleteOpenAIAsync(string prompt, LlmOptions options) {
		OpenAIRequest request = new OpenAIRequest {
			Model = options.Model,
			Messages = new[] {
				new OpenAIMessage { Role = "user", Content = prompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenAIRequestAsync(request);
	}

	private async Task<string> CompleteOpenAIWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options) {
		OpenAIRequest request = new OpenAIRequest {
			Model = options.Model,
			Messages = new[] {
				new OpenAIMessage { Role = "system", Content = systemPrompt },
				new OpenAIMessage { Role = "user", Content   = userPrompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenAIRequestAsync(request);
	}

	private async Task<string> SendOpenAIRequestAsync(OpenAIRequest request) {
		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenAI provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Set OpenAI-specific headers
		_httpClient.DefaultRequestHeaders.Remove("Authorization");
		string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["LLM:ApiKey"];
		if (!string.IsNullOrEmpty(apiKey)) {
			_httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
		}

		try {
			HttpResponseMessage response = await _httpClient.PostAsync($"{baseUrl}/chat/completions", content);
			response.EnsureSuccessStatusCode();

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OpenAIResponse? openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

			return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to complete OpenAI request");
			throw;
		}
	}

	private async Task<string> CompleteAnthropicAsync(string prompt, LlmOptions options) {
		AnthropicRequest request = new AnthropicRequest {
			Model = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
			Messages = new[] {
				new AnthropicMessage { Role = "user", Content = prompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Anthropic provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add Anthropic-specific headers
		_httpClient.DefaultRequestHeaders.Remove("x-api-key");
		string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? _configuration["LLM:ApiKey"];
		if (!string.IsNullOrEmpty(apiKey)) {
			_httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
			_httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
		}

		try {
			HttpResponseMessage response = await _httpClient.PostAsync($"{baseUrl}/messages", content);
			response.EnsureSuccessStatusCode();

			string             responseJson      = await response.Content.ReadAsStringAsync();
			AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

			return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to complete Anthropic request");
			throw;
		}
	}

	private async Task<string> CompleteAnthropicWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options) {
		AnthropicRequest request = new AnthropicRequest {
			Model  = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
			System = systemPrompt,
			Messages = new[] {
				new AnthropicMessage { Role = "user", Content = userPrompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Anthropic provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add Anthropic-specific headers
		_httpClient.DefaultRequestHeaders.Remove("x-api-key");
		string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? _configuration["LLM:ApiKey"];
		if (!string.IsNullOrEmpty(apiKey)) {
			_httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
			_httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
		}

		try {
			HttpResponseMessage response = await _httpClient.PostAsync($"{baseUrl}/messages", content);
			response.EnsureSuccessStatusCode();

			string             responseJson      = await response.Content.ReadAsStringAsync();
			AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

			return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to complete Anthropic request");
			throw;
		}
	}

	private async Task<string> CompleteOllamaAsync(string prompt, LlmOptions options) {
		OllamaRequest request = new OllamaRequest {
			Model  = options.Model,
			Prompt = prompt,
			Stream = false,
			Options = new OllamaOptions {
				Temperature = options.Temperature,
				NumPredict  = options.MaxTokens
			}
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Ollama provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		try {
			HttpResponseMessage response = await _httpClient.PostAsync($"{baseUrl}/api/generate", content);
			response.EnsureSuccessStatusCode();

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OllamaResponse? ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, JsonOptions.Default);

			return ollamaResponse?.Response ?? "";
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to complete Ollama request");
			throw;
		}
	}

	private async Task<string> CompleteOllamaWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options) {
		string fullPrompt = $"System: {systemPrompt}\n\nUser: {userPrompt}";
		return await CompleteOllamaAsync(fullPrompt, options);
	}

	private async Task<string> CompleteOpenRouterAsync(string prompt, LlmOptions options) {
		string? model = options.Model;
		OpenAIRequest request = new OpenAIRequest {
			Model = model,
			Messages = new[] {
				new OpenAIMessage { Role = "user", Content = prompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenRouterRequestAsync(request);
	}

	private async Task<string> CompleteOpenRouterWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options) {
		string? model = options.Model;
		OpenAIRequest request = new OpenAIRequest {
			Model = model,
			Messages = new[] {
				new OpenAIMessage { Role = "system", Content = systemPrompt },
				new OpenAIMessage { Role = "user", Content   = userPrompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stop        = options.StopSequences?.ToArray()
		};

		return await SendOpenRouterRequestAsync(request);
	}

	private async Task<string> SendOpenRouterRequestAsync(OpenAIRequest request) {
		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenRouter provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add OpenRouter-specific headers
		string? apiKey  = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? _configuration["LLM:ApiKey"];
		string appName = _configuration["LLM:AppName"] ?? "Thaum";
		string siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";

		_httpClient.DefaultRequestHeaders.Remove("Authorization");
		_httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
		_httpClient.DefaultRequestHeaders.Remove("X-Title");

		if (!string.IsNullOrEmpty(apiKey)) {
			_httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
		}
		_httpClient.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
		_httpClient.DefaultRequestHeaders.Add("X-Title", appName);

		try {
			HttpResponseMessage response = await _httpClient.PostAsync($"{baseUrl}/chat/completions", content);

			if (!response.IsSuccessStatusCode) {
				string errorContent = await response.Content.ReadAsStringAsync();
				_logger.LogError("OpenRouter API error {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
				throw new HttpRequestException($"OpenRouter API error {response.StatusCode}: {errorContent}");
			}

			string          responseJson   = await response.Content.ReadAsStringAsync();
			OpenAIResponse? openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

			return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to complete OpenRouter request");
			throw;
		}
	}

	private async Task<IAsyncEnumerable<string>> StreamOpenAIAsync(string prompt, LlmOptions options) {
		OpenAIStreamRequest request = new OpenAIStreamRequest {
			Model = options.Model,
			Messages = new[] {
				new OpenAIMessage { Role = "user", Content = prompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stream      = true
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenAI provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions") {
			Content = content
		};

		// Add OpenAI authorization header
		string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _configuration["LLM:ApiKey"];
		if (!string.IsNullOrEmpty(apiKey)) {
			httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
		}

		HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		return StreamResponseTokens(response);
	}

	private async Task<IAsyncEnumerable<string>> StreamAnthropicAsync(string prompt, LlmOptions options) {
		// Anthropic streaming - fallback to batch for now
		string result = await CompleteAnthropicAsync(prompt, options);
		return AsyncEnumerableFromSingle(result);
	}

	private async Task<IAsyncEnumerable<string>> StreamOllamaAsync(string prompt, LlmOptions options) {
		OllamaRequest request = new OllamaRequest {
			Model  = options.Model,
			Prompt = prompt,
			Stream = true,
			Options = new OllamaOptions {
				Temperature = options.Temperature,
				NumPredict  = options.MaxTokens
			}
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for Ollama provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate") {
			Content = content
		};
		HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		return StreamOllamaTokens(response);
	}

	private async Task<IAsyncEnumerable<string>> StreamOpenRouterAsync(string prompt, LlmOptions options) {
		string? model = options.Model;
		OpenAIStreamRequest request = new OpenAIStreamRequest {
			Model = model,
			Messages = new[] {
				new OpenAIMessage { Role = "user", Content = prompt }
			},
			Temperature = options.Temperature,
			MaxTokens   = options.MaxTokens,
			Stream      = true
		};

		string baseUrl = _configuration["LLM:BaseUrl"] ?? throw new InvalidOperationException("LLM:BaseUrl configuration is required for OpenRouter provider");
		string json    = JsonSerializer.Serialize(request, JsonOptions.Default);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

		// Add OpenRouter-specific headers to the request
		string? apiKey  = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? _configuration["LLM:ApiKey"];
		string appName = _configuration["LLM:AppName"] ?? "Thaum";
		string siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";

		HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions") {
			Content = content
		};

		if (!string.IsNullOrEmpty(apiKey)) {
			httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
		}
		httpRequest.Headers.Add("HTTP-Referer", siteUrl);
		httpRequest.Headers.Add("X-Title", appName);

		HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

		if (!response.IsSuccessStatusCode) {
			string errorContent = await response.Content.ReadAsStringAsync();
			_logger.LogError("OpenRouter streaming API error {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
			throw new HttpRequestException($"OpenRouter streaming API error {response.StatusCode}: {errorContent}");
		}

		return StreamResponseTokens(response);
	}

	private static async IAsyncEnumerable<string> StreamResponseTokens(HttpResponseMessage response) {
		using Stream stream = await response.Content.ReadAsStreamAsync();
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
		using Stream stream = await response.Content.ReadAsStreamAsync();
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
}

internal record OpenAIChoice {
	[JsonPropertyName("message")]
	public OpenAIMessage? Message { get; init; }
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