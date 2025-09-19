using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaum.Core.Crawling;
using Thaum.Core.Models;
using Thaum.Core.Parsing;

namespace Thaum.Core.Triads;

public static class TriadSerializer {
    static readonly Regex BlockRegex = new Regex(@"(?ms)^\s*(TOPOLOGY|MORPHISM|POLICY|MANIFEST)\s*:\s*(.*?)\s*(?=^\s*(TOPOLOGY|MORPHISM|POLICY|MANIFEST)\s*:|\Z)", RegexOptions.Compiled);

    public static FunctionTriad ParseTriadText(string text, CodeSymbol symbol, string filePath, string? signature = null) {
        string? topology = null, morphism = null, policy = null, manifest = null;
        // Prefer tag blocks using the reusable parser
        List<TaggedBlockParser.TaggedBlock> tags = TaggedBlockParser.ExtractAll(text, new[] { "TOPOLOGY", "MORPHISM", "POLICY", "MANIFEST" });
        if (tags.Count > 0) {
            foreach (TaggedBlockParser.TaggedBlock t in tags) {
                string block = (t.Content ?? string.Empty).Trim();
                switch (t.Name) {
                    case "TOPOLOGY": topology = block; break;
                    case "MORPHISM": morphism = block; break;
                    case "POLICY":   policy   = block; break;
                    case "MANIFEST": manifest = block; break;
                }
            }
        } else {
            // Fallback to label blocks (TOPOLOGY: ...)
            foreach (Match m in BlockRegex.Matches(text)) {
                string name  = m.Groups[1].Value.ToUpperInvariant();
                string block = m.Groups[2].Value.Trim();
                switch (name) {
                    case "TOPOLOGY": topology = block; break;
                    case "MORPHISM": morphism = block; break;
                    case "POLICY":   policy   = block; break;
                    case "MANIFEST": manifest = block; break;
                }
            }
        }
        return FunctionTriad.FromRawBlocks(symbol, filePath, topology, morphism, policy, manifest, signature);
    }

    public static async Task SaveTriadAsync(FunctionTriad triad, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(triad, GLB.JsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }
}
