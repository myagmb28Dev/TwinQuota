using System.Text.Json;

namespace TwinQuota.Core;

public sealed record ActiveModelObservation(
    string ModelId,
    DateTimeOffset ObservedAt);

public sealed class ActiveModelObservationStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ActiveModelObservationStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwinQuota",
            "active-model.json");
    }

    public async Task SaveAsync(ActiveModelObservation observation, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, observation, Options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ActiveModelObservation?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<ActiveModelObservation>(stream, Options, cancellationToken)
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

    public static ActiveModelObservation? ParseHookPayload(string json, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("modelName", out var modelNameElement) ||
                modelNameElement.ValueKind != JsonValueKind.String ||
                modelNameElement.GetString() is not { Length: > 0 } modelName)
            {
                return null;
            }

            return new ActiveModelObservation(modelName, observedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
