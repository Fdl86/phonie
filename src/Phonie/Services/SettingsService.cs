using System.IO;
using System.Text.Json;
using Phonie.Models;

namespace Phonie.Services;

public sealed class SettingsService
{
    private readonly string settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PHONIE");

    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true,
    };

    public string SettingsPath => Path.Combine(this.settingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(this.SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(this.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, this.serializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(this.settingsDirectory);
        var temporaryPath = this.SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, this.serializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, this.SettingsPath, true);
    }
}
