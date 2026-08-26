using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TwinQuota.Core;
using MediaBrush = System.Windows.Media.Brush;

namespace TwinQuota.Windows;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly TwinQuotaMonitor _monitor = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;
    private string _surfaceText = "Antigravity";
    private string _updatedText = "Not refreshed";
    private string _message = "Looking for an active Antigravity model…";
    private string _liveStatusText = "Checking";
    private string _activeModelName = "Checking…";
    private string _activeModelId = "Waiting for Antigravity";
    private string _activeModelProvider = "Unknown";
    private MediaBrush _liveBadgeForeground = BrushFrom("#FFBF69");
    private MediaBrush _liveBadgeBackground = BrushFrom("#3B2D1D");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshAsync();
        };
    }

    public ObservableCollection<QuotaRow> Quotas { get; } = [];

    public string SurfaceText
    {
        get => _surfaceText;
        private set => SetField(ref _surfaceText, value);
    }

    public string UpdatedText
    {
        get => _updatedText;
        private set => SetField(ref _updatedText, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string LiveStatusText
    {
        get => _liveStatusText;
        private set => SetField(ref _liveStatusText, value);
    }

    public string ActiveModelName
    {
        get => _activeModelName;
        private set => SetField(ref _activeModelName, value);
    }

    public string ActiveModelId
    {
        get => _activeModelId;
        private set => SetField(ref _activeModelId, value);
    }

    public string ActiveModelProvider
    {
        get => _activeModelProvider;
        private set => SetField(ref _activeModelProvider, value);
    }

    public MediaBrush LiveBadgeForeground
    {
        get => _liveBadgeForeground;
        private set => SetField(ref _liveBadgeForeground, value);
    }

    public MediaBrush LiveBadgeBackground
    {
        get => _liveBadgeBackground;
        private set => SetField(ref _liveBadgeBackground, value);
    }

    public string QuotaCountText => Quotas.Count == 0
        ? "No quota"
        : Quotas.Count == 1
            ? "1 window"
            : $"{Quotas.Count} windows";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? StatusTextChanged;

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "…";
        try
        {
            var snapshot = await _monitor.RefreshAsync();
            ApplySnapshot(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Message = $"Refresh failed: {exception.Message}";
            LiveStatusText = "Unavailable";
            LiveBadgeForeground = BrushFrom("#FF8A8A");
            LiveBadgeBackground = BrushFrom("#3B2028");
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
            RefreshButton.Content = "↻";
        }
    }

    private void ApplySnapshot(TwinQuotaSnapshot snapshot)
    {
        var activeModel = snapshot.Models.Count == 1 ? snapshot.Models[0] : null;
        if (activeModel is null)
        {
            ActiveModelName = "No active model reported";
            ActiveModelId = "Start an Antigravity session and refresh";
            ActiveModelProvider = "Unavailable";
        }
        else
        {
            ActiveModelName = activeModel.DisplayName;
            ActiveModelId = activeModel.Id;
            ActiveModelProvider = activeModel.Provider;
        }

        Quotas.Clear();
        var selectedGroups = ActiveQuotaSelector.Select(snapshot.QuotaGroups, activeModel);
        var selectedBuckets = selectedGroups
            .SelectMany(group => group.Buckets.Select(bucket => (Group: group, Bucket: bucket)))
            .OrderBy(item => QuotaWindowOrder(item.Bucket))
            .ThenBy(item => item.Bucket.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var item in selectedBuckets)
        {
            var percent = item.Bucket.RemainingFraction * 100;
            Quotas.Add(new QuotaRow(
                item.Group.DisplayName,
                item.Bucket.DisplayName,
                percent,
                $"{percent:0.#}%",
                FormatReset(item.Bucket.ResetTime),
                QuotaBrush(percent)));
        }

        OnPropertyChanged(nameof(QuotaCountText));
        SurfaceText = snapshot.Source
            ?? snapshot.Products.FirstOrDefault(product => product.Running)?.DisplayName
            ?? "Antigravity";
        UpdatedText = snapshot.IsLive
            ? $"Live · {snapshot.UpdatedAt.LocalDateTime:t}"
            : $"Cached · {snapshot.UpdatedAt.LocalDateTime:t}";
        Message = snapshot.Message ?? string.Empty;
        LiveStatusText = snapshot.IsLive ? "Live" : "Cached / offline";
        LiveBadgeForeground = snapshot.IsLive ? BrushFrom("#38D6A2") : BrushFrom("#FFBF69");
        LiveBadgeBackground = snapshot.IsLive ? BrushFrom("#17392F") : BrushFrom("#3B2D1D");

        var firstQuota = Quotas.FirstOrDefault();
        var trayStatus = activeModel is null
            ? $"TwinQuota · {LiveStatusText}"
            : firstQuota is null
                ? $"TwinQuota · {activeModel.DisplayName}"
                : $"TwinQuota · {activeModel.DisplayName} {firstQuota.RemainingText}";
        StatusTextChanged?.Invoke(this, trayStatus);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private static int QuotaWindowOrder(QuotaBucket bucket)
    {
        var identity = $"{bucket.DisplayName} {bucket.Window}";
        if (identity.Contains("5", StringComparison.OrdinalIgnoreCase)
            && identity.Contains("h", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return identity.Contains("week", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static string FormatReset(DateTimeOffset? resetTime)
    {
        if (resetTime is null)
        {
            return "No reset time reported";
        }

        var remaining = resetTime.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return $"Reset due · {resetTime.Value.LocalDateTime:g}";
        }

        var relative = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                : $"{Math.Max(1, remaining.Minutes)}m";
        return $"Resets in {relative} · {resetTime.Value.LocalDateTime:g}";
    }

    private static MediaBrush QuotaBrush(double percent) => percent switch
    {
        < 15 => BrushFrom("#FF7B8B"),
        < 35 => BrushFrom("#FFBF69"),
        _ => BrushFrom("#8B7CFF")
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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public sealed record QuotaRow(
        string GroupName,
        string WindowName,
        double RemainingPercent,
        string RemainingText,
        string ResetText,
        MediaBrush ProgressBrush);
}
