using System.Collections.Concurrent;
using System.CommandLine;
using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

public partial class CLI {
    public async Task CMD_compress_batch(
        string path,
        string language,
        string? promptName,
        int concurrency,
        int? sampleN,
        CancellationToken cancellationToken
    ) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;
        string prompt = string.IsNullOrWhiteSpace(promptName) ? "compress_function_v5" : promptName!;

        println($"Batch compressing: {root} ({lang}), prompt: {prompt}, concurrency: {concurrency}{(sampleN is not null ? ", n: " + sampleN : string.Empty)}");

        var codeMap = await _crawler.CrawlDir(root);
        var symbols = codeMap.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function).ToList();
        if (sampleN is int n && n > 0 && n < symbols.Count) {
            symbols = symbols.OrderBy(_ => Random.Shared.Next()).Take(n).ToList();
        }

        if (symbols.Count == 0) {
            println("No functions/methods found to compress.");
            return;
        }

        // Model from environment; fail fast if missing
        string model = GLB.DefaultModel;

        var throttler = new SemaphoreSlim(Math.Max(1, concurrency));
        var errors = new ConcurrentBag<(string file, string symbol, string message)>();
        int completed = 0;

        async Task ProcessSymbol(CodeSymbol sym) {
            await throttler.WaitAsync(cancellationToken);
            try {
                if (cancellationToken.IsCancellationRequested) return;

                string src = await _crawler.GetCode(sym) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(src)) {
                    errors.Add((sym.FilePath, sym.Name, "Empty source slice"));
                    return;
                }

                var context = new OptimizationContext(
                    Level: sym.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
                    AvailableKeys: [],
                    PromptName: null
                );

                string builtPrompt = await Thaum.Core.PromptUtil.BuildCustomPromptAsync(prompt, sym, context, src);

                // Stream and capture output
                var sb = new StringBuilder();
                var stream = await _llm.StreamCompleteAsync(builtPrompt, GLB.CompressionOptions(model));
                await foreach (var token in stream.WithCancellation(cancellationToken)) sb.Append(token);

                // Persist artifacts (prompt + response + parsed triad)
                await ArtifactSaver.SaveSessionAsync(sym, sym.FilePath, builtPrompt, sb.ToString(), null);

                int done = Interlocked.Increment(ref completed);
                if (done % 10 == 0 || done == symbols.Count) {
                    println($"Progress: {done}/{symbols.Count}");
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex) {
                errors.Add((sym.FilePath, sym.Name, ex.Message));
            }
            finally {
                throttler.Release();
            }
        }

        var tasks = symbols.Select(ProcessSymbol).ToArray();
        await Task.WhenAll(tasks);

        if (!errors.IsEmpty) {
            println("Errors:");
            foreach (var e in errors.Take(20)) println($"  - {Path.GetRelativePath(root, e.file)}::{e.symbol} -> {e.message}");
            if (errors.Count > 20) println($"  ... and {errors.Count - 20} more");
        }

        println("Batch compression complete.");
    }
}
