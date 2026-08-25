using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TwinQuota.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Closing += (_, args) =>
        {
            if (_exitRequested)
            {
                return;
            }

            args.Cancel = true;
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
        menu.Items.Add("Open TwinQuota", null, (_, _) => ShowWindow());
        menu.Items.Add("Refresh", null, async (_, _) =>
            await Dispatcher.InvokeAsync(async () => await _mainWindow.RefreshAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "TwinQuota",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    private void ShowWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _mainWindow?.Close();
        Shutdown();
    }
}
