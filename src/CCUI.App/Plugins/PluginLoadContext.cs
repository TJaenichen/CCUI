using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CCUI.App.Plugins;

/// <summary>
/// One load context per plugin. Assemblies the app already has (the contract, WPF, Microsoft.Extensions.*) come
/// from the app, so their types unify with the host's; everything else comes from the plugin's own folder through
/// its deps.json.
/// </summary>
internal sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath))
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    /// <summary>True when the app has an assembly of this simple name loaded; a plugin folder's copy is then ignored.</summary>
    public static bool IsSharedWithApp(string simpleName) =>
        Default.Assemblies.Any(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && IsSharedWithApp(name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
