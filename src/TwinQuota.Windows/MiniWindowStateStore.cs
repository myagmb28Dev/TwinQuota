using System.IO;
using System.Text.Json;
using TwinQuota.Core;

namespace TwinQuota.Windows;

internal sealed record SavedMiniWindowState(double Left, double Top, double Width = MiniResizeCalculator.DefaultWidth);

internal sealed class MiniWindowStateStore
{
    private readonly string _path;

    public MiniWindowStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwinQuota",
            "mini-window-state.json");
    }

    public SavedMiniWindowState? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var loaded = JsonSerializer.Deserialize<SavedMiniWindowState>(File.ReadAllText(_path));
            return loaded is null
                ? null
                : loaded with { Width = MiniResizeCalculator.ClampWidth(loaded.Width) };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(double left, double top, double width = MiniResizeCalculator.DefaultWidth)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var clampedWidth = MiniResizeCalculator.ClampWidth(width);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SavedMiniWindowState(left, top, clampedWidth)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
