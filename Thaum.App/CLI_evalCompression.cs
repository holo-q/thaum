using System.CommandLine;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;

namespace Thaum.CLI;

public partial class CLI {
    public async Task CMD_eval_compression(string path, string language, string? outputCsv, string? outputJson = null) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;

        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
            .ToList();

        var rows = new List<string> { "file,symbol,await,branch,calls,blocks,elses,passed,notes" };
        var jsonRows = new List<Thaum.Core.Eval.BatchRow>();

        foreach (var file in files) {
            try {
                var codeMap = await _crawler.CrawlFile(file);
                foreach (var sym in codeMap.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function)) {
                    string src = await _crawler.GetCode(sym) ?? string.Empty;
                    var report = Thaum.Core.Eval.FidelityEvaluator.EvaluateFunction(sym, src, null, lang);
                    string rel = Path.GetRelativePath(root,file);
                    string note = string.Join("; ", report.Notes);
                    rows.Add($"{rel},{sym.Name},{report.AwaitCountSrc},{report.BranchCountSrc},{report.CallHeurSrc},{report.BlockCountSrc},{report.ElseCountSrc},{report.PassedMinGate},{note.Replace(',', ' ')}");
                    jsonRows.Add(new Thaum.Core.Eval.BatchRow { File = rel, Symbol = sym.Name, Await = report.AwaitCountSrc, Branch = report.BranchCountSrc, Calls = report.CallHeurSrc, Blocks = report.BlockCountSrc, Elses = report.ElseCountSrc, Passed = report.PassedMinGate, Notes = note });
                }
            } catch (Exception ex) {
                string rel = Path.GetRelativePath(root,file);
                rows.Add($"{rel},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
                jsonRows.Add(new Thaum.Core.Eval.BatchRow { File = rel, Symbol = "<error>", Await = 0, Branch = 0, Calls = 0, Blocks = 0, Elses = 0, Passed = false, Notes = ex.Message });
            }
        }

        if (!string.IsNullOrWhiteSpace(outputCsv)) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputCsv)) ?? ".");
            await File.WriteAllLinesAsync(outputCsv!, rows);
            WriteLine($"Wrote report: {outputCsv}");
        } else {
            foreach (var r in rows) WriteLine(r);
        }

        if (!string.IsNullOrWhiteSpace(outputJson)) {
            var report = Thaum.Core.Eval.BatchReport.FromRows(jsonRows, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJson)) ?? ".");
            var json = System.Text.Json.JsonSerializer.Serialize(report, GLB.JsonOptions);
            await File.WriteAllTextAsync(outputJson!, json);
            WriteLine($"Wrote JSON report: {outputJson}");
        }
    }
}
