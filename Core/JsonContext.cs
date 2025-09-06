using System.Text.Json.Serialization;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

[JsonSerializable(typeof(OpenAIRequest))]
[JsonSerializable(typeof(OpenAIStreamRequest))]
[JsonSerializable(typeof(OpenAIResponse))]
[JsonSerializable(typeof(OpenAIChoice))]
[JsonSerializable(typeof(OpenAIMessage))]
[JsonSerializable(typeof(OpenAIStreamChunk))]
[JsonSerializable(typeof(OpenAIStreamChoice))]
[JsonSerializable(typeof(OpenAIStreamDelta))]
[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicMessage))]
[JsonSerializable(typeof(AnthropicResponse))]
[JsonSerializable(typeof(AnthropicContent))]
[JsonSerializable(typeof(OllamaRequest))]
[JsonSerializable(typeof(OllamaOptions))]
[JsonSerializable(typeof(OllamaResponse))]
[JsonSerializable(typeof(OllamaStreamChunk))]
[JsonSerializable(typeof(CodeSymbol))]
[JsonSerializable(typeof(SymbolHierarchy))]
[JsonSerializable(typeof(OptimizationContext))]
[JsonSerializable(typeof(CompressionLevel))]
[JsonSerializable(typeof(LLMOptions))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
internal partial class JsonContext : JsonSerializerContext { }