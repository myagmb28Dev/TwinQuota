using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TwinQuota.Core;
using InputMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MediaBrush = System.Windows.Media.Brush;

namespace TwinQuota.Windows;

public partial class MiniWindow : Window, INotifyPropertyChanged
{
    private readonly MiniWindowStateStore _stateStore = new();
    private readonly DispatcherTimer _locationSaveTimer;
    private string _modelName = "TwinQuota";
    private string _resetTimeText = string.Empty;
    private Visibility _resetTimeVisibility = Visibility.Collapsed;
    private string _displayContextText = string.Empty;
    private Visibility _contextVisibility = Visibility.Collapsed;
    private string _contextTooltipText = string.Empty;
    private MediaBrush _contextForegroundBrush = BrushFrom("#D5DCEB");
    private string _windowName = "Weekly";
    private double _remainingPercent = 100;
    private string _remainingPercentText = "100%";
    private MediaBrush _progressBrush = BrushFrom("#38D6A2");
    private Visibility _controlButtonsVisibility = Visibility.Collapsed;

    public MiniWindow()
    {
        InitializeComponent();
        DataContext = this;

        _locationSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _locationSaveTimer.Tick += (_, _) =>
        {
            _locationSaveTimer.Stop();
            SaveLocation();
        };

        LocationChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal && IsVisible)
            {
                _locationSaveTimer.Stop();
                _locationSaveTimer.Start();
            }
        };

        Loaded += (_, _) => RestoreSavedLocation();
    }

    public event EventHandler? RestoreNormalModeRequested;
    public event EventHandler? ExitRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ModelName
    {
        get => _modelName;
        private set => SetField(ref _modelName, value);
    }

    public string ResetTimeText
    {
        get => _resetTimeText;
        private set => SetField(ref _resetTimeText, value);
    }

    public Visibility ResetTimeVisibility
    {
        get => _resetTimeVisibility;
        private set => SetField(ref _resetTimeVisibility, value);
    }

    public string DisplayContextText
    {
        get => _displayContextText;
        private set => SetField(ref _displayContextText, value);
    }

    public Visibility ContextVisibility
    {
        get => _contextVisibility;
        private set => SetField(ref _contextVisibility, value);
    }

    public string ContextTooltipText
    {
        get => _contextTooltipText;
        private set => SetField(ref _contextTooltipText, value);
    }

    public MediaBrush ContextForegroundBrush
    {
        get => _contextForegroundBrush;
        private set => SetField(ref _contextForegroundBrush, value);
    }

    public string WindowName
    {
        get => _windowName;
        private set => SetField(ref _windowName, value);
    }

    public double RemainingPercent
    {
        get => _remainingPercent;
        set => SetField(ref _remainingPercent, value);
    }

    public string RemainingPercentText
    {
        get => _remainingPercentText;
        private set => SetField(ref _remainingPercentText, value);
    }

    public MediaBrush ProgressBrush
    {
        get => _progressBrush;
        private set => SetField(ref _progressBrush, value);
    }

    public Visibility ControlButtonsVisibility
    {
        get => _controlButtonsVisibility;
        private set => SetField(ref _controlButtonsVisibility, value);
    }

    public void UpdateSnapshot(TwinQuotaSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var activeModel = snapshot.ActiveModelId is { Length: > 0 } activeId
            ? snapshot.Models.FirstOrDefault(m => m.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase))
            : snapshot.Models.Count == 1
                ? snapshot.Models[0]
                : null;

        ModelName = activeModel?.DisplayName ?? "No active model";

        var selectedBuckets = ActiveQuotaSelector
            .Select(snapshot.QuotaGroups, activeModel)
            .SelectMany(group => group.Buckets)
            .OrderBy(QuotaWindowOrder)
            .ThenBy(bucket => bucket.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var primaryBucket = selectedBuckets.FirstOrDefault();
        DateTimeOffset? resetTime = primaryBucket?.ResetTime ?? activeModel?.ResetTime;

        if (primaryBucket is not null)
        {
            WindowName = FormatWindowName(primaryBucket);
            var remaining = Math.Clamp(primaryBucket.RemainingFraction * 100, 0, 100);
            RemainingPercent = remaining;
            RemainingPercentText = $"{(int)Math.Round(remaining)}%";
            ProgressBrush = QuotaBrush(remaining);
        }
        else if (activeModel?.RemainingFraction is double fraction)
        {
            WindowName = "Quota";
            var remaining = Math.Clamp(fraction * 100, 0, 100);
            RemainingPercent = remaining;
            RemainingPercentText = $"{(int)Math.Round(remaining)}%";
            ProgressBrush = QuotaBrush(remaining);
        }
        else
        {
            WindowName = "Quota";
            RemainingPercent = 100;
            RemainingPercentText = "100%";
            ProgressBrush = BrushFrom("#38D6A2");
        }

        var formattedReset = QuotaDisplayFormatter.FormatMiniReset(resetTime, now);
        if (!string.IsNullOrWhiteSpace(formattedReset))
        {
            ResetTimeText = formattedReset;
            ResetTimeVisibility = Visibility.Visible;
        }
        else
        {
            ResetTimeText = string.Empty;
            ResetTimeVisibility = Visibility.Collapsed;
        }

        if (snapshot.ContextUsage is { } context)
        {
            ContextVisibility = Visibility.Visible;
            DisplayContextText = $"{context.UsedPercent:0.#}%";
            ContextTooltipText = $"{context.HoverText}\n({(int)Math.Round(context.UsedPercent)}% used)";
            ContextForegroundBrush = ContextBrush(context.UsedPercent);
        }
        else
        {
            ContextVisibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, InputMouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseRightButtonUp(object sender, InputMouseButtonEventArgs e)
    {
        ControlButtonsVisibility = ControlButtonsVisibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RestoreNormalMode_Click(object sender, RoutedEventArgs e)
    {
        ControlButtonsVisibility = Visibility.Collapsed;
        RestoreNormalModeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreSavedLocation()
    {
        var saved = _stateStore.Load();
        if (saved is null)
        {
            Left = SystemParameters.WorkArea.Right - 320;
            Top = SystemParameters.WorkArea.Bottom - 120;
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        if (saved.Left >= virtualLeft && saved.Left < virtualRight - 50 &&
            saved.Top >= virtualTop && saved.Top < virtualBottom - 50)
        {
            Left = saved.Left;
            Top = saved.Top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - 320;
            Top = SystemParameters.WorkArea.Bottom - 120;
        }
    }

    private void SaveLocation()
    {
        if (WindowState == WindowState.Normal)
        {
            _stateStore.Save(Left, Top);
        }
    }

    private static int QuotaWindowOrder(QuotaBucket bucket)
    {
        var identity = $"{bucket.DisplayName} {bucket.Window}";
        if (identity.Contains("week", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (identity.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("five hour", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static string FormatWindowName(QuotaBucket bucket)
    {
        var identity = $"{bucket.DisplayName} {bucket.Window}";
        if (identity.Contains("week", StringComparison.OrdinalIgnoreCase))
        {
            return "Weekly";
        }

        if (identity.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("five hour", StringComparison.OrdinalIgnoreCase))
        {
            return "5 hours";
        }

        return bucket.DisplayName;
    }

    private static SolidColorBrush QuotaBrush(double percent) => percent switch
    {
        < 15 => BrushFrom("#FF7B8B"),
        < 35 => BrushFrom("#FFBF69"),
        _ => BrushFrom("#38D6A2")
    };

    private static SolidColorBrush ContextBrush(double percent) => percent switch
    {
        >= 85 => BrushFrom("#FF7B8B"),
        >= 60 => BrushFrom("#FFBF69"),
        _ => BrushFrom("#D5DCEB")
    };

    private static SolidColorBrush BrushFrom(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
