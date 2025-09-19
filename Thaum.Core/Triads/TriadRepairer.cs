using System.Text;
using System.Text.Json;
using Thaum.Core.Crawling;
using Thaum.Core.Models;
using Thaum.Core.Services;

namespace Thaum.Core.Triads;

/// <summary>
/// Attempts to repair incomplete triads by asking the model to emit only the missing tags.
/// Strategy: Provide source code context, symbol info, and already-extracted blocks; request only missing
/// XML child tags. Output is parsed and merged into the triad.
/// TODO we could allow multiple passes and different repair prompts.
/// </summary>
public static class TriadRepairer {
    public static async Task<(FunctionTriad triad, string raw)> RepairAsync(
        LLM llm,
        CodeSymbol symbol,
        string sourceCode,
        FunctionTriad current,
        LLMOptions? options = null
    ) {
        List<string> missing = new List<string>();
        if (string.IsNullOrWhiteSpace(current.Topology)) missing.Add("TOPOLOGY");
        if (string.IsNullOrWhiteSpace(current.Morphism)) missing.Add("MORPHISM");
        if (string.IsNullOrWhiteSpace(current.Policy))   missing.Add("POLICY");
        if (string.IsNullOrWhiteSpace(current.Manifest)) missing.Add("MANIFEST");
        if (missing.Count == 0) return (current, "");

        string prompt = BuildRepairPrompt(symbol, sourceCode, current, missing);

        // Prefer non-stream for exact body
        string raw = await llm.CompleteAsync(prompt, options ?? GLB.CompressionOptions(GLB.DefaultModel));
        // Parse only tags we requested and merge
        FunctionTriad repaired = TriadSerializer.ParseTriadText(raw, symbol, current.FilePath, current.Signature);
        FunctionTriad merged = new FunctionTriad {
            SymbolName = current.SymbolName,
            FilePath   = current.FilePath,
            Signature  = current.Signature,
            Topology   = string.IsNullOrWhiteSpace(current.Topology) ? repaired.Topology : current.Topology,
            Morphism   = string.IsNullOrWhiteSpace(current.Morphism) ? repaired.Morphism : current.Morphism,
            Policy     = string.IsNullOrWhiteSpace(current.Policy)   ? repaired.Policy   : current.Policy,
            Manifest   = string.IsNullOrWhiteSpace(current.Manifest) ? repaired.Manifest : current.Manifest,
            TimestampUtc = DateTime.UtcNow
        };

        return (merged, raw);
    }

    static string BuildRepairPrompt(CodeSymbol symbol, string source, FunctionTriad triad, List<string> missing) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("You are completing a structured triad. Output ONLY the missing tags as XML blocks.");
        sb.AppendLine("Allowed tags: <TOPOLOGY>, <MORPHISM>, <POLICY>, <MANIFEST>.");
        sb.AppendLine("Do not re-emit tags that are already provided. Do not include commentary.");
        sb.AppendLine();
        sb.AppendLine($"<symbol>{symbol.Name}</symbol>");
        sb.AppendLine("<sourceCode>");
        sb.AppendLine(source);
        sb.AppendLine("</sourceCode>");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(triad.Topology)) { sb.AppendLine("<TOPOLOGY>"); sb.AppendLine(triad.Topology!); sb.AppendLine("</TOPOLOGY>"); }
        if (!string.IsNullOrWhiteSpace(triad.Morphism)) { sb.AppendLine("<MORPHISM>"); sb.AppendLine(triad.Morphism!); sb.AppendLine("</MORPHISM>"); }
        if (!string.IsNullOrWhiteSpace(triad.Policy))   { sb.AppendLine("<POLICY>");   sb.AppendLine(triad.Policy!);   sb.AppendLine("</POLICY>"); }
        if (!string.IsNullOrWhiteSpace(triad.Manifest)) { sb.AppendLine("<MANIFEST>"); sb.AppendLine(triad.Manifest!); sb.AppendLine("</MANIFEST>"); }
        sb.AppendLine();
        sb.AppendLine($"<missing>{string.Join(",", missing)}</missing>");
        sb.AppendLine("Now output only the missing tags as XML blocks in any order.");
        return sb.ToString();
    }
}

