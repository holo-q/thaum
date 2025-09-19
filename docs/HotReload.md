Ratatui Hot Reload (AssemblyLoadContext)

Overview
- Stable host process owns the terminal and loads a TUI plugin assembly into a collectible AssemblyLoadContext. On file changes, it rebuilds the plugin, swaps in a fresh load, and unloads the old one.

Projects
- `Ratatui.Reload.Abstractions`: Interfaces for `IReloadableApp` and `IReloadContext`.
- `Ratatui.Reload`: `HotReloadRunner` (host + watcher + builder) and `PluginLoadContext`.
- `Thaum.TUI`: Sample plugin implementing `IReloadableApp` to demonstrate reload.

Usage
- Run: `dotnet run --project Thaum.App -- tui-watch` (Ctrl+C to quit)
- Edit code under `Thaum.TUI` and save to trigger rebuild and live reload.
- Pass a custom plugin project: `tui-watch --plugin path/to/YourPlugin.csproj`.

Manual Reload Mode
- Show a bottom-left hint and manually trigger reloads:
  - `dotnet run --project Thaum.App -- tui-watch --manual --hint`
  - Press `r` to reload when the hint shows “Changes detected”.
  - Customize the key: `--key x`

Integration Steps for Existing Apps
1) Move your TUI rendering loop into a class implementing `IReloadableApp`.
2) Let the host own `Terminal`. Implement `HandleEvent`, `Update`, and `Draw`.
3) Implement `CaptureState`/`RestoreState` to preserve navigation and selections across reloads.
4) Start via `HotReloadRunner` from your CLI or program entrypoint.

Notes
- This is IDE-agnostic and works in plain terminals.
- If unload blockers exist (native libs, static singletons), refactor them into the host or fall back to out-of-process reload.
