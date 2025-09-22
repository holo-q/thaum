using System.Collections.Concurrent;
using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Spectre.Console;
using Thaum.Core;
using Thaum.Core.Crawling;
using Thaum.Core.Services;
using Thaum.Core.Triads;
using Thaum.Core.Utils;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

public partial class CLI {
    // IDEA we could emit a session index (CSV/JSON) with prompt/response paths and metrics to support dataset export and reproducibility.
    // IDEA we could add --seed and deterministic ordering for reproducible sampling.
    // IDEA we could add per-provider QPS/backoff if we see throttling.
    public async Task CMD_compress_batch(
        string path,
        string language,
        string? promptName,
        int concurrency,
        int? sampleN,
        CancellationToken cancellationToken,
        int retryIncomplete = 0,
        int? seed = null
    ) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;
        string prompt = string.IsNullOrWhiteSpace(promptName) ? "compress_function_v5" : promptName!;

        println($"Batch compressing: {root} ({lang}), prompt: {prompt}, concurrency: {concurrency}{(sampleN is not null ? ", n: " + sampleN : string.Empty)}");

        // Create a single session root for this batch so artifacts are grouped together
        string sessionRoot = Path.Combine(GLB.CacheDir, "sessions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(sessionRoot);
        _logger.LogInformation("Session: {SessionRoot}", sessionRoot);

        CodeMap codeMap = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("Scanning workspace for functions", async _ => await _crawler.CrawlDir(root));
        List<CodeSymbol> symbols = codeMap.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function).ToList();
        if (sampleN is int n and > 0 && n < symbols.Count) {
            Random rng = seed is int s ? new Random(s) : Random.Shared;
            symbols = symbols.OrderBy(_ => rng.Next()).Take(n).ToList();
        }

        if (symbols.Count == 0) {
            println("No functions/methods found to compress.");
            return;
        }

        // Model from environment; fail fast if missing
        string model = GLB.DefaultModel;

        SemaphoreSlim                                               throttler  = new SemaphoreSlim(Math.Max(1, concurrency));
        ConcurrentBag<(string file, string symbol, string message)> errors     = new ConcurrentBag<(string file, string symbol, string message)>();
        ConcurrentBag<string>                                       triadPaths = new ConcurrentBag<string>();
        ConcurrentBag<object>                                       indexItems = new ConcurrentBag<object>();
        int                                                         completed  = 0;

