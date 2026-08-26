using System.IO;
using System.Text.Json;

namespace TwinQuota.Windows;

internal sealed record SavedWindowSize(double Width, double Height);

internal sealed class WindowSizeStore
{
    private readonly string _path;

    public WindowSizeStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwinQuota",
            "window-state.json");
    }

    public SavedWindowSize? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<SavedWindowSize>(File.ReadAllText(_path))
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

    public void Save(double width, double height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SavedWindowSize(width, height)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
