using System.CommandLine;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;

namespace Thaum.CLI;

public partial class CLI {
    public async Task CMD_eval_compression(string path, string language, string? outputCsv) {
        string root = Path.GetFullPath(path);
        string lang = language == "auto" ? LangUtil.DetectLanguageFromDirectory(root) : language;

        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
            .ToList();

        var rows = new List<string> { "file,symbol,await,branch,calls,blocks,elses,passed,notes" };

        foreach (var file in files) {
            try {
                var codeMap = await _crawler.CrawlFile(file);
                foreach (var sym in codeMap.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function)) {
                    string src = await _crawler.GetCode(sym) ?? string.Empty;
                    var report = Thaum.Core.Eval.FidelityEvaluator.EvaluateFunction(sym, src, null, lang);
                    string note = string.Join("; ", report.Notes);
                    rows.Add($"{Path.GetRelativePath(root,file)},{sym.Name},{report.AwaitCountSrc},{report.BranchCountSrc},{report.CallHeurSrc},{report.BlockCountSrc},{report.ElseCountSrc},{report.PassedMinGate},{note.Replace(',', ' ')}");
                }
            } catch (Exception ex) {
                rows.Add($"{Path.GetRelativePath(root,file)},<error>,0,0,0,0,0,false,{ex.Message.Replace(',', ' ')}");
            }
        }

        if (!string.IsNullOrWhiteSpace(outputCsv)) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputCsv)) ?? ".");
            await File.WriteAllLinesAsync(outputCsv!, rows);
            WriteLine($"Wrote report: {outputCsv}");
        } else {
            foreach (var r in rows) WriteLine(r);
        }
    }
}
