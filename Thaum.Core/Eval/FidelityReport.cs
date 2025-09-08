using System.Text.Json.Serialization;

namespace Thaum.Core.Eval;

public class FidelityReport {
    public string   SymbolName     { get; set; } = string.Empty;
    public string   FilePath       { get; set; } = string.Empty;
    public bool     HasTriad       { get; set; }
    public bool     TriadComplete  { get; set; }

    // Simple structural signals
    public int      AwaitCountSrc  { get; set; }
    public int      BranchCountSrc { get; set; }
    public int      CallHeurSrc    { get; set; }

    // Signature (when extracted)
    public string?  SigName        { get; set; }
    public string?  SigReturnType  { get; set; }
    public int      SigParamCount  { get; set; }

    public bool     PassedMinGate  { get; set; }
    public string[] Notes          { get; set; } = Array.Empty<string>();
}
