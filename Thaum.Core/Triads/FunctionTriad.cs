using System.Text.Json.Serialization;
using Thaum.Core.Crawling;
using Thaum.Core.Models;

namespace Thaum.Core.Triads;

public class FunctionTriad {
    public string   SymbolName   { get; set; } = string.Empty;
    public string   FilePath     { get; set; } = string.Empty;
    public string?  Signature    { get; set; }
    public string?  Topology     { get; set; }
    public string?  Morphism     { get; set; }
    public string?  Policy       { get; set; }
    public string?  Manifest     { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsComplete => !string.IsNullOrWhiteSpace(Topology)
                              && !string.IsNullOrWhiteSpace(Morphism)
                              && !string.IsNullOrWhiteSpace(Policy)
                              && !string.IsNullOrWhiteSpace(Manifest);

    public static FunctionTriad FromRawBlocks(CodeSymbol symbol, string filePath, string? topology, string? morphism, string? policy, string? manifest, string? signature = null) {
        return new FunctionTriad {
            SymbolName = symbol.Name,
            FilePath   = filePath,
            Signature  = signature,
            Topology   = topology,
            Morphism   = morphism,
            Policy     = policy,
            Manifest   = manifest
        };
    }
}

