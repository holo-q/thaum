using System.Text.Json.Serialization;

namespace Thaum.Core.Eval;

public class BatchRow {
    public string File { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Await { get; set; }
    public int Branch { get; set; }
    public int Calls { get; set; }
    public int Blocks { get; set; }
    public int Elses  { get; set; }
    public bool Passed { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class BatchSummary {
    public int Files { get; set; }
    public int Functions { get; set; }
    public int Passed { get; set; }
    public double PassRate { get; set; }
    public double AvgAwait { get; set; }
    public double AvgBranch { get; set; }
    public double AvgCalls { get; set; }
}

public class BatchReport {
    public string Language { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public List<BatchRow> Rows { get; set; } = new();
    public BatchSummary Summary { get; set; } = new();

    public static BatchReport FromRows(IEnumerable<BatchRow> rows, string language) {
        List<BatchRow> list = rows.ToList();
        BatchSummary summary = new BatchSummary {
            Files     = list.Select(r => r.File).Distinct().Count(),
            Functions = list.Count,
            Passed    = list.Count(r => r.Passed),
        };
        summary.PassRate = summary.Functions > 0 ? (double)summary.Passed / summary.Functions : 0;
        summary.AvgAwait  = list.Count > 0 ? list.Average(r => r.Await)  : 0;
        summary.AvgBranch = list.Count > 0 ? list.Average(r => r.Branch) : 0;
        summary.AvgCalls  = list.Count > 0 ? list.Average(r => r.Calls)  : 0;

        return new BatchReport {
            Language = language,
            Rows     = list,
            Summary  = summary,
        };
    }
}

