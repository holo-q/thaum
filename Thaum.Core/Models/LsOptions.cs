namespace Thaum.Core.Models;

/// <summary>
/// Options for listing code symbols and files where parameters control
/// the depth and format of the symbol exploration output
/// </summary>
public record LsOptions(
	string  ProjectPath,
	string  Language,
	int     MaxDepth,
	bool    ShowTypes,
	bool    NoColors  = false,
	string? BatchJson = null,
	bool    Split     = false
);