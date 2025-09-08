using System.CommandLine;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Utils;
using static System.Console;
using Thaum.Core.Triads;

namespace Thaum.CLI;

public partial class CLI {
    public async Task CMD_eval_compression(string path, string language, string? outputCsv, string? outputJson = null, int? sampleN = null, bool useTriads = false) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
            .ToList();

        var rows = new List<string> { "file,symbol,await,branch,calls,blocks,elses,passed,notes" };
        var jsonRows = new List<Thaum.Core.Eval.BatchRow>();

        // Pre-scan to collect all function symbols across files
        var allSymbols = new List<(string file, Core.Models.CodeSymbol sym)>();
        foreach (var file in files) {
            try {
                var codeMap = await _crawler.CrawlFile(file);
                foreach (var sym in codeMap.Where(s => s.Kind is Core.Models.SymbolKind.Method or Core.Models.SymbolKind.Function)) {
                    allSymbols.Add((file, sym));
                }
            } catch (Exception ex) {
                string rel = Path.GetRelativePath(root,file);
                rows.Add($"{rel},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
                jsonRows.Add(new Thaum.Core.Eval.BatchRow { File = rel, Symbol = "<error>", Await = 0, Branch = 0, Calls = 0, Blocks = 0, Elses = 0, Passed = false, Notes = ex.Message });
            }
        }

        // Random sampling if requested
        if (sampleN is int n && n > 0 && n < allSymbols.Count) {
            allSymbols = allSymbols.OrderBy(_ => Random.Shared.Next()).Take(n).ToList();
        }

        // Optionally load triads from cache/sessions
        var triadsMap = new Dictionary<(string file, string symbol), FunctionTriad>();
        int triadsLoaded = 0;
        if (useTriads) {
            string sessionsDir = Path.Combine(GLB.CacheDir, "sessions");
            if (Directory.Exists(sessionsDir)) {
                foreach (var triadPath in Directory.GetFiles(sessionsDir, "*.triad.json", SearchOption.AllDirectories)) {
                    try {
                        string jsonText = await File.ReadAllTextAsync(triadPath);
                        var triad = System.Text.Json.JsonSerializer.Deserialize<FunctionTriad>(jsonText, GLB.JsonOptions);
                        if (triad is null) continue;
                        // Only include triads for files under root
                        var triadFile = Path.GetFullPath(triad.FilePath);
                        if (!triadFile.StartsWith(root, StringComparison.Ordinal)) continue;
                        triadsMap[(triadFile, triad.SymbolName)] = triad;
                        triadsLoaded++;
                    } catch {
                        // ignore bad files
                    }
                }
            }
        }

        // Evaluate selected symbols
        int matchedTriads = 0;
        foreach (var (file, sym) in allSymbols) {
            try {
                string src = await _crawler.GetCode(sym) ?? string.Empty;
                FunctionTriad? triad = null;
                if (useTriads && triadsMap.TryGetValue((Path.GetFullPath(file), sym.Name), out triad)) matchedTriads++;
                var report = Thaum.Core.Eval.FidelityEvaluator.EvaluateFunction(sym, src, triad, lang);
                string rel = Path.GetRelativePath(root,file);
                string note = string.Join("; ", report.Notes);
                rows.Add($"{rel},{sym.Name},{report.AwaitCountSrc},{report.BranchCountSrc},{report.CallHeurSrc},{report.BlockCountSrc},{report.ElseCountSrc},{report.PassedMinGate},{note.Replace(',', ' ')}");
                jsonRows.Add(new Thaum.Core.Eval.BatchRow { File = rel, Symbol = sym.Name, Await = report.AwaitCountSrc, Branch = report.BranchCountSrc, Calls = report.CallHeurSrc, Blocks = report.BlockCountSrc, Elses = report.ElseCountSrc, Passed = report.PassedMinGate, Notes = note });
            } catch (Exception ex) {
                string rel = Path.GetRelativePath(root,file);
                rows.Add($"{rel},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
                jsonRows.Add(new Thaum.Core.Eval.BatchRow { File = rel, Symbol = "<error>", Await = 0, Branch = 0, Calls = 0, Blocks = 0, Elses = 0, Passed = false, Notes = ex.Message });
            }
        }

        // Build summary report
        var reportObj = Thaum.Core.Eval.BatchReport.FromRows(jsonRows, lang);

        // Default output directory under cache/evals if none provided
        string defaultDir = Path.Combine(GLB.CacheDir, "evals", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
        if (string.IsNullOrWhiteSpace(outputCsv)) {
            Directory.CreateDirectory(defaultDir);
            outputCsv = Path.Combine(defaultDir, "report.csv");
        }
        if (string.IsNullOrWhiteSpace(outputJson)) {
            Directory.CreateDirectory(defaultDir);
            outputJson = Path.Combine(defaultDir, "report.json");
        }

        // Write CSV
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputCsv)) ?? ".");
        await File.WriteAllLinesAsync(outputCsv!, rows);
        WriteLine($"Wrote report: {outputCsv}");

        // Write JSON
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJson)) ?? ".");
        var json = System.Text.Json.JsonSerializer.Serialize(reportObj, GLB.JsonOptions);
        await File.WriteAllTextAsync(outputJson!, json);
        WriteLine($"Wrote JSON report: {outputJson}");

        // Console summary
        WriteLine($"Summary: files={reportObj.Summary.Files} functions={reportObj.Summary.Functions} passed={reportObj.Summary.Passed} passRate={(reportObj.Summary.PassRate * 100):F1}% avgAwait={reportObj.Summary.AvgAwait:F2} avgBranch={reportObj.Summary.AvgBranch:F2} avgCalls={reportObj.Summary.AvgCalls:F2}");
        if (useTriads) WriteLine($"Triads: loaded={triadsLoaded} matched={matchedTriads} of sampled={allSymbols.Count}");
    }
}
