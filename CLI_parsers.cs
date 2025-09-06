using Thaum.CLI.Models;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing command-line parsing functions where options emerge from args
/// where defaults provide reasonable fallbacks where language detection enables auto-configuration
/// where compression levels map to semantic density targets
/// </summary>
public partial class CLI {
	private LsOptions ParseLsOptions(string[] args) {
		string projectPath = Directory.GetCurrentDirectory();
		string language    = "auto";
		int    maxDepth    = 10;
		bool   showTypes   = false;
		bool   noColors    = false;

		// Check if first arg is a path/assembly specifier
		if (args.Length > 1 && !args[1].StartsWith("--")) {
			projectPath = args[1];
		}

		for (int i = 1; i < args.Length; i++) {
			switch (args[i]) {
				case "--path" when i + 1 < args.Length:
					projectPath = args[++i];
					break;
				case "--lang" when i + 1 < args.Length:
					language = args[++i];
					break;
				case "--depth" when i + 1 < args.Length:
					maxDepth = int.Parse(args[++i]);
					break;
				case "--types":
					showTypes = true;
					break;
				case "--no-colors":
					noColors = true;
					break;
				default:
					// Skip non-flag args that aren't the first positional argument
					break;
			}
		}

		// Auto-detect language if not specified (skip for assembly inspection and non-existent paths)
		if (language == "auto" &&
		    !projectPath.StartsWith("assembly:", StringComparison.OrdinalIgnoreCase) &&
		    Directory.Exists(projectPath)) {
			language = LangUtil.DetectLanguageFromDirectory(projectPath);
		}

		return new LsOptions(projectPath, language, maxDepth, showTypes, noColors);
	}

	private CompressorOptions ParseSummarizeOptions(string[] args) {
		string           projectPath      = Directory.GetCurrentDirectory();
		string           language         = "auto";
		CompressionLevel compressionLevel = CompressionLevel.Optimize;

		for (int i = 1; i < args.Length; i++) {
			switch (args[i]) {
				case "--path" when i + 1 < args.Length:
					projectPath = args[++i];
					break;
				case "--lang" when i + 1 < args.Length:
					language = args[++i];
					break;
				case "--compression" when i + 1 < args.Length:
				case "-c" when i + 1 < args.Length:
					string compressionArg = args[++i].ToLowerInvariant();
					compressionLevel = compressionArg switch {
						"optimize" or "o" => CompressionLevel.Optimize,
						"compress" or "c" => CompressionLevel.Compress,
						"golf" or "g"     => CompressionLevel.Golf,
						"endgame" or "e"  => CompressionLevel.Endgame,
						_                 => throw new ArgumentException($"Invalid compression level: {compressionArg}. Valid options: optimize, compress, golf, endgame")
					};
					break;
				case "--endgame":
					compressionLevel = CompressionLevel.Endgame;
					break;
				default:
					if (!args[i].StartsWith("--")) {
						projectPath = args[i];
					}
					break;
			}
		}

		if (language == "auto")
			language = LangUtil.DetectLanguageFromDirectory(projectPath);

		return new CompressorOptions(projectPath, language, compressionLevel);
	}
}