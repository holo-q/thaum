using System.Text;

namespace Thaum.Core.Utils;

public class TraceColumnFormatter
{
    private readonly int _terminalWidth;
    private readonly int _fixedOperatorWidth;
    private readonly string _operator;
    
    public TraceColumnFormatter(int terminalWidth = 80, string operatorSymbol = "->")
    {
        _terminalWidth = terminalWidth;
        _operator = operatorSymbol;
        _fixedOperatorWidth = _operator.Length + 2; // operator plus surrounding spaces
    }

    public string FormatTraceLine(string source, string target, string status = "")
    {
        var availableWidth = _terminalWidth - _fixedOperatorWidth;
        
        // If status is provided, reserve space for it (with brackets and spacing)
        var statusWidth = !string.IsNullOrEmpty(status) ? status.Length + 3 : 0; // "[status] "
        var contentWidth = availableWidth - statusWidth;
        
        // Calculate space distribution
        var sourceWidth = Math.Min(source.Length, contentWidth / 2);
        var targetWidth = contentWidth - sourceWidth;
        
        // Truncate if necessary
        var truncatedSource = source.Length > sourceWidth 
            ? source[..(sourceWidth - 3)] + "..."
            : source;
            
        var truncatedTarget = target.Length > targetWidth 
            ? target[..(targetWidth - 3)] + "..."
            : target;
        
        // Build the formatted line
        var sb = new StringBuilder();
        
        // Add status if provided
        if (!string.IsNullOrEmpty(status))
        {
            sb.Append($"[{status}] ");
        }
        
        // Add source with padding
        sb.Append(truncatedSource.PadRight(sourceWidth));
        
        // Add operator
        sb.Append($" {_operator} ");
        
        // Add target
        sb.Append(truncatedTarget);
        
        return sb.ToString();
    }
    
    public string FormatHeaderLine(string title)
    {
        var padding = (_terminalWidth - title.Length - 4) / 2; // 4 for "== =="
        var leftPadding = "=".PadRight(Math.Max(0, padding), '=');
        var rightPadding = "=".PadLeft(Math.Max(0, padding), '=');
        
        return $"{leftPadding} {title} {rightPadding}".PadRight(_terminalWidth, '=')[.._terminalWidth];
    }
    
    public string FormatProgressLine(string operation, int current, int total, double percentage)
    {
        var progressInfo = $"({current}/{total} - {percentage:F1}%)";
        var availableWidth = _terminalWidth - progressInfo.Length - 1;
        
        var truncatedOperation = operation.Length > availableWidth 
            ? operation[..(availableWidth - 3)] + "..."
            : operation;
            
        return $"{truncatedOperation.PadRight(availableWidth)} {progressInfo}";
    }
    
    public void PrintTraceLine(string source, string target, string status = "")
    {
        Console.WriteLine(FormatTraceLine(source, target, status));
    }
    
    public void PrintHeaderLine(string title)
    {
        Console.WriteLine();
        Console.WriteLine(FormatHeaderLine(title));
        Console.WriteLine();
    }
    
    public void PrintProgressLine(string operation, int current, int total)
    {
        var percentage = (double)current / total * 100;
        Console.WriteLine(FormatProgressLine(operation, current, total, percentage));
    }
}

public static class TraceFormatter
{
    private static TraceColumnFormatter? _instance;
    
    public static TraceColumnFormatter Instance
    {
        get
        {
            if (_instance == null)
            {
                var width = GetTerminalWidth();
                _instance = new TraceColumnFormatter(width);
            }
            return _instance;
        }
    }
    
    private static int GetTerminalWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch
        {
            return 80; // Fallback width
        }
    }
    
    public static void PrintTrace(string source, string target, string status = "")
    {
        Instance.PrintTraceLine(source, target, status);
    }
    
    public static void PrintHeader(string title)
    {
        Instance.PrintHeaderLine(title);
    }
    
    public static void PrintProgress(string operation, int current, int total)
    {
        Instance.PrintProgressLine(operation, current, total);
    }
}