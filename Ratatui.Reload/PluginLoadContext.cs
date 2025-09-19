using System.Reflection;
using System.Runtime.Loader;

namespace Ratatui.Reload;

internal sealed class PluginLoadContext : AssemblyLoadContext {
	private readonly AssemblyDependencyResolver _resolver;

	public PluginLoadContext(string mainAssemblyPath) : base(isCollectible: true) {
		_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
	}

	protected override Assembly? Load(AssemblyName assemblyName) {
		string? path = _resolver.ResolveAssemblyToPath(assemblyName);
		return path != null
			? LoadFromAssemblyPath(path)
			: null;
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
		string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return path != null
			? LoadUnmanagedDllFromPath(path)
			: IntPtr.Zero;
	}
}