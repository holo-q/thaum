using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ratatui.Reload;

/// <summary>
/// External terminal bridge that mirrors frames to a second terminal window
/// without spawning a second .NET process. Uses named pipes/FIFOs for ANSI out and key input.
/// </summary>
internal sealed class TerminalBridge : IDisposable {
	private readonly string  _label;
	private readonly int     _width;
	private readonly int     _height;
	private readonly string  _repoRoot;
	private readonly string? _terminal;

	private          string?                 _dir;
	private          string?                 _pipeOut;
	private          string?                 _pipeIn;
	private          Process?                _proc;
	private          FileStream?             _outStream;
	private          FileStream?             _inStream;
	private readonly CancellationTokenSource _cts   = new();
	private readonly ConcurrentQueue<Event>  _queue = new();

	public TerminalBridge(string label, int width, int height, string repoRoot, string? terminal) {
		_label    = label;
		_width    = Math.Max(20, width);
		_height   = Math.Max(8, height);
		_repoRoot = repoRoot;
		_terminal = terminal;
	}

	public bool Start() {
		if (!IsUnix()) return false;
		try {
			_dir = Path.Combine(Path.GetTempPath(), "thaum-bridge", Guid.NewGuid().ToString("n"));
			Directory.CreateDirectory(_dir);
			_pipeOut = Path.Combine(_dir, "out.ansi");
			_pipeIn  = Path.Combine(_dir, "in.keys");

			// Create FIFOs
			if (!MkFifo(_pipeOut!) || !MkFifo(_pipeIn!))
				throw new InvalidOperationException("mkfifo failed");

			_outStream = OpenWriteFifo(_pipeOut!);
			_inStream  = OpenReadFifo(_pipeIn!);

			// Launch external terminal hooked to the FIFOs
			_proc = LaunchTerminalProcess(_terminal ?? "xterm", _repoRoot, _pipeOut!, _pipeIn!, _label, _width, _height);
			if (_proc == null || _proc.HasExited)
				throw new InvalidOperationException("Failed to start external terminal");

			// Start input pump
			_ = Task.Run(InputPumpAsync, _cts.Token);
			// Initial clear
			WriteAnsi("\x1b[2J\x1b[H");
			return true;
		} catch {
			Dispose();
			return false;
		}
	}

	public void WriteAnsi(string text) {
		if (_outStream is null) return;
		byte[] buf = Encoding.UTF8.GetBytes(text);
		try {
			_outStream.Write(buf, 0, buf.Length);
			_outStream.Flush();
		} catch { /* ignore transient broken pipe while closing */
		}
	}

	public bool TryDequeue(out Event ev) => _queue.TryDequeue(out ev);

	private async Task InputPumpAsync() {
		if (_inStream is null) return;
		byte[] buf   = new byte[256];
		var    token = _cts.Token;
		try {
			while (!token.IsCancellationRequested) {
				int n = await _inStream.ReadAsync(buf.AsMemory(0, buf.Length), token);
				if (n <= 0) {
					await Task.Delay(10, token);
					continue;
				}
				for (int i = 0; i < n; i++) {
					byte b = buf[i];
					if (b == 0x1B) // ESC sequence
					{
						int consumed = TryParseEsc(buf.AsSpan(i, n - i), out Event evEsc);
						if (consumed > 0) {
							_queue.Enqueue(evEsc);
							i += consumed - 1;
							continue;
						}
						// bare ESC
						_queue.Enqueue(MakeKey(KeyCode.ESC));
					} else if (b == (byte)'\r' || b == (byte)'\n') {
						_queue.Enqueue(MakeKey(KeyCode.ENTER));
					} else if (b == 0x7F || b == 0x08) {
						_queue.Enqueue(MakeKey(KeyCode.Backspace));
					} else if (b is >= 0x20 and <= 0x7E) {
						_queue.Enqueue(MakeChar((char)b));
					}
				}
			}
		} catch { /* swallow on shutdown */
		}
	}

	private static Event MakeKey(KeyCode code) => new Event { Kind = EventKind.Key, Key = new KeyEvent((uint)code, 0, 0) };
	private static Event MakeChar(char   ch)   => new Event { Kind = EventKind.Key, Key = new KeyEvent((uint)KeyCode.Char, ch, 0) };

