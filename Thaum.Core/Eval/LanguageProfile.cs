namespace Thaum.Core.Eval;

public class LanguageProfile {
    public string  Id              { get; init; }
    public string[] BranchKeywords { get; init; } = [];
    public string  AsyncKeyword    { get; init; } = "await";

    public LanguageProfile(string id) {
        Id = id.ToLowerInvariant();
    }

    public static LanguageProfile For(string language) {
        string id = language.ToLowerInvariant();
        return id switch {
            "c-sharp"    => new LanguageProfile(id) { BranchKeywords = new[] { "if", "switch", "for", "foreach", "while", "do" }, AsyncKeyword = "await" },
            "javascript" => new LanguageProfile(id) { BranchKeywords = new[] { "if", "switch", "for", "while" }, AsyncKeyword = "await" },
            "typescript" => new LanguageProfile(id) { BranchKeywords = new[] { "if", "switch", "for", "while" }, AsyncKeyword = "await" },
            "python"     => new LanguageProfile(id) { BranchKeywords = new[] { "if", "elif", "for", "while" }, AsyncKeyword = "await" },
            _             => new LanguageProfile(id)
        };
    }
}

