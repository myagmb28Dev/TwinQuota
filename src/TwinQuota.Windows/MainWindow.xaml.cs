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
    private string _updatedText = "Not refreshed yet";
    private string _message = "Looking for Antigravity…";
    private string _liveStatusText = "Checking";
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

    public ObservableCollection<ProductRow> Products { get; } = [];
    public ObservableCollection<QuotaRow> Quotas { get; } = [];
    public ObservableCollection<ModelRow> Models { get; } = [];

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

    public string ModelCountText => Models.Count == 1 ? "1 model" : $"{Models.Count} models";

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
        RefreshButton.Content = "Refreshing…";
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
            RefreshButton.Content = "Refresh";
        }
    }

    private void ApplySnapshot(TwinQuotaSnapshot snapshot)
    {
        Products.Clear();
        foreach (var product in snapshot.Products)
        {
            Products.Add(new ProductRow(
                product.DisplayName,
                string.IsNullOrWhiteSpace(product.Version) ? "Version unknown" : $"v{product.Version}",
                product.Detail,
                product.Running ? BrushFrom("#38D6A2") : product.Installed ? BrushFrom("#FFBF69") : BrushFrom("#66718C")));
        }

        Quotas.Clear();
        foreach (var group in snapshot.QuotaGroups)
        {
            foreach (var bucket in group.Buckets)
            {
                var percent = bucket.RemainingFraction * 100;
                Quotas.Add(new QuotaRow(
                    group.DisplayName,
                    bucket.DisplayName,
                    percent,
                    $"{percent:0.#}%",
                    FormatReset(bucket.ResetTime),
                    QuotaBrush(percent)));
            }
        }

        Models.Clear();
        foreach (var model in snapshot.Models)
        {
            var percent = model.RemainingFraction is null ? (double?)null : model.RemainingFraction.Value * 100;
            Models.Add(new ModelRow(
                model.Id,
                model.DisplayName,
                model.Provider,
                percent is null ? "Available" : $"{percent:0.#}%",
                FormatReset(model.ResetTime),
                percent is null ? BrushFrom("#C9C2FF") : QuotaBrush(percent.Value)));
        }

        OnPropertyChanged(nameof(ModelCountText));
        UpdatedText = snapshot.IsLive
            ? $"Live · {snapshot.UpdatedAt.LocalDateTime:t} · {snapshot.Source}"
            : $"Cached · {snapshot.UpdatedAt.LocalDateTime:g}";
        Message = snapshot.Message ?? string.Empty;
        LiveStatusText = snapshot.IsLive ? "Live" : "Cached / offline";
        LiveBadgeForeground = snapshot.IsLive ? BrushFrom("#38D6A2") : BrushFrom("#FFBF69");
        LiveBadgeBackground = snapshot.IsLive ? BrushFrom("#17392F") : BrushFrom("#3B2D1D");

        var firstQuota = Quotas.FirstOrDefault();
        var trayStatus = firstQuota is null
            ? $"TwinQuota · {LiveStatusText}"
            : $"TwinQuota · {firstQuota.GroupName} {firstQuota.RemainingText}";
        StatusTextChanged?.Invoke(this, trayStatus);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

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

    public sealed record ProductRow(string DisplayName, string VersionText, string Detail, MediaBrush StatusBrush);
    public sealed record QuotaRow(
        string GroupName,
        string WindowName,
        double RemainingPercent,
        string RemainingText,
        string ResetText,
        MediaBrush ProgressBrush);
    public sealed record ModelRow(
        string Id,
        string DisplayName,
        string Provider,
        string RemainingText,
        string ResetText,
        MediaBrush RemainingBrush);
}
