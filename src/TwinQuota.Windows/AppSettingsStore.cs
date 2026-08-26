using System.IO;
using System.Text.Json;

namespace TwinQuota.Windows;

internal sealed record AppSettings(bool ShowOnlyWhenAntigravityWindowIsOpen)
{
    public static AppSettings Default { get; } = new(false);
}

internal sealed class AppSettingsStore
{
    private readonly string _path;

    public AppSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwinQuota",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? AppSettings.Default
                : AppSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
