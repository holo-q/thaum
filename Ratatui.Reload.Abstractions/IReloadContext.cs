using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Ratatui.Reload.Abstractions;

public interface IReloadContext {
	ILogger           Logger          { get; }
	IServiceProvider  Services        { get; }
	CancellationToken AppCancellation { get; }
	string            ProjectPath     { get; }
}