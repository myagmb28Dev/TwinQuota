using System.Text.Json;

namespace TwinQuota.Core;

public sealed class SnapshotCache
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public SnapshotCache(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwinQuota",
            "snapshot.json");
    }

    public async Task SaveAsync(TwinQuotaSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _path, true);
    }

    public async Task<TwinQuotaSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<TwinQuotaSnapshot>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
