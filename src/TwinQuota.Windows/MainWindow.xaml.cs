using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TwinQuota.Core;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using InputMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MediaBrush = System.Windows.Media.Brush;
using WindowsPoint = System.Windows.Point;
using WindowsSize = System.Windows.Size;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;

namespace TwinQuota.Windows;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double ResizeHitThickness = 8;

    private readonly TwinQuotaMonitor _monitor = new();
    private readonly AntigravityWindowDetector _antigravityWindowDetector = new();
    private readonly AppSettingsStore _appSettingsStore = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _windowDetectionTimer;
    private readonly DispatcherTimer _sizeSaveTimer;
    private readonly DispatcherTimer _sidebarCloseTimer;
    private readonly WindowSizeStore _windowSizeStore = new();
    private HwndSource? _windowSource;
    private bool _refreshing;
    private bool _hasAntigravityWindow;
    private bool _savedShowOnlyWhenAntigravityWindowIsOpen;
    private bool _pendingShowOnlyWhenAntigravityWindowIsOpen;
    private string _activeModelName = "Looking for an active model…";
    private string? _expandedModelKey;
    private TwinQuotaSnapshot? _lastSnapshot;
    private Visibility _dashboardVisibility = Visibility.Visible;
    private Visibility _modelsVisibility = Visibility.Collapsed;
    private Visibility _settingsVisibility = Visibility.Collapsed;
    private Visibility _contextGaugeVisibility = Visibility.Collapsed;
    private Geometry? _contextArcGeometry;
    private MediaBrush _contextGaugeBrush = BrushFrom("#8B7CFF");
    private string _contextHoverHeader = "Context usage";
    private string _contextHoverSubtext = string.Empty;
    private MediaBrush _homeTabBackground = BrushFrom("#29234F");
    private MediaBrush _modelsTabBackground = BrushFrom("#151B2E");
    private MediaBrush _settingsTabBackground = BrushFrom("#151B2E");

    public MainWindow()
    {
        InitializeComponent();
        var settings = _appSettingsStore.Load();
        _savedShowOnlyWhenAntigravityWindowIsOpen = settings.ShowOnlyWhenAntigravityWindowIsOpen;
        _pendingShowOnlyWhenAntigravityWindowIsOpen = settings.ShowOnlyWhenAntigravityWindowIsOpen;
        _hasAntigravityWindow = _antigravityWindowDetector.HasVisibleWindow();
        RestoreSavedSize();
        DataContext = this;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                SidebarPopup.IsOpen = false;
                SidebarSensorPopup.IsOpen = false;
                return;
            }

            SidebarSensorPopup.IsOpen = IsVisible;
        };
        IsVisibleChanged += (_, _) =>
        {
            SidebarPopup.IsOpen = false;
            SidebarSensorPopup.IsOpen = IsVisible && WindowState != WindowState.Minimized;
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(
            HideIfFocusMovedOutsideAntigravity,
            DispatcherPriority.Background);
        LocationChanged += (_, _) => RepositionSidebars();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _windowDetectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _windowDetectionTimer.Tick += (_, _) => UpdateAntigravityWindowPresence();
        _windowDetectionTimer.Start();
        _sidebarCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _sidebarCloseTimer.Tick += (_, _) =>
        {
            _sidebarCloseTimer.Stop();
            if (!SidebarHoverZone.IsMouseOver && !SidebarSensor.IsMouseOver && !SidebarPanel.IsMouseOver)
            {
                SidebarPopup.IsOpen = false;
            }
        };
        _sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _sizeSaveTimer.Tick += (_, _) =>
        {
            _sizeSaveTimer.Stop();
            SaveWindowSize();
        };

        SizeChanged += (_, _) =>
        {
            UpdateSidebarScale();
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            _sizeSaveTimer.Stop();
            _sizeSaveTimer.Start();
        };
        Loaded += async (_, _) =>
        {
            UpdateSidebarScale();
            SidebarSensorPopup.IsOpen = true;
            _refreshTimer.Start();
            await RefreshAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _windowDetectionTimer.Stop();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        base.OnClosed(e);
    }

    public ObservableCollection<QuotaRow> Quotas { get; } = [];
    public ObservableCollection<ModelRow> AvailableModels { get; } = [];

    internal bool ShouldShowOnStartup =>
        !_savedShowOnlyWhenAntigravityWindowIsOpen || _hasAntigravityWindow;

    public string ActiveModelName
    {
        get => _activeModelName;
        private set => SetField(ref _activeModelName, value);
    }

    public Visibility DashboardVisibility
    {
        get => _dashboardVisibility;
        private set => SetField(ref _dashboardVisibility, value);
    }

    public Visibility ModelsVisibility
    {
        get => _modelsVisibility;
        private set => SetField(ref _modelsVisibility, value);
    }

    public Visibility SettingsVisibility
    {
        get => _settingsVisibility;
        private set => SetField(ref _settingsVisibility, value);
    }

    public Visibility ContextGaugeVisibility
    {
        get => _contextGaugeVisibility;
        private set => SetField(ref _contextGaugeVisibility, value);
    }

    public Geometry? ContextArcGeometry
    {
        get => _contextArcGeometry;
        private set => SetField(ref _contextArcGeometry, value);
    }

    public MediaBrush ContextGaugeBrush
    {
        get => _contextGaugeBrush;
        private set => SetField(ref _contextGaugeBrush, value);
    }

    public string ContextHoverHeader
    {
        get => _contextHoverHeader;
        private set => SetField(ref _contextHoverHeader, value);
    }

    public string ContextHoverSubtext
    {
        get => _contextHoverSubtext;
        private set => SetField(ref _contextHoverSubtext, value);
    }

    public MediaBrush HomeTabBackground
    {
        get => _homeTabBackground;
        private set => SetField(ref _homeTabBackground, value);
    }

    public MediaBrush ModelsTabBackground
    {
        get => _modelsTabBackground;
        private set => SetField(ref _modelsTabBackground, value);
    }

    public MediaBrush SettingsTabBackground
    {
        get => _settingsTabBackground;
        private set => SetField(ref _settingsTabBackground, value);
    }

    public bool ShowOnlyWhenAntigravityWindowIsOpen
    {
        get => _pendingShowOnlyWhenAntigravityWindowIsOpen;
        set
        {
            if (!SetField(ref _pendingShowOnlyWhenAntigravityWindowIsOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasUnsavedSettings));
        }
    }

    public bool HasUnsavedSettings =>
        _pendingShowOnlyWhenAntigravityWindowIsOpen != _savedShowOnlyWhenAntigravityWindowIsOpen;

    public string AntigravityWindowStatus => _hasAntigravityWindow
        ? "Antigravity window detected"
        : "No Antigravity window detected";

    public MediaBrush AntigravityWindowStatusBrush => _hasAntigravityWindow
        ? BrushFrom("#38D6A2")
        : BrushFrom("#9CA8C5");

    public string ModelCountText => AvailableModels.Count == 1
        ? "1 model"
        : $"{AvailableModels.Count} models";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? StatusTextChanged;

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var snapshot = await _monitor.RefreshAsync();
            ApplySnapshot(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            ActiveModelName = "Unable to refresh";
            StatusTextChanged?.Invoke(this, "TwinQuota · Unable to refresh");
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void SaveWindowSize()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            _windowSizeStore.Save(bounds.Width, bounds.Height);
        }
    }

    private void RestoreSavedSize()
    {
        var size = _windowSizeStore.Load();
        if (size is null || !double.IsFinite(size.Width) || !double.IsFinite(size.Height))
        {
            return;
        }

        Width = Math.Max(MinWidth, size.Width);
        Height = Math.Max(MinHeight, size.Height);
    }

    private void ApplySnapshot(TwinQuotaSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        var activeModel = snapshot.ActiveModelId is { Length: > 0 } activeId
            ? snapshot.Models.FirstOrDefault(model => model.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase))
            : snapshot.Models.Count == 1
                ? snapshot.Models[0]
                : null;
        ActiveModelName = activeModel?.DisplayName ?? "No active model";
        PopulateQuotas(Quotas, snapshot, activeModel);

        var contextUsage = snapshot.ContextUsage;
        if (contextUsage is not null)
        {
            ContextGaugeVisibility = Visibility.Visible;
            var usedPercent = (int)Math.Round(contextUsage.UsedPercent, MidpointRounding.AwayFromZero);
            var remainingPercent = Math.Max(0, 100 - usedPercent);
            ContextHoverHeader = "Context length:";
            ContextHoverSubtext = $"{usedPercent}% used ({remainingPercent}% left)\n{contextUsage.UsedK}/{contextUsage.MaxK} tokens used";
            ContextGaugeBrush = ContextBrush(contextUsage.UsedPercent);
            ContextArcGeometry = BuildArcGeometry(contextUsage.UsedPercent);
        }
        else
        {
            ContextGaugeVisibility = Visibility.Collapsed;
            ContextArcGeometry = null;
        }

        AvailableModels.Clear();
        foreach (var family in ModelFamilyGrouper.Group(snapshot.Models))
        {
            var row = new ModelRow(
                family.DisplayName,
                family.Priorities.Count > 0 ? string.Join(" · ", family.Priorities) : "Default",
                family);
            if (GetModelKey(family) == _expandedModelKey)
            {
                row.DetailVisibility = Visibility.Visible;
                PopulateQuotas(row.Quotas, snapshot, SelectRepresentative(family));
            }

            AvailableModels.Add(row);
        }

        OnPropertyChanged(nameof(ModelCountText));

        var firstQuota = Quotas.FirstOrDefault();
        var trayStatus = activeModel is null
            ? "TwinQuota · No active model"
            : firstQuota is null
                ? $"TwinQuota · {activeModel.DisplayName}"
                : $"TwinQuota · {activeModel.DisplayName} {firstQuota.RemainingText}";
        StatusTextChanged?.Invoke(this, trayStatus);
    }

    private static void PopulateQuotas(
        ObservableCollection<QuotaRow> destination,
        TwinQuotaSnapshot snapshot,
        ModelAvailability? model)
    {
        destination.Clear();
        var selectedBuckets = ActiveQuotaSelector
            .Select(snapshot.QuotaGroups, model)
            .SelectMany(group => group.Buckets)
            .OrderBy(QuotaWindowOrder)
            .ThenBy(bucket => bucket.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var bucket in selectedBuckets)
        {
            AddQuota(destination, bucket);
        }

        if (destination.Count == 0 && model?.RemainingFraction is double remainingFraction)
        {
            AddQuota(destination, new QuotaBucket(
                model.Id,
                "Model quota",
                string.Empty,
                remainingFraction,
                model.ResetTime,
                null));
        }
    }

    private static void AddQuota(ObservableCollection<QuotaRow> destination, QuotaBucket bucket)
    {
        var remainingPercent = Math.Clamp(bucket.RemainingFraction * 100, 0, 100);
        var usedPercent = 100 - remainingPercent;
        destination.Add(new QuotaRow(
            FormatWindowName(bucket),
            remainingPercent,
            $"{remainingPercent:0.#}%",
            $"Used {usedPercent:0.#}%, {FormatReset(bucket.ResetTime)}",
            QuotaBrush(remainingPercent)));
    }

    private void HomeTab_Click(object sender, RoutedEventArgs e)
    {
        SelectTab(AppTab.Dashboard);
        SidebarPopup.IsOpen = false;
    }

    private void ModelsTab_Click(object sender, RoutedEventArgs e)
    {
        SelectTab(AppTab.Models);
        SidebarPopup.IsOpen = false;
    }

    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        SelectTab(AppTab.Settings);
        SidebarPopup.IsOpen = false;
    }

    private void SettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _savedShowOnlyWhenAntigravityWindowIsOpen = _pendingShowOnlyWhenAntigravityWindowIsOpen;
        _appSettingsStore.Save(new AppSettings(_savedShowOnlyWhenAntigravityWindowIsOpen));
        OnPropertyChanged(nameof(HasUnsavedSettings));
    }

    private void SelectTab(AppTab tab)
    {
        DashboardVisibility = tab == AppTab.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        ModelsVisibility = tab == AppTab.Models ? Visibility.Visible : Visibility.Collapsed;
        SettingsVisibility = tab == AppTab.Settings ? Visibility.Visible : Visibility.Collapsed;
        HomeTabBackground = BrushFrom(tab == AppTab.Dashboard ? "#29234F" : "#151B2E");
        ModelsTabBackground = BrushFrom(tab == AppTab.Models ? "#29234F" : "#151B2E");
        SettingsTabBackground = BrushFrom(tab == AppTab.Settings ? "#29234F" : "#151B2E");
    }

    private void ModelRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModelRow row })
        {
            return;
        }

        var shouldExpand = row.DetailVisibility != Visibility.Visible;
        foreach (var modelRow in AvailableModels)
        {
            modelRow.DetailVisibility = Visibility.Collapsed;
        }

        _expandedModelKey = shouldExpand ? GetModelKey(row.Family) : null;
        if (!shouldExpand || _lastSnapshot is null)
        {
            return;
        }

        PopulateQuotas(row.Quotas, _lastSnapshot, SelectRepresentative(row.Family));
        row.DetailVisibility = Visibility.Visible;
    }

    private static ModelAvailability SelectRepresentative(ModelFamily family) =>
        family.Models.FirstOrDefault(model => model.RemainingFraction is not null) ?? family.Models[0];

    private static string GetModelKey(ModelFamily family) => $"{family.Provider}\u001f{family.DisplayName}";

    private void SidebarHoverZone_MouseEnter(object sender, InputMouseEventArgs e)
    {
        _sidebarCloseTimer.Stop();
        SidebarPopup.IsOpen = true;
    }

    private void SidebarHoverZone_MouseLeave(object sender, InputMouseEventArgs e) => ScheduleSidebarClose();

    private void SidebarPanel_MouseEnter(object sender, InputMouseEventArgs e) => _sidebarCloseTimer.Stop();

    private void SidebarPanel_MouseLeave(object sender, InputMouseEventArgs e) => ScheduleSidebarClose();

    private void SidebarSensor_MouseEnter(object sender, InputMouseEventArgs e)
    {
        _sidebarCloseTimer.Stop();
        SidebarPopup.IsOpen = true;
    }

    private void SidebarSensor_MouseLeave(object sender, InputMouseEventArgs e) => ScheduleSidebarClose();

    private void ScheduleSidebarClose()
    {
        _sidebarCloseTimer.Stop();
        _sidebarCloseTimer.Start();
    }

    private void UpdateSidebarScale()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var linearScale = Math.Clamp(Math.Min(ActualWidth / 240, ActualHeight / 300), 0.5, 1);
        var scale = Math.Sqrt(linearScale);
        SidebarPanel.LayoutTransform = new ScaleTransform(scale, scale);
        SidebarPopup.HorizontalOffset = -62 * scale;
        SidebarPopup.VerticalOffset = 50 * scale;

        SidebarSensor.Width = 74 * scale;
        SidebarSensor.Height = 174 * scale;
        SidebarSensorPopup.HorizontalOffset = -62 * scale;
        SidebarSensorPopup.VerticalOffset = 50 * scale;

        SidebarHoverZone.Width = 12 * scale;
        SidebarHoverZone.Height = 174 * scale;
        SidebarHoverZone.Margin = new Thickness(0, 50 * scale, 0, 0);
    }

    private void RepositionSidebars()
    {
        if (SidebarPopup.IsOpen)
        {
            var offset = SidebarPopup.HorizontalOffset;
            SidebarPopup.HorizontalOffset = offset + 1;
            SidebarPopup.HorizontalOffset = offset;
        }

        if (SidebarSensorPopup.IsOpen)
        {
            var offset = SidebarSensorPopup.HorizontalOffset;
            SidebarSensorPopup.HorizontalOffset = offset + 1;
            SidebarSensorPopup.HorizontalOffset = offset;
        }
    }

    private void UpdateAntigravityWindowPresence()
    {
        var isAntigravityForeground = _antigravityWindowDetector.IsForegroundWindowAntigravity();
        var hasWindow = isAntigravityForeground || _antigravityWindowDetector.HasVisibleWindow();
        var presenceChanged = hasWindow != _hasAntigravityWindow;
        if (presenceChanged)
        {
            _hasAntigravityWindow = hasWindow;
            OnPropertyChanged(nameof(AntigravityWindowStatus));
            OnPropertyChanged(nameof(AntigravityWindowStatusBrush));
        }

        if (!_savedShowOnlyWhenAntigravityWindowIsOpen)
        {
            return;
        }

        if (isAntigravityForeground)
        {
            if (!IsVisible)
            {
                ShowForAntigravityWindow();
            }

            return;
        }

        if (!hasWindow)
        {
            HideForFocusLoss();
            return;
        }

        HideIfFocusMovedOutsideAntigravity(isAntigravityForeground);
    }

    internal void ResetPendingSettings()
    {
        if (_pendingShowOnlyWhenAntigravityWindowIsOpen == _savedShowOnlyWhenAntigravityWindowIsOpen)
        {
            return;
        }

        _pendingShowOnlyWhenAntigravityWindowIsOpen = _savedShowOnlyWhenAntigravityWindowIsOpen;
        OnPropertyChanged(nameof(ShowOnlyWhenAntigravityWindowIsOpen));
        OnPropertyChanged(nameof(HasUnsavedSettings));
    }

    private void ShowForAntigravityWindow()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    private void HideIfFocusMovedOutsideAntigravity() =>
        HideIfFocusMovedOutsideAntigravity(_antigravityWindowDetector.IsForegroundWindowAntigravity());

    private void HideIfFocusMovedOutsideAntigravity(bool isAntigravityForeground)
    {
        if (!_savedShowOnlyWhenAntigravityWindowIsOpen || !IsVisible || IsActive ||
            _antigravityWindowDetector.IsForegroundWindowOwnedByCurrentProcess() ||
            isAntigravityForeground)
        {
            return;
        }

        HideForFocusLoss();
    }

    private void HideForFocusLoss()
    {
        if (!IsVisible)
        {
            return;
        }

        SaveWindowSize();
        _refreshTimer.Stop();
        ResetPendingSettings();
        SidebarPopup.IsOpen = false;
        SidebarSensorPopup.IsOpen = false;
        WindowState = WindowState.Minimized;
        Hide();
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != WmNcHitTest || WindowState != WindowState.Normal ||
            !GetWindowRect(windowHandle, out var windowRect))
        {
            return IntPtr.Zero;
        }

        var packedPoint = longParameter.ToInt64();
        var pointerX = unchecked((short)(packedPoint & 0xFFFF));
        var pointerY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
        var dpi = VisualTreeHelper.GetDpi(this);
        var borderX = Math.Max(1, (int)Math.Ceiling(ResizeHitThickness * dpi.DpiScaleX));
        var borderY = Math.Max(1, (int)Math.Ceiling(ResizeHitThickness * dpi.DpiScaleY));

        var onLeft = pointerX >= windowRect.Left && pointerX < windowRect.Left + borderX;
        var onRight = pointerX <= windowRect.Right && pointerX > windowRect.Right - borderX;
        var onTop = pointerY >= windowRect.Top && pointerY < windowRect.Top + borderY;
        var onBottom = pointerY <= windowRect.Bottom && pointerY > windowRect.Bottom - borderY;

        var hitTest = onTop
            ? onLeft ? HtTopLeft : onRight ? HtTopRight : HtTop
            : onBottom
                ? onLeft ? HtBottomLeft : onRight ? HtBottomRight : HtBottom
                : onLeft
                    ? HtLeft
                    : onRight
                        ? HtRight
                        : 0;
        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    private void ScrollViewer_ScrollChanged(object sender, WpfScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || sender is not WpfScrollViewer scrollViewer)
        {
            return;
        }

        var scrollBar = FindVisualChild<WpfScrollBar>(scrollViewer);
        if (scrollBar is null || scrollBar.Orientation != WpfOrientation.Vertical)
        {
            return;
        }

        scrollBar.BeginAnimation(OpacityProperty, null);
        var fade = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1110),
            FillBehavior = FillBehavior.Stop
        };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.68, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.68, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(850))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1110))));
        scrollBar.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ResetPendingSettings();
        WindowState = WindowState.Minimized;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, InputMouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static int QuotaWindowOrder(QuotaBucket bucket) =>
        IsFiveHour(bucket) ? 0 : IsWeekly(bucket) ? 1 : 2;

    private static string FormatWindowName(QuotaBucket bucket)
    {
        if (IsFiveHour(bucket))
        {
            return "5 hours";
        }

        return IsWeekly(bucket) ? "Weekly" : bucket.DisplayName;
    }

    private static bool IsFiveHour(QuotaBucket bucket)
    {
        var identity = $"{bucket.DisplayName} {bucket.Window}";
        return identity.Contains("5h", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("five hour", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWeekly(QuotaBucket bucket)
    {
        var identity = $"{bucket.DisplayName} {bucket.Window}";
        return identity.Contains("week", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatReset(DateTimeOffset? resetTime)
    {
        if (resetTime is null)
        {
            return "reset time unknown";
        }

        var remaining = resetTime.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "reset due";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"resets in {(int)remaining.TotalDays}d";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"resets in {Math.Max(1, remaining.Minutes)}m";
    }

    private static MediaBrush QuotaBrush(double percent) => percent switch
    {
        < 15 => BrushFrom("#FF7B8B"),
        < 35 => BrushFrom("#FFBF69"),
        _ => BrushFrom("#8B7CFF")
    };

    private static MediaBrush ContextBrush(double percent) => percent switch
    {
        >= 85 => BrushFrom("#FF7B8B"),
        >= 60 => BrushFrom("#FFBF69"),
        _ => BrushFrom("#8B7CFF")
    };

    private static Geometry? BuildArcGeometry(double percent)
    {
        if (percent <= 0)
        {
            return null;
        }

        const double centerX = 14;
        const double centerY = 14;
        const double radius = 11;

        if (percent >= 99.99)
        {
            var fullCircle = new EllipseGeometry(new WindowsPoint(centerX, centerY), radius, radius);
            fullCircle.Freeze();
            return fullCircle;
        }

        var angleRad = (percent / 100.0) * 2.0 * Math.PI;
        var startX = centerX;
        var startY = centerY - radius;
        var endX = centerX + (radius * Math.Sin(angleRad));
        var endY = centerY - (radius * Math.Cos(angleRad));
        var isLargeArc = percent > 50.0;

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new WindowsPoint(startX, startY),
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            new WindowsPoint(endX, endY),
            new WindowsSize(radius, radius),
            0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush BrushFrom(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #pragma warning disable SYSLIB1054
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);
    #pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum AppTab
    {
        Dashboard,
        Models,
        Settings
    }

    public sealed record QuotaRow(
        string WindowName,
        double RemainingPercent,
        string RemainingText,
        string UsageText,
        MediaBrush ProgressBrush);

    public sealed class ModelRow : INotifyPropertyChanged
    {
        private Visibility _detailVisibility = Visibility.Collapsed;

        public ModelRow(string displayName, string prioritiesText, ModelFamily family)
        {
            DisplayName = displayName;
            PrioritiesText = prioritiesText;
            Family = family;
        }

        public string DisplayName { get; }
        public string PrioritiesText { get; }
        public ModelFamily Family { get; }
        public ObservableCollection<QuotaRow> Quotas { get; } = [];

        public Visibility DetailVisibility
        {
            get => _detailVisibility;
            set
            {
                if (_detailVisibility == value)
                {
                    return;
                }

                _detailVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailVisibility)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
