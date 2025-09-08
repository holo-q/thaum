using System.Text.RegularExpressions;
using Thaum.Core.Models;
using Thaum.Core.Triads;

namespace Thaum.Core.Eval;

public static class FidelityEvaluator {
    static readonly Regex AwaitRx   = new Regex(@"\bawait\b", RegexOptions.Compiled);
    static readonly Regex BranchRx  = new Regex(@"\b(if|switch|for|foreach|while)\b", RegexOptions.Compiled);
    static readonly Regex CallHeur  = new Regex(@"[A-Za-z_][A-Za-z0-9_]*\s*\(", RegexOptions.Compiled);

    public static FidelityReport EvaluateFunction(CodeSymbol symbol, string sourceCode, FunctionTriad? triad, string? language = null) {
        var notes = new List<string>();
        var report = new FidelityReport {
            SymbolName    = symbol.Name,
            FilePath      = symbol.FilePath,
            HasTriad      = triad != null,
            TriadComplete = triad?.IsComplete ?? false,
        };

        // Minimal structural metrics from source window
        var awaitCount  = AwaitRx.Matches(sourceCode).Count;
        var branchCount = BranchRx.Matches(sourceCode).Count;
        var callHeur    = CallHeur.Matches(sourceCode).Count;

        // Prefer TreeSitter AST signals for supported languages (C# for now)
        if (!string.IsNullOrWhiteSpace(language)) {
            var ast = TreeSitterGates.AnalyzeFunctionSource(language!, sourceCode);
            if (ast.AwaitCount + ast.BranchCount + ast.CallCount > 0) {
                awaitCount  = ast.AwaitCount;
                branchCount = ast.BranchCount;
                callHeur    = ast.CallCount;
                notes.Add($"AST-backed counts used for {language}");
            }
        }

        report.AwaitCountSrc  = awaitCount;
        report.BranchCountSrc = branchCount;
        report.CallHeurSrc    = callHeur;

        if (!report.HasTriad) notes.Add("No triad available");
        if (!report.TriadComplete) notes.Add("Triad missing one or more blocks");

        // Minimal gate: triad present + complete + non-trivial function (has any structure)
        report.PassedMinGate = report.HasTriad && report.TriadComplete && (awaitCount + branchCount + callHeur > 0);

        report.Notes = notes.ToArray();
        return report;
    }
}
