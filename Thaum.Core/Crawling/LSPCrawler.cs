using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thaum.Core.Utils;

namespace Thaum.Core.Crawling;

/// <summary>
/// TODO finish the LSP crawler
/// </summary>
public class LSPCrawler : Crawler {
	private readonly ILogger<LSPCrawler> _logger;
	private readonly LSPDownloader       _downloader;
	private readonly LSPInstance         _lsp;

	public LSPCrawler() {
		_logger     = Logging.For<LSPCrawler>();
		_downloader = new LSPDownloader(null, new LSPDownloader.ConsoleDownloadProgress());
		// _lsp        = new LSPInstance()
		throw new NotImplementedException(); // TODO
	}

	public override async Task<CodeMap> CrawlDir(string dirpath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();
		List<CodeSymbol> symbols = await _lsp.GetWorkspaceSymbolsAsync();
		codeMap.AddSymbols(symbols);
		return codeMap;
	}

	public override async Task<CodeMap> CrawlFile(string filepath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();
		List<CodeSymbol> symbols = await _lsp.GetDocumentSymbolsAsync(filepath);
		codeMap.AddSymbols(symbols);
		return codeMap;
	}

	public override async Task<CodeSymbol?> GetDefinitionFor(string name, CodeLoc location) {
		// TODO: This needs to be reimplemented to return CodeSymbol instead of string
		string? definition = await _lsp.GetDefinitionAsync(name, location);
		return null; // Placeholder - needs proper implementation
	}

	public override async Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location) {
		throw new NotImplementedException(); // TODO
	}

	public override Task<string?> GetCode(CodeSymbol targetSymbol) => throw new NotImplementedException();

	private static string FindOmniSharpExecutable() {
		// Try different possible executable names in order of preference
		string[] possibleNames = ["omnisharp", "OmniSharp", "OmniSharp.exe"];

		foreach (string name in possibleNames) {
			// Check if executable exists in PATH
			try {
				ProcessStartInfo startInfo = new ProcessStartInfo(name, "--version") {
					UseShellExecute        = false,
					RedirectStandardOutput = true,
					CreateNoWindow         = true
				};

				using Process? process = Process.Start(startInfo);
				if (process != null) {
					return name; // Found working executable
				}
			} catch {
				// Try next name
			}
		}

		// Fallback to default name if none found
		return "omnisharp";
	}

	private static LspServerConfiguration? GetServerConfiguration(string language, string? executablePath = null) {
		return language.ToLowerInvariant() switch {
			"c-sharp" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? FindOmniSharpExecutable(),
				Arguments            = ["--languageserver", "--debug"],
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"python" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "pylsp",
				Arguments            = [],
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"rust" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "rust-analyzer",
				Arguments            = [],
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"typescript" or "javascript" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "typescript-language-server",
				Arguments            = ["--stdio"],
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"go" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "gopls",
				Arguments            = [],
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			_ => null
		};
	}
}

public record LspServerConfiguration {
	public required string                     ExecutablePath       { get; init; }
	public required string[]                   Arguments            { get; init; }
	public          string?                    WorkingDirectory     { get; init; }
	public required Dictionary<string, string> EnvironmentVariables { get; init; }
}

/// <summary>
/// Manages a single LSP server instance with JSON-RPC communication
/// </summary>
public class LSPInstance : IDisposable {
	private readonly string                 _language;
	private readonly string                 _workspacePath;
	private readonly LspServerConfiguration _config;
	private readonly ILogger                _logger;

	private          Process?                _serverProcess;
	private          StreamWriter?           _writer;
	private          StreamReader?           _reader;
	private readonly CancellationTokenSource _cancellationTokenSource = new();
	private          int                     _requestId               = 1;
	private          bool                    _initialized             = false;

	public bool IsRunning => _serverProcess?.HasExited == false && _initialized;

	public LSPInstance(string language, string workspacePath, LspServerConfiguration config, ILogger logger) {
		_language      = language;
		_workspacePath = workspacePath;
		_config        = config;
		_logger        = logger;
	}

