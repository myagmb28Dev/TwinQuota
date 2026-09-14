using System.IO;
using System.Text.Json;

namespace TwinQuota.Windows;

internal sealed record SavedMiniWindowState(double Left, double Top);

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
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<SavedMiniWindowState>(File.ReadAllText(_path))
                : null;
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

    public void Save(double left, double top)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SavedMiniWindowState(left, top)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
