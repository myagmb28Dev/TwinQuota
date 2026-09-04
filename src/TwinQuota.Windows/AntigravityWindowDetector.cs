using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TwinQuota.Core;

namespace TwinQuota.Windows;

internal sealed class AntigravityWindowDetector
{
    private const uint Th32CsSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

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

    private readonly string _userProfile;

    public AntigravityWindowDetector(string? userProfile = null)
    {
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public bool HasVisibleWindow()
    {
        var processTree = CaptureProcessTree();
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
                    if (IsAntigravityProcess(processName, process.Id, processTree))
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
            return IsAntigravityProcess(processName, process.Id, CaptureProcessTree()) ||
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

    private bool IsAntigravityProcess(
        string normalizedName,
        int processId,
        IReadOnlyList<ProcessTreeEntry> processTree)
    {
        if (normalizedName.StartsWith("antigravity", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IdeHostProcessNames.Contains(normalizedName)
            && HasInstalledExtension(normalizedName)
            && AntigravityProcessTree.HasActiveExtensionDescendant(processId, processTree);
    }

    private bool HasInstalledExtension(string normalizedHostName)
    {
        var roots = normalizedHostName.ToLowerInvariant() switch
        {
            "code" => new[] { Path.Combine(_userProfile, ".vscode", "extensions") },
            "codeinsiders" => new[] { Path.Combine(_userProfile, ".vscode-insiders", "extensions") },
            "cursor" => new[] { Path.Combine(_userProfile, ".cursor", "extensions") },
            "windsurf" => new[] { Path.Combine(_userProfile, ".windsurf", "extensions") },
            "vscodium" => new[]
            {
                Path.Combine(_userProfile, ".vscode-oss", "extensions"),
                Path.Combine(_userProfile, ".vscodium", "extensions")
            },
            _ => []
        };

        return roots.Any(ContainsAntigravityExtension);
    }

    private static bool ContainsAntigravityExtension(string root)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateDirectories(root, "google.google-antigravity*").Any()
                || Directory.EnumerateDirectories(root, "google.antigravity*").Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ProcessTreeEntry> CaptureProcessTree()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == InvalidHandleValue)
        {
            return [];
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return [];
            }

            var processes = new List<ProcessTreeEntry>();
            do
            {
                processes.Add(new ProcessTreeEntry(
                    unchecked((int)entry.ProcessId),
                    unchecked((int)entry.ParentProcessId),
                    Path.GetFileNameWithoutExtension(entry.ExecutableFile)));
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return processes;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    #pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint UsageCount;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }
}
