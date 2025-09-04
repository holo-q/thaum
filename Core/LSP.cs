using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

/// <summary>
/// LSP Client Manager that communicates with language servers via JSON-RPC over stdio
/// </summary>
public class LSP : ILanguageServer {
	private readonly ILogger<LSP>       _logger;
	private readonly LSPManager                      _manager;
	private readonly Dictionary<string, LspServerInstance> _servers = new();

	public LSP(ILogger<LSP> logger) {
		_logger = logger;
		_manager = new LSPManager(
			LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug)).CreateLogger<LSPManager>(),
			progressReporter: new ConsoleDownloadProgress()
		);
	}

	public async Task<bool> StartLanguageServerAsync(string language, string workspacePath) {
		if (_servers.ContainsKey(language)) {
			_logger.LogWarning("Language server for {Language} is already running", language);
			return true;
		}

		try {
			// Get or download the LSP server executable
			_logger.LogInformation("Ensuring {Language} language server is available...", language);
			string? executablePath = await _manager.GetLspServerPathAsync(language);

			if (string.IsNullOrEmpty(executablePath)) {
				_logger.LogError("Failed to obtain LSP server for language: {Language}", language);
				return false;
			}

			LspServerConfiguration? serverConfig = GetServerConfiguration(language, executablePath);
			if (serverConfig == null) {
				_logger.LogWarning("No LSP server configuration found for language: {Language}", language);
				return false;
			}

			LspServerInstance instance = new LspServerInstance(language, workspacePath, serverConfig, _logger);
			bool success  = await instance.StartAsync();

			if (success) {
				_servers[language] = instance;
				_logger.LogInformation("Successfully started {Language} language server for {WorkspacePath}",
					language, workspacePath);
			}

			return success;
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to start {Language} language server", language);
			return false;
		}
	}

	public async Task<bool> StopLanguageServerAsync(string language) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance)) {
			return true;
		}

		try {
			await instance.StopAsync();
			_servers.Remove(language);
			_logger.LogInformation("Stopped {Language} language server", language);
			return true;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error stopping {Language} language server", language);
			return false;
		}
	}

	public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string language, string workspacePath) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance) || !instance.IsRunning) {
			return new List<CodeSymbol>();
		}

		try {
			return await instance.GetWorkspaceSymbolsAsync();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting workspace symbols for {Language}", language);
			return new List<CodeSymbol>();
		}
	}

	public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string language, string filePath) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance) || !instance.IsRunning) {
			return new List<CodeSymbol>();
		}

		try {
			return await instance.GetDocumentSymbolsAsync(filePath);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting document symbols for {FilePath}", filePath);
			return new List<CodeSymbol>();
		}
	}

	public async Task<string?> GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance) || !instance.IsRunning) {
			return null;
		}

		try {
			return await instance.GetDefinitionAsync(filePath, position);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting definition for {FilePath}:{Line}:{Character}",
				filePath, position.Line, position.Character);
			return null;
		}
	}

	public async Task<List<string>> GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance) || !instance.IsRunning) {
			return new List<string>();
		}

		try {
			return await instance.GetReferencesAsync(filePath, position);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting references for {FilePath}:{Line}:{Character}",
				filePath, position.Line, position.Character);
			return new List<string>();
		}
	}

	public bool IsLanguageServerRunning(string language) {
		return _servers.TryGetValue(language, out LspServerInstance? instance) && instance.IsRunning;
	}

	public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string language, string workspacePath) {
		if (!_servers.TryGetValue(language, out LspServerInstance? instance) || !instance.IsRunning) {
			yield break;
		}

		await foreach (SymbolChange change in instance.WatchWorkspaceChanges()) {
			yield return change;
		}
	}

	public void Dispose() {
		Task[] stopTasks = _servers.Values.Select(instance => instance.StopAsync()).ToArray();
		Task.WaitAll(stopTasks, TimeSpan.FromSeconds(5));
		_servers.Clear();
	}

	private static string FindOmniSharpExecutable() {
		// Try different possible executable names in order of preference
		string[] possibleNames = new[] { "omnisharp", "OmniSharp", "OmniSharp.exe" };

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
				Arguments            = new[] { "--languageserver", "--debug" },
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"python" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "pylsp",
				Arguments            = Array.Empty<string>(),
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"rust" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "rust-analyzer",
				Arguments            = Array.Empty<string>(),
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"typescript" or "javascript" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "typescript-language-server",
				Arguments            = new[] { "--stdio" },
				WorkingDirectory     = null,
				EnvironmentVariables = new Dictionary<string, string>()
			},
			"go" => new LspServerConfiguration {
				ExecutablePath       = executablePath ?? "gopls",
				Arguments            = Array.Empty<string>(),
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
internal class LspServerInstance : IDisposable {
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

	public LspServerInstance(string language, string workspacePath, LspServerConfiguration config, ILogger logger) {
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

			_logger.LogDebug("Received initialize response: {Response}", response.Length > 500 ? response[..500] + "..." : response);

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
		if (!_initialized || _writer == null) return new List<CodeSymbol>();

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
			return new List<CodeSymbol>();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting workspace symbols");
			return new List<CodeSymbol>();
		}
	}

	public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string filePath) {
		if (!_initialized || _writer == null) return new List<CodeSymbol>();

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
			return new List<CodeSymbol>();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting document symbols for {FilePath}", filePath);
			return new List<CodeSymbol>();
		}
	}

	public async Task<string?> GetDefinitionAsync(string filePath, ThaumPosition position) {
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
						line      = position.Line,
						character = position.Character
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
				filePath, position.Line, position.Character);
			return null;
		}
	}

	public async Task<List<string>> GetReferencesAsync(string filePath, ThaumPosition position) {
		if (!_initialized || _writer == null) return new List<string>();

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
						line      = position.Line,
						character = position.Character
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
			return new List<string>();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error requesting references for {FilePath}:{Line}:{Character}",
				filePath, position.Line, position.Character);
			return new List<string>();
		}
	}

	public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges() {
		// This would require implementing file system watching
		yield break;
		await Task.CompletedTask;
	}

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
			string?       line;
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
				int totalRead = 0;
				while (totalRead < length) {
					int read = await _reader.ReadAsync(buffer, totalRead, length - totalRead);
					if (read == 0) {
						_logger.LogError("Unexpected end of stream while reading response content");
						break;
					}
					totalRead += read;
				}

				string content = new string(buffer, 0, totalRead);
				_logger.LogDebug("Read response content: {Content}", content.Length > 200 ? content[..200] + "..." : content);
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