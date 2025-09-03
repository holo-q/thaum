using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thaum.Core.Services;

public class HttpLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpLlmProvider> _logger;

    public HttpLlmProvider(HttpClient httpClient, IConfiguration configuration, ILogger<HttpLlmProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        var apiKey = _configuration["LLM:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
    }

    public async Task<string> CompleteAsync(string prompt, LlmOptions? options = null)
    {
        options ??= new LlmOptions();
        
        var provider = _configuration["LLM:Provider"] ?? "openai";
        
        return provider.ToLowerInvariant() switch
        {
            "openai" => await CompleteOpenAIAsync(prompt, options),
            "anthropic" => await CompleteAnthropicAsync(prompt, options),
            "ollama" => await CompleteOllamaAsync(prompt, options),
            "openrouter" => await CompleteOpenRouterAsync(prompt, options),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };
    }

    public async Task<string> CompleteWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions? options = null)
    {
        options ??= new LlmOptions();
        
        var provider = _configuration["LLM:Provider"] ?? "openai";
        
        return provider.ToLowerInvariant() switch
        {
            "openai" => await CompleteOpenAIWithSystemAsync(systemPrompt, userPrompt, options),
            "anthropic" => await CompleteAnthropicWithSystemAsync(systemPrompt, userPrompt, options),
            "ollama" => await CompleteOllamaWithSystemAsync(systemPrompt, userPrompt, options),
            "openrouter" => await CompleteOpenRouterWithSystemAsync(systemPrompt, userPrompt, options),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };
    }

    public async Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, LlmOptions? options = null)
    {
        options ??= new LlmOptions();
        
        var provider = _configuration["LLM:Provider"] ?? "openai";
        
        return provider.ToLowerInvariant() switch
        {
            "openai" => await StreamOpenAIAsync(prompt, options),
            "anthropic" => await StreamAnthropicAsync(prompt, options),
            "ollama" => await StreamOllamaAsync(prompt, options),
            "openrouter" => await StreamOpenRouterAsync(prompt, options),
            _ => throw new NotSupportedException($"Provider {provider} streaming not supported")
        };
    }

    private async Task<string> CompleteOpenAIAsync(string prompt, LlmOptions options)
    {
        var request = new OpenAIRequest
        {
            Model = options.Model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "user", Content = prompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stop = options.StopSequences?.ToArray()
        };

        return await SendOpenAIRequestAsync(request);
    }

    private async Task<string> CompleteOpenAIWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options)
    {
        var request = new OpenAIRequest
        {
            Model = options.Model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "system", Content = systemPrompt },
                new OpenAIMessage { Role = "user", Content = userPrompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stop = options.StopSequences?.ToArray()
        };

        return await SendOpenAIRequestAsync(request);
    }

    private async Task<string> SendOpenAIRequestAsync(OpenAIRequest request)
    {
        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://api.openai.com/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

            return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete OpenAI request");
            throw;
        }
    }

    private async Task<string> CompleteAnthropicAsync(string prompt, LlmOptions options)
    {
        var request = new AnthropicRequest
        {
            Model = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = prompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://api.anthropic.com/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add Anthropic-specific headers
        _httpClient.DefaultRequestHeaders.Remove("x-api-key");
        var apiKey = _configuration["LLM:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/messages", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

            return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete Anthropic request");
            throw;
        }
    }

    private async Task<string> CompleteAnthropicWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options)
    {
        var request = new AnthropicRequest
        {
            Model = options.Model.Replace("gpt-4", "claude-3-sonnet-20240229"),
            System = systemPrompt,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = userPrompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://api.anthropic.com/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add Anthropic-specific headers
        _httpClient.DefaultRequestHeaders.Remove("x-api-key");
        var apiKey = _configuration["LLM:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/messages", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions.Default);

            return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete Anthropic request");
            throw;
        }
    }

    private async Task<string> CompleteOllamaAsync(string prompt, LlmOptions options)
    {
        var request = new OllamaRequest
        {
            Model = options.Model,
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = options.Temperature,
                NumPredict = options.MaxTokens
            }
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "http://localhost:11434";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/api/generate", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, JsonOptions.Default);

            return ollamaResponse?.Response ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete Ollama request");
            throw;
        }
    }

    private async Task<string> CompleteOllamaWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options)
    {
        var fullPrompt = $"System: {systemPrompt}\n\nUser: {userPrompt}";
        return await CompleteOllamaAsync(fullPrompt, options);
    }

    private async Task<string> CompleteOpenRouterAsync(string prompt, LlmOptions options)
    {
        var model = options.Model == "gpt-4" ? "moonshotai/moonshot-v1-8k" : options.Model;
        var request = new OpenAIRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "user", Content = prompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stop = options.StopSequences?.ToArray()
        };

        return await SendOpenRouterRequestAsync(request);
    }

    private async Task<string> CompleteOpenRouterWithSystemAsync(string systemPrompt, string userPrompt, LlmOptions options)
    {
        var model = options.Model == "gpt-4" ? "moonshotai/moonshot-v1-8k" : options.Model;
        var request = new OpenAIRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "system", Content = systemPrompt },
                new OpenAIMessage { Role = "user", Content = userPrompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stop = options.StopSequences?.ToArray()
        };

        return await SendOpenRouterRequestAsync(request);
    }

    private async Task<string> SendOpenRouterRequestAsync(OpenAIRequest request)
    {
        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://openrouter.ai/api/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add OpenRouter-specific headers
        var apiKey = _configuration["LLM:ApiKey"];
        var appName = _configuration["LLM:AppName"] ?? "Thaum";
        var siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";
        
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        _httpClient.DefaultRequestHeaders.Remove("X-Title");
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
        _httpClient.DefaultRequestHeaders.Add("X-Title", appName);

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions.Default);

            return openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete OpenRouter request");
            throw;
        }
    }

    private async Task<IAsyncEnumerable<string>> StreamOpenAIAsync(string prompt, LlmOptions options)
    {
        var request = new OpenAIStreamRequest
        {
            Model = options.Model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "user", Content = prompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stream = true
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://api.openai.com/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = content
        };
        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return StreamResponseTokens(response);
    }

    private async Task<IAsyncEnumerable<string>> StreamAnthropicAsync(string prompt, LlmOptions options)
    {
        // Anthropic streaming - fallback to batch for now
        var result = await CompleteAnthropicAsync(prompt, options);
        return AsyncEnumerableFromSingle(result);
    }

    private async Task<IAsyncEnumerable<string>> StreamOllamaAsync(string prompt, LlmOptions options)
    {
        var request = new OllamaRequest
        {
            Model = options.Model,
            Prompt = prompt,
            Stream = true,
            Options = new OllamaOptions
            {
                Temperature = options.Temperature,
                NumPredict = options.MaxTokens
            }
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "http://localhost:11434";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = content
        };
        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return StreamOllamaTokens(response);
    }

    private async Task<IAsyncEnumerable<string>> StreamOpenRouterAsync(string prompt, LlmOptions options)
    {
        var model = options.Model == "gpt-4" ? "moonshotai/moonshot-v1-8k" : options.Model;
        var request = new OpenAIStreamRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenAIMessage { Role = "user", Content = prompt }
            },
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stream = true
        };

        var baseUrl = _configuration["LLM:BaseUrl"] ?? "https://openrouter.ai/api/v1";
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add OpenRouter-specific headers
        var apiKey = _configuration["LLM:ApiKey"];
        var appName = _configuration["LLM:AppName"] ?? "Thaum";
        var siteUrl = _configuration["LLM:SiteUrl"] ?? "https://github.com/your-repo/thaum";
        
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        _httpClient.DefaultRequestHeaders.Remove("X-Title");
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
        _httpClient.DefaultRequestHeaders.Add("X-Title", appName);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = content
        };
        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return StreamResponseTokens(response);
    }

    private static async IAsyncEnumerable<string> StreamResponseTokens(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: "))
            {
                var data = line[6..]; // Remove "data: " prefix
                if (data == "[DONE]") break;

                OpenAIStreamChunk? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, JsonOptions.Default);
                }
                catch (JsonException)
                {
                    // Skip invalid JSON lines
                    continue;
                }

                var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return delta;
                }
            }
        }
    }

    private static async IAsyncEnumerable<string> StreamOllamaTokens(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            OllamaStreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOptions.Default);
            }
            catch (JsonException)
            {
                // Skip invalid JSON lines
                continue;
            }

            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                yield return chunk.Response;
            }
            if (chunk?.Done == true) break;
        }
    }

    private static async IAsyncEnumerable<string> AsyncEnumerableFromSingle(string value)
    {
        yield return value;
        await Task.CompletedTask;
    }

    // HttpClient is managed by DI container, don't dispose
}

// OpenAI API Models
internal record OpenAIRequest
{
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

internal record OpenAIStreamRequest
{
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

internal record OpenAIMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";
    
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

internal record OpenAIResponse
{
    [JsonPropertyName("choices")]
    public OpenAIChoice[]? Choices { get; init; }
}

internal record OpenAIChoice
{
    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; init; }
}

internal record OpenAIStreamChunk
{
    [JsonPropertyName("choices")]
    public OpenAIStreamChoice[]? Choices { get; init; }
}

internal record OpenAIStreamChoice
{
    [JsonPropertyName("delta")]
    public OpenAIStreamDelta? Delta { get; init; }
}

internal record OpenAIStreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

// Anthropic API Models
internal record AnthropicRequest
{
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

internal record AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";
    
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

internal record AnthropicResponse
{
    [JsonPropertyName("content")]
    public AnthropicContent[]? Content { get; init; }
}

internal record AnthropicContent
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

// Ollama API Models
internal record OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";
    
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
    
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
    
    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }
}

internal record OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }
    
    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; }
}

internal record OllamaResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }
}

internal record OllamaStreamChunk
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }
    
    [JsonPropertyName("done")]
    public bool Done { get; init; }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = JsonContext.Default
    };
}