        async Task ProcessSymbol(CodeSymbol sym) {
            await throttler.WaitAsync(cancellationToken);
            try {
                if (cancellationToken.IsCancellationRequested) return;
                Stopwatch symSw = Stopwatch.StartNew();
                string src = await _crawler.GetCode(sym) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(src)) {
                    errors.Add((sym.FilePath, sym.Name, "Empty source slice"));
                    return;
                }

                OptimizationContext context = new OptimizationContext(
                    Level: sym.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
                    AvailableKeys: [],
                    PromptName: null
                );

                string builtPrompt = await PromptUtil.BuildCustomPromptAsync(prompt, sym, context, src);

                // Stream and capture output
                StringBuilder            sb     = new StringBuilder();
                IAsyncEnumerable<string> stream = await _llm.StreamCompleteAsync(builtPrompt, GLB.CompressionOptions(model));
                await foreach (string token in stream.WithCancellation(cancellationToken)) sb.Append(token);

                // Parse triad and log a dense summary
                string text = sb.ToString();
                FunctionTriad triad = TriadSerializer.ParseTriadText(text, sym, sym.FilePath, null);
                symSw.Stop();
                int tLen = triad.Topology?.Length ?? 0;
                int mLen = triad.Morphism?.Length ?? 0;
                int pLen = triad.Policy?.Length ?? 0;
                int mfLen = triad.Manifest?.Length ?? 0;
                bool complete = triad.IsComplete;
                string rel = Path.GetRelativePath(root, sym.FilePath);
                _logger.LogInformation(
                    "file={file} sym={sym} ms={ms} topo={topo} morph={morph} policy={policy} manifest={manifest} complete={complete}",
                    rel,
                    sym.Name,
                    (int)symSw.Elapsed.TotalMilliseconds,
                    tLen,
                    mLen,
                    pLen,
                    mfLen,
                    complete);

                // Persist artifacts (prompt + response + parsed triad)
                SessionSaveResult saveRes = await ArtifactSaver.SaveSessionAsync(sym, sym.FilePath, builtPrompt, text, null, sessionRoot);
                string triadPath = saveRes.TriadPath;

                // If incomplete, log missing tags and the full raw output (explicit as requested)
                if (!complete) {
                    List<string> missing = new List<string>();
                    if (tLen == 0) missing.Add("TOPOLOGY");
                    if (mLen == 0) missing.Add("MORPHISM");
                    if (pLen == 0) missing.Add("POLICY");
                    if (mfLen == 0) missing.Add("MANIFEST");
                    _logger.LogWarning("INCOMPLETE TRIAD {file}::{sym} missing=[{missing}]\nRAW:\n{raw}", rel, sym.Name, string.Join(",", missing), text);
                }

                string provider = GLB.AppConfig["LLM:Provider"] ?? "unknown";
                indexItems.Add(new {
                    file = rel,
                    symbol = sym.Name,
                    attempt = "initial",
                    complete,
                    lengths = new { topology = tLen, morphism = mLen, policy = pLen, manifest = mfLen },
                    model,
                    provider,
                    promptName = prompt,
                    promptPath = saveRes.PromptPath,
                    responsePath = saveRes.ResponsePath,
                    triadPath = saveRes.TriadPath,
                    ms = (int)symSw.Elapsed.TotalMilliseconds
                });

                // Retry incomplete triads if requested
                if (!complete && retryIncomplete > 0) {
                    for (int attempt = 1; attempt <= retryIncomplete && !complete; attempt++) {
                        // First try a repair pass (fill ONLY missing tags)
                        (FunctionTriad merged, string rawRepair) = await TriadRepairer.RepairAsync(_llm, sym, src, triad, GLB.CompressionOptions(model));
                        SessionSaveResult repRes = await ArtifactSaver.SaveSessionAsync(sym, sym.FilePath, builtPrompt, rawRepair, null, sessionRoot, fileSuffix: $"-repair{attempt}");
                        triad = merged;
                        complete = triad.IsComplete;
                        indexItems.Add(new {
                            file = rel,
                            symbol = sym.Name,
                            attempt = $"repair{attempt}",
                            complete,
                            lengths = new { topology = triad.Topology?.Length ?? 0, morphism = triad.Morphism?.Length ?? 0, policy = triad.Policy?.Length ?? 0, manifest = triad.Manifest?.Length ?? 0 },
                            model,
                            provider,
                            promptName = prompt,
                            promptPath = repRes.PromptPath,
                            responsePath = repRes.ResponsePath,
                            triadPath = repRes.TriadPath,
                            ms = (int)symSw.Elapsed.TotalMilliseconds
                        });
                        if (complete) break;

                        // Fallback: full re-roll
                        StringBuilder            sb2     = new StringBuilder();
                        IAsyncEnumerable<string> stream2 = await _llm.StreamCompleteAsync(builtPrompt, GLB.CompressionOptions(model));
                        await foreach (string token in stream2.WithCancellation(cancellationToken)) sb2.Append(token);
                        string rerollText = sb2.ToString();
                        triad = TriadSerializer.ParseTriadText(rerollText, sym, sym.FilePath, null);
                        complete = triad.IsComplete;
                        SessionSaveResult rrRes = await ArtifactSaver.SaveSessionAsync(sym, sym.FilePath, builtPrompt, rerollText, null, sessionRoot, fileSuffix: $"-retry{attempt}");
                        indexItems.Add(new {
                            file = rel,
                            symbol = sym.Name,
                            attempt = $"retry{attempt}",
                            complete,
                            lengths = new { topology = triad.Topology?.Length ?? 0, morphism = triad.Morphism?.Length ?? 0, policy = triad.Policy?.Length ?? 0, manifest = triad.Manifest?.Length ?? 0 },
                            model,
                            provider,
                            promptName = prompt,
                            promptPath = rrRes.PromptPath,
                            responsePath = rrRes.ResponsePath,
                            triadPath = rrRes.TriadPath,
                            ms = (int)symSw.Elapsed.TotalMilliseconds
                        });
                        if (complete) break;
                    }
                }
                triadPaths.Add(triadPath);
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex) {
                errors.Add((sym.FilePath, sym.Name, ex.Message));
            }
            finally {
                throttler.Release();
            }
        }

        Stopwatch sw = Stopwatch.StartNew();
        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask($"Compressing (0/{symbols.Count})", maxValue: symbols.Count);

                async Task Wrap(CodeSymbol s) {
                    await ProcessSymbol(s);
                    int done = Interlocked.Increment(ref completed);
                    task.Value = Math.Min(done, symbols.Count);
                    // Compact live stats in description
                    double rate = done / Math.Max(0.5, sw.Elapsed.TotalSeconds);
                    TimeSpan eta = TimeSpan.FromSeconds(Math.Max(0, (symbols.Count - done) / Math.Max(0.1, rate)));
                    task.Description = $"Compressing ({done}/{symbols.Count}) @ {rate:F1}/s ETA {eta:mm\\:ss}";
                }

                Task[] tasks = symbols.Select(Wrap).ToArray();
                await Task.WhenAll(tasks);
            });

        if (!errors.IsEmpty) {
            println("Errors:");
            foreach ((string file, string symbol, string message) e in errors.Take(20)) println($"  - {Path.GetRelativePath(root, e.file)}::{e.symbol} -> {e.message}");
            if (errors.Count > 20) println($"  ... and {errors.Count - 20} more");
        }

        // Write a simple session index to aid listing
        try {
            var index = new {
                root,
                language = lang,
                model,
                provider = GLB.AppConfig["LLM:Provider"] ?? "unknown",
                promptName = prompt,
                createdUtc = DateTime.UtcNow,
                items = indexItems.ToArray()
            };
            string indexPath = Path.Combine(sessionRoot, "session_index.json");
            await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(index, GLB.JsonOptions));
            println($"Wrote session index: {indexPath}");
            // Immediately list triads for this session index
            await CMD_ls_triads_from_batch(root, indexPath, split: false);
        } catch (Exception ex) {
            println($"Failed to write session index: {ex.Message}");
        }

        println($"Batch compression complete. Saved {triadPaths.Count} triads under: {sessionRoot}");
    }
}
