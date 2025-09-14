using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Thaum.App.RatatuiTUI;

internal interface IEditorOpener
{
    void Open(string projectPath, string filePath, int line);
}

internal sealed class DefaultEditorOpener : IEditorOpener
{
    public void Open(string projectPath, string filePath, int line)
    {
        string full = Path.IsPathRooted(filePath) ? filePath : Path.Combine(projectPath, filePath);
        string? cmd = Environment.GetEnvironmentVariable("THAUM_EDITOR") ?? Environment.GetEnvironmentVariable("EDITOR");
        string args = string.Empty;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        if (string.IsNullOrWhiteSpace(cmd))
        {
            if (isWindows) { cmd = "code"; args = $"-g \"{full}\":{line}"; }
            else if (isMac) { cmd = "open"; args = $"-a \"Visual Studio Code\" --args -g \"{full}\":{line}"; }
            else if (File.Exists("/usr/bin/code") || File.Exists("/usr/local/bin/code")) { cmd = "code"; args = $"-g \"{full}\":{line}"; }
            else if (File.Exists("/usr/bin/nvim") || File.Exists("/usr/local/bin/nvim")) { cmd = "nvim"; args = $"+{line} \"{full}\""; }
            else if (File.Exists("/usr/bin/vim") || File.Exists("/usr/local/bin/vim")) { cmd = "vim"; args = $"+{line} \"{full}\""; }
            else { Console.WriteLine($"Open: {full}:{line}"); return; }
        }
        else
        {
            string c = cmd.ToLowerInvariant();
            if (c.Contains("code")) args = $"-g \"{full}\":{line}";
            else if (c.Contains("nvim") || c.Contains("vim")) args = $"+{line} \"{full}\"";
            else if (isMac && c == "open") args = $"\"{full}\"";
            else args = $"\"{full}\"";
        }

        var psi = new ProcessStartInfo(cmd!, args) { UseShellExecute = false };
        Process.Start(psi);
    }
}