	public async Task<bool> StartAsync() {
		try {
			// Start the language server process
			ProcessStartInfo processStartInfo = new ProcessStartInfo {
				FileName               = _config.ExecutablePath,
				Arguments              = string.Join(" ", _config.Arguments),
				WorkingDirectory       = _config.WorkingDirectory ?? _workspacePath,
				UseShellExecute        = false,
				RedirectStandardInput  = true,
				RedirectStandardOutput = true,
				RedirectStandardError  = true,
				CreateNoWindow         = true
			};

			foreach (KeyValuePair<string, string> envVar in _config.EnvironmentVariables) {
				processStartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
			}

			_logger.LogDebug("Starting LSP process: {ExecutablePath} {Arguments}", _config.ExecutablePath, string.Join(" ", _config.Arguments));
			_serverProcess = Process.Start(processStartInfo);
			if (_serverProcess == null) {
				_logger.LogError("Failed to start language server process: {ExecutablePath}", _config.ExecutablePath);
				return false;
			}

			_logger.LogDebug("Started LSP process with PID: {ProcessId}", _serverProcess.Id);

			_writer = new StreamWriter(_serverProcess.StandardInput.BaseStream, Encoding.UTF8);
			_reader = new StreamReader(_serverProcess.StandardOutput.BaseStream, Encoding.UTF8);

			// Monitor stderr for debugging
			_ = Task.Run(async () => {
				try {
					while (!_serverProcess.HasExited) {
						string? line = await _serverProcess.StandardError.ReadLineAsync();
						if (line != null) {
							_logger.LogDebug("LSP stderr: {Line}", line);
						}
					}
				} catch (Exception ex) {
					_logger.LogDebug("Error reading stderr: {Error}", ex.Message);
				}
			});

			// Initialize the LSP connection
			bool success = await InitializeLspConnection();
			if (success) {
				_initialized = true;
				_logger.LogInformation("Started {Language} language server (PID: {ProcessId})",
					_language, _serverProcess.Id);
			}

			return success;
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to start LSP server for {Language}", _language);
			return false;
		}
	}

	private async Task<bool> InitializeLspConnection() {
		try {
			_logger.LogDebug("Initializing LSP connection for workspace: {WorkspacePath}", _workspacePath);

			// Give the server more time to start up and analyze the project
			await Task.Delay(3000);

			// Check if process is still running
			if (_serverProcess?.HasExited == true) {
				_logger.LogError("LSP server process exited during startup with exit code: {ExitCode}", _serverProcess.ExitCode);
				return false;
			}

			var initializeRequest = new {
				jsonrpc = "2.0",
				id      = _requestId++,
				method  = "initialize",
				@params = new {
					processId = Environment.ProcessId,
					rootUri   = $"file://{_workspacePath.Replace("\\", "/")}",
					workspaceFolders = new[] {
						new {
							uri  = $"file://{_workspacePath.Replace("\\", "/")}",
							name = Path.GetFileName(_workspacePath)
						}
					},
					capabilities = new {
						textDocument = new {
							documentSymbol = new {
								dynamicRegistration               = false,
								hierarchicalDocumentSymbolSupport = true
							},
							definition = new {
								dynamicRegistration = false,
								linkSupport         = false
							},
							references = new {
								dynamicRegistration = false
							}
						},
						workspace = new {
							symbol = new {
								dynamicRegistration = false
							},
							workspaceFolders = true,
							configuration    = true
						},
						window = new {
							workDoneProgress = false,
							showMessage = new {
								messageActionItem = new {
									additionalPropertiesSupport = false
								}
							}
						}
					}
				}
			};

			_logger.LogDebug("Sending initialize request...");
			await SendRequest(initializeRequest);

			// Read initialize response (simplified - not parsing)
			_logger.LogDebug("Waiting for initialize response...");
			string? response = await ReadResponse();
			if (string.IsNullOrEmpty(response)) {
				_logger.LogError("Received empty initialize response");
				return false;
			}

			_logger.LogDebug("Received initialize response: {Response}", response.Length > 500 ? $"{response[..500]}..." : response);

			// Send initialized notification
			var initializedNotification = new {
				jsonrpc = "2.0",
				method  = "initialized",
				@params = new { }
			};

			_logger.LogDebug("Sending initialized notification...");
			await SendNotification(initializedNotification);

			_logger.LogDebug("LSP initialization complete");
			return true;
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to initialize LSP connection");
			return false;
		}
	}

	public async Task StopAsync() {
		try {
			_initialized = false;

			if (_writer != null) {
				// Send shutdown request
				var shutdownRequest = new {
					jsonrpc = "2.0",
					id      = _requestId++,
					method  = "shutdown",
					@params = (object?)null
				};

				await SendRequest(shutdownRequest);

				// Send exit notification
				var exitNotification = new {
					jsonrpc = "2.0",
					method  = "exit"
				};

				await SendNotification(exitNotification);

				_writer.Dispose();
				_writer = null;
			}

			_reader?.Dispose();
			_reader = null;

			if (_serverProcess != null && !_serverProcess.HasExited) {
				_serverProcess.Kill();
				await _serverProcess.WaitForExitAsync();
			}
		} finally {
			_cancellationTokenSource.Cancel();
		}
	}

