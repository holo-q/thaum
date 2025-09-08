using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaum.Core.Models;

namespace Thaum.Core.Triads;

public static class TriadSerializer {
    static readonly Regex BlockRegex = new Regex(@"(?ms)^\s*(TOPOLOGY|MORPHISM|POLICY|MANIFEST)\s*:\s*(.*?)\s*(?=^\s*(TOPOLOGY|MORPHISM|POLICY|MANIFEST)\s*:|\Z)");
    static readonly Regex XmlBlockRegex = new Regex(@"(?is)<\s*(TOPOLOGY|MORPHISM|POLICY|MANIFEST)\s*>\s*(.*?)\s*<\s*/\s*\1\s*>", RegexOptions.Compiled);

    public static FunctionTriad ParseTriadText(string text, CodeSymbol symbol, string filePath, string? signature = null) {
        string? topology = null, morphism = null, policy = null, manifest = null;
        // Prefer XML-tagged blocks if present
        var xmlMatches = XmlBlockRegex.Matches(text);
        var matches = xmlMatches.Count > 0 ? xmlMatches : BlockRegex.Matches(text);
        foreach (Match m in matches) {
            var name  = m.Groups[1].Value.ToUpperInvariant();
            var block = m.Groups[2].Value.Trim();
            switch (name) {
                case "TOPOLOGY": topology = block; break;
                case "MORPHISM": morphism = block; break;
                case "POLICY":   policy   = block; break;
                case "MANIFEST": manifest = block; break;
            }
        }
        return FunctionTriad.FromRawBlocks(symbol, filePath, topology, morphism, policy, manifest, signature);
    }

    public static async Task SaveTriadAsync(FunctionTriad triad, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(triad, GLB.JsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }
}
