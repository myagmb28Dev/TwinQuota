using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TwinQuota.Windows;

internal sealed class AntigravityWindowDetector
{
    private static readonly HashSet<string> TerminalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "conhost",
        "powershell",
        "pwsh",
        "windowsterminal",
        "wt"
    };

    private static readonly HashSet<string> IdeHostProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "codeinsiders",
        "cursor",
        "windsurf",
        "vscodium"
    };

    public bool HasVisibleWindow()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero || !IsWindowVisible(windowHandle) || IsIconic(windowHandle))
                    {
                        continue;
                    }

                    var processName = Normalize(process.ProcessName);
                    if (IsAntigravityProcess(processName))
                    {
                        return true;
                    }

                    if (TerminalProcessNames.Contains(process.ProcessName) &&
                        IsAntigravityTerminalTitle(process.MainWindowTitle))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Processes can exit or become inaccessible while the list is being inspected.
                }
            }
        }

        return false;
    }

    public bool IsForegroundWindowAntigravity()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero || !IsWindowVisible(windowHandle) || IsIconic(windowHandle) ||
            GetWindowThreadProcessId(windowHandle, out var processId) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var processName = Normalize(process.ProcessName);
            return IsAntigravityProcess(processName) ||
                   (TerminalProcessNames.Contains(process.ProcessName) &&
                    IsAntigravityTerminalTitle(process.MainWindowTitle));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public bool IsForegroundWindowOwnedByCurrentProcess()
    {
        var windowHandle = GetForegroundWindow();
        return windowHandle != IntPtr.Zero &&
               GetWindowThreadProcessId(windowHandle, out var processId) != 0 &&
               processId == Environment.ProcessId;
    }

    private static bool IsAntigravityProcess(string normalizedName) =>
        normalizedName.StartsWith("antigravity", StringComparison.OrdinalIgnoreCase) ||
        normalizedName.Equals("agy", StringComparison.OrdinalIgnoreCase) ||
        IdeHostProcessNames.Contains(normalizedName);

    private static bool IsAntigravityTerminalTitle(string title) =>
        title.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("agy", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("agy ", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));

    #pragma warning disable SYSLIB1054
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);
    #pragma warning restore SYSLIB1054
}
