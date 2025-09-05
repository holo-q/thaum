using Thaum.Core.Models;

namespace Thaum.CLI.Models;

internal record SummarizeOptions(string ProjectPath, string Language, CompressionLevel CompressionLevel = CompressionLevel.Optimize);