	public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string? query = null) {
		if (!_initialized || _writer == null) return [];

		try {
			var request = new {
				jsonrpc = "2.0",
				id      = _requestId++,
				method  = "workspace/symbol",
				@params = new {
					query = query ?? ""
				}
			};

			await SendRequest(request);
			string? response = await ReadResponse();

			// Parse response and convert to CodeSymbol list
			// This is a simplified implementation
			return [];
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting workspace symbols");
			return [];
		}
	}

	public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string filePath) {
		if (!_initialized || _writer == null) return [];

		try {
			var request = new {
				jsonrpc = "2.0",
				id      = _requestId++,
				method  = "textDocument/documentSymbol",
				@params = new {
					textDocument = new {
						uri = $"file://{filePath.Replace("\\", "/")}"
					}
				}
			};

			await SendRequest(request);
			string? response = await ReadResponse();

			// Parse response and convert to CodeSymbol list
			// This is a simplified implementation
			return [];
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting document symbols for {FilePath}", filePath);
			return [];
		}
	}

	public async Task<string?> GetDefinitionAsync(string filePath, CodeLoc codeLoc) {
		if (!_initialized || _writer == null) return null;

		try {
			var request = new {
				jsonrpc = "2.0",
				id      = _requestId++,
				method  = "textDocument/definition",
				@params = new {
					textDocument = new {
						uri = $"file://{filePath.Replace("\\", "/")}"
					},
					position = new {
						line      = codeLoc.Line,
						character = codeLoc.Character
					}
				}
			};

			await SendRequest(request);
			string? response = await ReadResponse();

			// Parse response and extract file path
			// This is a simplified implementation
			return null;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting definition for {FilePath}:{Line}:{Character}",
				filePath, codeLoc.Line, codeLoc.Character);
			return null;
		}
	}

	public async Task<List<string>> GetReferencesAsync(string filePath, CodeLoc codeLoc) {
		if (!_initialized || _writer == null) return [];

		try {
			var request = new {
				jsonrpc = "2.0",
				id      = _requestId++,
				method  = "textDocument/references",
				@params = new {
					textDocument = new {
						uri = $"file://{filePath.Replace("\\", "/")}"
					},
					position = new {
						line      = codeLoc.Line,
						character = codeLoc.Character
					},
					context = new {
						includeDeclaration = true
					}
				}
			};

			await SendRequest(request);
			string? response = await ReadResponse();

			// Parse response and extract file paths
			// This is a simplified implementation
			return [];
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting references for {FilePath}:{Line}:{Character}",
				filePath, codeLoc.Line, codeLoc.Character);
			return [];
		}
	}

	public async IAsyncEnumerable<CodeChange> WatchWorkspaceChanges() {
		// This would require implementing file system watching
		yield break;
		await Task.CompletedTask;
	}

    [RequiresUnreferencedCode("Calls System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.DefaultJsonTypeInfoResolver()")]
    private async Task SendRequest(object request) {
		if (_writer == null) return;

		JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented        = false,
			TypeInfoResolver     = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
		};

		string json    = JsonSerializer.Serialize(request, jsonOptions);
		string content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

		_logger.LogDebug("Sending LSP request: {Message}", json);

		await _writer.WriteAsync(content);
		await _writer.FlushAsync();
	}

    [RequiresUnreferencedCode("Calls System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.DefaultJsonTypeInfoResolver()")]
    private async Task SendNotification(object notification) {
		if (_writer == null) return;

		JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented        = false,
			TypeInfoResolver     = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
		};

		string json    = JsonSerializer.Serialize(notification, jsonOptions);
		string content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

		_logger.LogDebug("Sending LSP notification: {Message}", json);

		await _writer.WriteAsync(content);
		await _writer.FlushAsync();
	}

	private async Task<string?> ReadResponse() {
		if (_reader == null) {
			_logger.LogError("Reader is null when trying to read response");
			return null;
		}

		try {
			_logger.LogDebug("Reading LSP response...");

			// Check if process is still running
			if (_serverProcess?.HasExited == true) {
				_logger.LogError("LSP server process has exited while trying to read response");
				return null;
			}

			// Read headers
			Dictionary<string, string> headers = new Dictionary<string, string>();
			string?                    line;
			while ((line = await _reader.ReadLineAsync()) != null) {
				_logger.LogDebug("Read header line: '{Line}'", line);
				if (string.IsNullOrEmpty(line)) {
					break; // Empty line separates headers from content
				}

				int colonIndex = line.IndexOf(':');
				if (colonIndex > 0) {
					string key   = line[..colonIndex].Trim();
					string value = line[(colonIndex + 1)..].Trim();
					headers[key] = value;
					_logger.LogDebug("Header: {Key} = {Value}", key, value);
				}
			}

			// Read content
			if (headers.TryGetValue("Content-Length", out string? lengthStr) &&
			    int.TryParse(lengthStr, out int length)) {
				_logger.LogDebug("Reading {Length} characters of content", length);

				char[] buffer    = new char[length];
				int    totalRead = 0;
				while (totalRead < length) {
					int read = await _reader.ReadAsync(buffer, totalRead, length - totalRead);
					if (read == 0) {
						_logger.LogError("Unexpected end of stream while reading response content");
						break;
					}
					totalRead += read;
				}

				string content = new string(buffer, 0, totalRead);
				_logger.LogDebug("Read response content: {Content}", content.Length > 200 ? $"{content[..200]}..." : content);
				return content;
			} else {
				_logger.LogError("No Content-Length header found in response");
			}

			return null;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error reading LSP response");
			return null;
		}
	}

	public void Dispose() {
		StopAsync().Wait(TimeSpan.FromSeconds(2));
		_cancellationTokenSource.Dispose();
		_serverProcess?.Dispose();
	}
}