	private static int TryParseEsc(ReadOnlySpan<byte> span, out Event ev) {
		// Minimal CSI parsing for arrows/home/end/pgup/pgdn/delete
		ev = default;
		if (span.Length < 2) return 0;
		if (span[0] != 0x1B) return 0;
		if (span[1] == (byte)'[') {
			if (span.Length >= 3) {
				switch (span[2]) {
					case (byte)'A':
						ev = MakeKey(KeyCode.Up);
						return 3;
					case (byte)'B':
						ev = MakeKey(KeyCode.Down);
						return 3;
					case (byte)'C':
						ev = MakeKey(KeyCode.Right);
						return 3;
					case (byte)'D':
						ev = MakeKey(KeyCode.Left);
						return 3;
					case (byte)'H':
						ev = MakeKey(KeyCode.Home);
						return 3;
					case (byte)'F':
						ev = MakeKey(KeyCode.End);
						return 3;
					case (byte)'3':
						if (span.Length >= 4 && span[3] == (byte)'~') {
							ev = MakeKey(KeyCode.Delete);
							return 4;
						}
						break;
					case (byte)'5':
						if (span.Length >= 4 && span[3] == (byte)'~') {
							ev = MakeKey(KeyCode.PAGE_UP);
							return 4;
						}
						break;
					case (byte)'6':
						if (span.Length >= 4 && span[3] == (byte)'~') {
							ev = MakeKey(KeyCode.PAGE_DOWN);
							return 4;
						}
						break;
				}
			}
		}
		return 0;
	}

	private static bool IsUnix() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

	private static bool MkFifo(string path) {
		if (!IsUnix()) return false;
		try {
			if (File.Exists(path)) File.Delete(path);
			using var p = Process.Start(new ProcessStartInfo("mkfifo", $"\"{path}\"") { UseShellExecute = false, CreateNoWindow = true });
			p!.WaitForExit(3000);
			return File.Exists(path);
		} catch { return false; }
	}

	private static FileStream OpenWriteFifo(string path)
		=> new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);

	private static FileStream OpenReadFifo(string path)
		=> new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Write, 4096, FileOptions.Asynchronous);

	private static Process LaunchTerminalProcess(string terminal, string cwd, string pipeOut, string pipeIn, string title, int w, int h) {
		string shellCmd = $"stty raw -echo; printf '\\033]0;{Escape(title)}\\007'; cat > \"{pipeIn}\" & cat < \"{pipeOut}\"";
		string args = terminal switch {
			"kitty"          => $"-e bash -lc \"{shellCmd}\"",
			"alacritty"      => $"-e bash -lc \"{shellCmd}\"",
			"wezterm"        => $"start --cwd \"{cwd}\" -- bash -lc \"{shellCmd}\"",
			"gnome-terminal" => $"-- bash -lc \"{shellCmd}\"",
			"konsole"        => $"-e bash -lc \"{shellCmd}\"",
			"xterm"          => $"-geometry {w}x{h} -e bash -lc \"{shellCmd}\"",
			_                => $"-e bash -lc \"{shellCmd}\"",
		};
		var psi = new ProcessStartInfo(terminal, args) {
			UseShellExecute  = false,
			WorkingDirectory = cwd,
			CreateNoWindow   = false,
		};
		return Process.Start(psi)!;
	}

	private static string Escape(string s) => s.Replace("\"", "'", StringComparison.Ordinal);

	public void Dispose() {
		try { _cts.Cancel(); } catch { }
		try { _inStream?.Dispose(); } catch { }
		try { _outStream?.Dispose(); } catch { }
		try {
			if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true);
			_proc?.Dispose();
		} catch { }
		try {
			if (!string.IsNullOrEmpty(_pipeOut) && File.Exists(_pipeOut)) File.Delete(_pipeOut);
		} catch { }
		try {
			if (!string.IsNullOrEmpty(_pipeIn) && File.Exists(_pipeIn)) File.Delete(_pipeIn);
		} catch { }
		try {
			if (!string.IsNullOrEmpty(_dir) && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
		} catch { }
	}
}