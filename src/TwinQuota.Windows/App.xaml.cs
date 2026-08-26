using System.Drawing;
using System.IO;
using System.Windows;
using TwinQuota.Core;
using Forms = System.Windows.Forms;

namespace TwinQuota.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _applicationIcon;
    private MainWindow? _mainWindow;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var hookRegistration = new AntigravityHookRegistration();
        if (e.Args.Contains("--unregister-antigravity-hook", StringComparer.OrdinalIgnoreCase))
        {
            hookRegistration.Remove();
            Shutdown();
            return;
        }

        var hookExecutablePath = Path.Combine(AppContext.BaseDirectory, "TwinQuota.Hook.exe");
        if (File.Exists(hookExecutablePath))
        {
            hookRegistration.EnsureRegistered(hookExecutablePath);
        }

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Closing += (_, args) =>
        {
            _mainWindow.SaveWindowSize();
            if (_exitRequested)
            {
                return;
            }

            args.Cancel = true;
            _mainWindow.ResetPendingSettings();
            _mainWindow.Hide();
        };
        _mainWindow.StatusTextChanged += (_, status) =>
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Text = status.Length <= 63 ? status : status[..63];
            }
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _applicationIcon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "TwinQuota",
            Visible = true,
            ContextMenuStrip = menu
        };

        if (_mainWindow.ShouldShowOnStartup)
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _applicationIcon?.Dispose();
        base.OnExit(e);
    }

    private static Icon LoadApplicationIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/TwinQuota.ico"));
        if (resource is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using (resource.Stream)
        using (var icon = new Icon(resource.Stream))
        {
            return (Icon)icon.Clone();
        }
    }

    internal void ExitApplication()
    {
        _exitRequested = true;
        _mainWindow?.Close();
        Shutdown();
    }
}
