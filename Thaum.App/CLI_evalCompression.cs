using Spectre.Console;
using Thaum.Core.Eval;
using Thaum.Core.Models;
using Thaum.Utils;
using static System.Console;
using Thaum.Core.Triads;

namespace Thaum.CLI;

public partial class CLI {
    /// <summary>
    /// Batch evaluation harness.
    /// Default: uses saved triads (compression outputs) from cache/sessions to measure triad completeness and basic gates.
    /// Pass --no-triads to run a source-only structural baseline (useful for context/complexity stats).
    /// TODO we could add --triads-from <dir> to target a specific session.
    /// TODO we could add --seed for reproducible sampling and stratified splits.
    /// TODO we could emit a session index here to aid dataset export.
    /// </summary>
    public async Task CMD_eval_compression(string path, string language, string? outputCsv, string? outputJson = null, int? sampleN = null, bool useTriads = true, string? triadsFrom = null, int? seed = null) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;
        List<string> files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
            .ToList();

        List<string>   rows     = ["file,symbol,await,branch,calls,blocks,elses,passed,notes"];
        List<BatchRow> jsonRows = [];

        // Pre-scan to collect all function symbols across files
        List<(string file, CodeSymbol sym)> allSymbols = [];
        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask($"Scanning files ({files.Count})", maxValue: files.Count);
                foreach (string file in files) {
                    try {
                        CodeMap codeMap = await _crawler.CrawlFile(file);
                        foreach (CodeSymbol sym in codeMap.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function)) {
                            allSymbols.Add((file, sym));
                        }
                    } catch (Exception ex) {
                        string rel = Path.GetRelativePath(root,file);
                        rows.Add($"{rel},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
                        jsonRows.Add(new BatchRow { File = rel, Symbol = "<error>", Await = 0, Branch = 0, Calls = 0, Blocks = 0, Elses = 0, Passed = false, Notes = ex.Message });
                    }
                    task.Increment(1);
                }
            });

        // Random sampling if requested
        if (sampleN is int n and > 0 && n < allSymbols.Count) {
            Random rng = seed is int s ? new Random(s) : Random.Shared;
            allSymbols = allSymbols.OrderBy(_ => rng.Next()).Take(n).ToList();
        }

        // Optionally load triads from cache/sessions (default ON)
        // TODO we could filter by model/prompt or timestamp window to avoid stale artifacts
        Dictionary<(string file, string symbol), FunctionTriad> triadsMap    = new Dictionary<(string file, string symbol), FunctionTriad>();
        int           triadsLoaded = 0;
        if (useTriads) {
            string sessionsDir = string.IsNullOrWhiteSpace(triadsFrom) ? Path.Combine(GLB.CacheDir, "sessions") : Path.GetFullPath(triadsFrom);
            if (Directory.Exists(sessionsDir)) {
                List<string> triadFiles = Directory.GetFiles(sessionsDir, "*.triad.json", SearchOption.AllDirectories).ToList();
                await AnsiConsole.Progress()
                    .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                    .StartAsync(async ctx => {
                        ProgressTask task = ctx.AddTask($"Loading triads ({triadFiles.Count})", maxValue: triadFiles.Count);
                        foreach (string triadPath in triadFiles) {
                            try {
                                string         jsonText = await File.ReadAllTextAsync(triadPath);
                                FunctionTriad? triad    = System.Text.Json.JsonSerializer.Deserialize<FunctionTriad>(jsonText, GLB.JsonOptions);
                                if (triad is null) { task.Increment(1); continue; }
                                string triadFile = Path.GetFullPath(triad.FilePath ?? "");
                                if (!string.IsNullOrEmpty(triadFile) && triadFile.StartsWith(root, StringComparison.Ordinal)) {
                                    triadsMap[(triadFile, triad.SymbolName)] = triad;
                                    triadsLoaded++;
                                }
                            } catch { /* ignore bad files */ }
                            task.Increment(1);
                        }
                    });
            }
        }

        // Evaluate selected symbols
        // TODO we could parallelize this loop with bounded concurrency; ensure crawler + GetCode are safe.
        int matchedTriads = 0;
        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask($"Evaluating ({allSymbols.Count})", maxValue: allSymbols.Count);
                foreach ((string file, CodeSymbol sym) in allSymbols) {
                    try {
                        string src = await _crawler.GetCode(sym) ?? string.Empty;
                        FunctionTriad? triad = null;
                        if (useTriads && triadsMap.TryGetValue((Path.GetFullPath(file), sym.Name), out triad)) matchedTriads++;
                        FidelityReport report = FidelityEvaluator.EvaluateFunction(sym, src, triad, lang);
                        string rel = Path.GetRelativePath(root,file);
                        string note = string.Join("; ", report.Notes);
                        rows.Add($"{rel},{sym.Name},{report.AwaitCountSrc},{report.BranchCountSrc},{report.CallHeurSrc},{report.BlockCountSrc},{report.ElseCountSrc},{report.PassedMinGate},{note.Replace(',', ' ')}");
                        jsonRows.Add(new BatchRow { File = rel, Symbol = sym.Name, Await = report.AwaitCountSrc, Branch = report.BranchCountSrc, Calls = report.CallHeurSrc, Blocks = report.BlockCountSrc, Elses = report.ElseCountSrc, Passed = report.PassedMinGate, Notes = note });
                    } catch (Exception ex) {
                        string rel = Path.GetRelativePath(root,file);
                        rows.Add($"{rel},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
                        jsonRows.Add(new BatchRow { File = rel, Symbol = "<error>", Await = 0, Branch = 0, Calls = 0, Blocks = 0, Elses = 0, Passed = false, Notes = ex.Message });
                    }
                    task.Increment(1);
                }
            });

        // Build summary report
        BatchReport reportObj = BatchReport.FromRows(jsonRows, lang);

        // Default output directory under cache/evals if none provided
        // Intent: keep eval artifacts out of git and consistently organized.
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
        string json = System.Text.Json.JsonSerializer.Serialize(reportObj, GLB.JsonOptions);
        await File.WriteAllTextAsync(outputJson!, json);
        WriteLine($"Wrote JSON report: {outputJson}");

        // Console summary (fast glance)
        WriteLine($"Summary: files={reportObj.Summary.Files} functions={reportObj.Summary.Functions} passed={reportObj.Summary.Passed} passRate={(reportObj.Summary.PassRate * 100):F1}% avgAwait={reportObj.Summary.AvgAwait:F2} avgBranch={reportObj.Summary.AvgBranch:F2} avgCalls={reportObj.Summary.AvgCalls:F2}");
        if (useTriads) WriteLine($"Triads: loaded={triadsLoaded} matched={matchedTriads} of sampled={allSymbols.Count}");
    }
}
