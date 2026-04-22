using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CUE4Parse.UE4.Versions;

namespace UEpaker;

public class ProviderSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UEpaker", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string PakDirectory { get; set; } = string.Empty;
    public string AesKey { get; set; } = string.Empty;
    public string MappingsPath { get; set; } = string.Empty;
    public EGame UeVersion { get; set; } = EGame.GAME_UE5_6;

    public static ProviderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ProviderSettings>(json, JsonOptions)
                       ?? new ProviderSettings();
            }
        }
        catch { /* corrupt settings — start fresh */ }
        return new ProviderSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* best-effort */ }
    }
}
