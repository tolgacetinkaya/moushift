using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clickloom.Core;

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

public sealed class SettingsStore
{
    public const long MaximumFileBytes = 2 * 1024 * 1024;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public SettingsLoadResult Load()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return new(AppSettings.CreateDefault(), null);
            try { return new(ReadValidated(FilePath), null); }
            catch (Exception error) when (IsStorageError(error))
            {
                return new(AppSettings.CreateDefault(),
                    $"Could not load settings; the file was preserved. Safe defaults were loaded. {error.Message}");
            }
        }
    }

    public void Save(AppSettings settings)
    {
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        if (bytes.LongLength > MaximumFileBytes) throw new ArgumentException("Settings file exceeds the size limit.", nameof(settings));
        lock (gate)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes);
                    output.Flush(flushToDisk: true);
                }
                if (!File.Exists(FilePath)) File.Move(temporary, FilePath);
                else
                {
                    bool validOriginal;
                    try { ReadValidated(FilePath); validOriginal = true; }
                    catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
                    { validOriginal = false; }

                    if (validOriginal) File.Replace(temporary, FilePath, FilePath + ".bak");
                    else
                    {
                        // Keep both the corrupt bytes and the last-known-good .bak.
                        // A user must explicitly save before the original is replaced.
                        var corruptCopy = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
                        File.Copy(FilePath, corruptCopy, overwrite: false);
                        File.Replace(temporary, FilePath, destinationBackupFileName: null);
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static AppSettings ReadValidated(string filePath)
    {
        using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaximumFileBytes) throw new InvalidDataException("Settings file exceeds the size limit.");
        var settings = JsonSerializer.Deserialize<AppSettings>(input, JsonOptions)
            ?? throw new InvalidDataException("Settings file is empty.");
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
        return settings;
    }

    private static bool IsStorageError(Exception error) => error is IOException or InvalidDataException
        or UnauthorizedAccessException or JsonException or NotSupportedException;
}
