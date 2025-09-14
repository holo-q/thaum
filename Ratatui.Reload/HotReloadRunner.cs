using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ratatui;
using Ratatui.Reload.Abstractions;

namespace Ratatui.Reload;

public sealed class HotReloadRunner : IReloadContext, IDisposable
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _services;
    private readonly string _pluginProject;
    private readonly string _configuration;
    private readonly TimeSpan _debounce;
    private readonly string _buildOutputDir;

    private volatile bool _rebuildRequested;
    private volatile bool _buildFailed;
    private string _lastBuildLog = string.Empty;

    private FileSystemWatcher? _watcher;
    private Task? _watcherTask;

    private PluginLoadContext? _currentAlc;
    private IReloadableApp? _currentApp;
    private WeakReference? _unloadRef;

    private readonly CancellationTokenSource _cts = new();

    public HotReloadRunner(ILogger logger, IServiceProvider services, string pluginProject, string configuration = "Debug", TimeSpan? debounce = null, string? buildOutputDir = null)
    {
        _logger = logger;
        _services = services;
        _pluginProject = pluginProject;
        _configuration = configuration;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _buildOutputDir = buildOutputDir ?? Path.Combine(Path.GetTempPath(), "thaum-reload", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_buildOutputDir);
    }

    public ILogger Logger => _logger;
    public IServiceProvider Services => _services;
    public CancellationToken AppCancellation => _cts.Token;
    public string ProjectPath => Path.GetFullPath(Path.GetDirectoryName(_pluginProject)!);

    public async Task<int> RunAsync(Func<(int width, int height)> sizeProvider, CancellationToken cancel)
    {
        // Initial build and load
        if (!await BuildPluginAsync(cancel))
        {
            _buildFailed = true;
        }
        else
        {
            _buildFailed = false;
            if (!TryLoadPlugin(out string err))
            {
                _buildFailed = true;
                _lastBuildLog = err + "\n" + _lastBuildLog;
            }
        }

        StartWatcher();

        using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);
        var (w, h) = sizeProvider();
        _currentApp?.OnResize(w, h);

        var sw = new Stopwatch();
        sw.Start();
        TimeSpan last = sw.Elapsed;

        while (!cancel.IsCancellationRequested && !AppCancellation.IsCancellationRequested)
        {
            // Handle rebuild requests
            if (_rebuildRequested)
            {
                _rebuildRequested = false;
                if (await BuildPluginAsync(cancel))
                {
                    _buildFailed = false;
                    ReloadSwap();
                    // reapply size
                    (w, h) = sizeProvider();
                    _currentApp?.OnResize(w, h);
                }
                else
                {
                    _buildFailed = true;
                }
            }

            // Pump a frame
            TimeSpan now = sw.Elapsed;
            TimeSpan dt = now - last;
            last = now;

            // Input
            if (term.NextEvent(TimeSpan.FromMilliseconds(50), out Event ev))
            {
                if (!_currentApp?.HandleEvent(ev) ?? true)
                {
                    if (ev.Kind == EventKind.Resize)
                    {
                        var size = sizeProvider();
                        _currentApp?.OnResize(size.Item1, size.Item2);
                    }
                }
            }

            _currentApp?.Update(dt);

            // Draw app or error overlay
            _currentApp?.Draw(term);
            if (_buildFailed)
            {
                DrawBuildOverlay(term, _lastBuildLog);
            }
        }

        return 0;
    }

    private void DrawBuildOverlay(Terminal term, string log)
    {
        var size = term.Size();
        int width = Math.Max(20, Math.Min(120, size.Width - 4));
        int height = Math.Max(6, Math.Min(30, size.Height - 4));
        var rect = new Ratatui.Rect(2, 1, width, height);
        using var para = new Ratatui.Paragraph("").Title("Build Error", border: true);
        string tail = string.Join('\n', log.Split('\n').TakeLast(height - 4));
        para.AppendSpan(tail, new Ratatui.Style(fg: Ratatui.Color.LightRed));
        term.Draw(para, rect);
    }

    private async Task<bool> BuildPluginAsync(CancellationToken cancel)
    {
        try
        {
            string args = $"build \"{_pluginProject}\" -c {_configuration} -o \"{_buildOutputDir}\"";
            var psi = new ProcessStartInfo("dotnet", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi)!;
            var sb = new StringBuilder();
            var tOut = proc.StandardOutput.ReadToEndAsync();
            var tErr = proc.StandardError.ReadToEndAsync();
            await Task.WhenAll(tOut, tErr);
            proc.WaitForExit();
            sb.AppendLine(tOut.Result);
            sb.AppendLine(tErr.Result);
            _lastBuildLog = sb.ToString();
            _logger.LogInformation("Build exited {Code}", proc.ExitCode);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _lastBuildLog = ex.ToString();
            _logger.LogError(ex, "Build failed");
            return false;
        }
    }

    private void StartWatcher()
    {
        try
        {
            string dir = Path.GetDirectoryName(_pluginProject)!;
            _watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            var queue = new ConcurrentQueue<DateTime>();
            _watcher.Changed += (_, __) => queue.Enqueue(DateTime.UtcNow);
            _watcher.Created += (_, __) => queue.Enqueue(DateTime.UtcNow);
            _watcher.Deleted += (_, __) => queue.Enqueue(DateTime.UtcNow);
            _watcher.Renamed += (_, __) => queue.Enqueue(DateTime.UtcNow);

            _watcherTask = Task.Run(async () =>
            {
                DateTime last = DateTime.MinValue;
                while (!_cts.IsCancellationRequested)
                {
                    if (queue.TryDequeue(out DateTime t))
                    {
                        last = t;
                    }
                    if (last != DateTime.MinValue && (DateTime.UtcNow - last) > _debounce)
                    {
                        _rebuildRequested = true;
                        last = DateTime.MinValue;
                    }
                    await Task.Delay(50);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watcher error");
        }
    }

    private bool TryLoadPlugin(out string error)
    {
        try
        {
            string mainDll = Directory.GetFiles(_buildOutputDir, "*.dll").First(p => Path.GetFileName(p).Equals(Path.GetFileNameWithoutExtension(_pluginProject) + ".dll", StringComparison.OrdinalIgnoreCase));
            var alc = new PluginLoadContext(mainDll);
            var asm = alc.LoadFromAssemblyPath(mainDll);
            var entry = asm.GetTypes().FirstOrDefault(t => typeof(IReloadableApp).IsAssignableFrom(t) && !t.IsAbstract);
            if (entry == null)
            {
                error = "No IReloadableApp implementation found";
                return false;
            }
            var inst = (IReloadableApp)Activator.CreateInstance(entry)!;
            inst.Init(this);
            _currentApp = inst;
            _currentAlc = alc;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return false;
        }
    }

    private void ReloadSwap()
    {
        object? state = _currentApp?.CaptureState();

        var oldApp = _currentApp;
        var oldAlc = _currentAlc;
        _currentApp = null;
        _currentAlc = null;

        if (oldApp != null)
        {
            try { oldApp.Dispose(); } catch { /* ignored */ }
        }

        if (!TryLoadPlugin(out string err))
        {
            _buildFailed = true;
            _lastBuildLog = err + "\n" + _lastBuildLog;
        }
        else
        {
            try { _currentApp?.RestoreState(state); } catch { /* ignored */ }
        }

        if (oldAlc != null)
        {
            _unloadRef = new WeakReference(oldAlc);
            oldAlc.Unload();
            for (int i = 0; i < 5 && _unloadRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _watcher?.Dispose(); } catch { }
        try { _currentApp?.Dispose(); } catch { }
        try { _currentAlc?.Unload(); } catch { }
    }
}
