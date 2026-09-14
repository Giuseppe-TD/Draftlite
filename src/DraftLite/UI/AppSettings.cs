using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DraftLite.UI;

/// <summary>Preferenze locali, salvate in %APPDATA%\DraftLite\settings.json.</summary>
public sealed class AppSettings
{
    public string Paper { get; set; } = "A4";
    public bool SceneNumbers { get; set; } = false;
    public bool PageNumbers { get; set; } = true;
    public bool IncludeTitlePage { get; set; } = true;
    public bool ShowNavigator { get; set; } = true;
    public float Zoom { get; set; } = 1.0f;
    public string LastFolder { get; set; } = string.Empty;
    public bool AutoSave { get; set; } = true;

    public static string Folder
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DraftLite");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath => Path.Combine(Folder, "settings.json");
    public static string AutoSavePath => Path.Combine(Folder, "autosave.dlite");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* preferenze corrotte: si riparte dai default */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { /* niente di grave */ }
    }

    public IO.PageSetup ToPageSetup()
    {
        var ps = Paper == "Letter" ? IO.PageSetup.Letter() : IO.PageSetup.A4();
        ps.SceneNumbers = SceneNumbers;
        ps.PageNumbers = PageNumbers;
        ps.IncludeTitlePage = IncludeTitlePage;
        return ps;
    }
}
