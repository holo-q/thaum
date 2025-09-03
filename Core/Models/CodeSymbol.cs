using System.Text.Json.Serialization;

namespace Thaum.Core.Models;

public record CodeSymbol(
    string Name,
    SymbolKind Kind,
    string FilePath,
    Position StartPosition,
    Position EndPosition,
    string? Summary = null,
    string? ExtractedKey = null,
    List<CodeSymbol>? Children = null,
    List<string>? Dependencies = null,
    DateTime? LastModified = null
)
{
    [JsonIgnore]
    public bool IsSummarized => !string.IsNullOrEmpty(Summary);
    
    [JsonIgnore]
    public bool HasExtractedKey => !string.IsNullOrEmpty(ExtractedKey);
}

public enum SymbolKind
{
    Function,
    Method,
    Class,
    Interface,
    Module,
    Namespace,
    Property,
    Field,
    Variable,
    Parameter
}

public record Position(int Line, int Character);

public record SymbolHierarchy(
    string ProjectPath,
    List<CodeSymbol> RootSymbols,
    Dictionary<string, string> ExtractedKeys,
    DateTime LastUpdated
);

public record SummarizationContext(
    int Level,
    List<string> AvailableKeys,
    string? ParentContext = null,
    List<string>? SiblingContexts = null
);