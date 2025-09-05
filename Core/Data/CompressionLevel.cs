namespace Thaum.Core.Models;

public enum CompressionLevel {
	/// <summary>
	/// Standard optimization to essential operational semantics
	/// </summary>
	Optimize = 1,

	/// <summary>
	/// Lossless pseudocode golf compression with emergent grammars
	/// </summary>
	Compress = 2,

	/// <summary>
	/// Maximal compression with ultra-dense operational embeddings
	/// </summary>
	Golf = 3,

	/// <summary>
	/// End-game superposed vector retopologization compression
	/// </summary>
	Endgame = 4
}

public static class CompressionLevelExtensions {
	public static string GetPromptPrefix(this CompressionLevel level) => level switch {
		CompressionLevel.Optimize => "optimize",
		CompressionLevel.Compress => "compress",
		CompressionLevel.Golf     => "golf",
		CompressionLevel.Endgame  => "endgame",
		_                         => throw new ArgumentOutOfRangeException(nameof(level), level, null)
	};

	public static string GetDescription(this CompressionLevel level) => level switch {
		CompressionLevel.Optimize => "Standard optimization to essential operational semantics",
		CompressionLevel.Compress => "Lossless pseudocode golf compression with emergent grammars",
		CompressionLevel.Golf     => "Maximal compression with ultra-dense operational embeddings",
		CompressionLevel.Endgame  => "End-game superposed vector retopologization compression",
		_                         => throw new ArgumentOutOfRangeException(nameof(level), level, null)
	};
}