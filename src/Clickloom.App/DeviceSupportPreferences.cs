using System.IO;
using System.Text.Json;

namespace Clickloom.App;

internal static class DeviceSupportPreferences
{
    private sealed record Preferences(string LibraryPath);
    public static string? Load(string folder)
    {
        try
        {
            string path = Path.Combine(folder, "device-input.json");
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
            return value is not null && Path.IsPathFullyQualified(value.LibraryPath) ? value.LibraryPath : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public static void Save(string folder, string library)
    {
        if (!Path.IsPathFullyQualified(library) || !File.Exists(library)) throw new ArgumentException("Choose an installed x64 interception.dll.");
        using (var stream = File.OpenRead(library))
        using (var reader = new BinaryReader(stream))
        {
            if (reader.ReadUInt16() != 0x5A4D || stream.Length < 64) throw new InvalidDataException("This is not a Windows library.");
            stream.Position = 0x3C; int offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 6) throw new InvalidDataException("Invalid library header.");
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Clickloom requires the x64 library.");
        }
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "device-input.json"), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(library))); File.Move(temporary, path, true);
    }
}
