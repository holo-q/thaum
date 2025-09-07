namespace Thaum.Core.Models;

public record CodeChange(
	string      FilePath,
	ChangeType  Type,
	CodeSymbol? Symbol = null
);

public enum ChangeType {
	Added,
	Modified,
	Deleted,
	Renamed
}