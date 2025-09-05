namespace Thaum.CLI.Models;

public record LsOptions(string ProjectPath, string Language, int MaxDepth, bool ShowTypes, bool NoColors = false);