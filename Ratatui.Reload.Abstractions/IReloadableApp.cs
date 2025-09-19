using Ratatui;

namespace Ratatui.Reload.Abstractions;

/// <summary>
/// Interface for apps that support hot reloading via RatHost.
/// </summary>
public interface IReloadableApp : IDisposable {
    Task<bool> RunAsync(Terminal terminal, CancellationToken cancellationToken);
}