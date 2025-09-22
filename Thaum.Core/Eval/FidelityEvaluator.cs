using System.Text.RegularExpressions;
using Thaum.Core.Crawling;
using Thaum.Core.Triads;

namespace Thaum.Core.Eval;

public static class FidelityEvaluator {
    static readonly Regex AwaitRx   = new Regex(@"\bawait\b", RegexOptions.Compiled);
    static readonly Regex BranchRx  = new Regex(@"\b(if|switch|for|foreach|while)\b", RegexOptions.Compiled);
    static readonly Regex CallHeur  = new Regex(@"[A-Za-z_][A-Za-z0-9_]*\s*\(", RegexOptions.Compiled);

    public static FidelityReport EvaluateFunction(CodeSymbol symbol, string sourceCode, FunctionTriad? triad, string? language = null) {
        List<string> notes = new List<string>();
        FidelityReport report = new FidelityReport {
            SymbolName    = symbol.Name,
            FilePath      = symbol.FilePath,
            HasTriad      = triad != null,
            TriadComplete = triad?.IsComplete ?? false,
        };

        // Minimal structural metrics from source window
        int awaitCount  = AwaitRx.Matches(sourceCode).Count;
        int branchCount = BranchRx.Matches(sourceCode).Count;
        int callHeur    = CallHeur.Matches(sourceCode).Count;
        int blockCount  = 0;
        int elseCount   = 0;

        // Prefer TreeSitter AST signals for supported languages (C# for now)
        if (!string.IsNullOrWhiteSpace(language)) {
            AstSignals ast = TreeSitterGates.AnalyzeFunctionSource(language!, sourceCode);
            if (ast.AwaitCount + ast.BranchCount + ast.CallCount > 0) {
                awaitCount  = ast.AwaitCount;
                branchCount = ast.BranchCount;
                callHeur    = ast.CallCount;
                notes.Add($"AST-backed counts used for {language}");
            }
            blockCount = ast.BlockCount;
            elseCount  = ast.ElseCount;
            if (language!.ToLowerInvariant() == "c-sharp") {
                MethodSignature sig = SignatureExtractor.ExtractCSharp(sourceCode);
                report.SigName       = sig.Name;
                report.SigReturnType = sig.ReturnType;
                report.SigParamCount = sig.ParamCount;
            }
        }

        report.AwaitCountSrc  = awaitCount;
        report.BranchCountSrc = branchCount;
        report.CallHeurSrc    = callHeur;
        report.BlockCountSrc  = blockCount;
        report.ElseCountSrc   = elseCount;

        if (!report.HasTriad) notes.Add("No triad available");
        if (!report.TriadComplete) notes.Add("Triad missing one or more blocks");

        // Optional signature gate (C#): name should match symbol's name when extracted
        if (!string.IsNullOrWhiteSpace(report.SigName) && !string.Equals(report.SigName, symbol.Name, StringComparison.Ordinal)) {
            notes.Add($"Signature name mismatch: extracted='{report.SigName}' symbol='{symbol.Name}'");
        }

        // Minimal gate: triad present + complete + non-trivial function (has any structure)
        report.PassedMinGate = report is { HasTriad: true, TriadComplete: true } && (awaitCount + branchCount + callHeur > 0);
        
        report.Notes = notes.ToArray();
        return report;
    }
}
