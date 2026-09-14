using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public bool RevisionMarks { get; set; } = true;
    public bool PdfTextColors { get; set; } = false;
    public string Watermark { get; set; } = string.Empty;
    public string CopyFor { get; set; } = string.Empty;

    public bool ShowNavigator { get; set; } = true;
    public float Zoom { get; set; } = 1.0f;
    public string LastFolder { get; set; } = string.Empty;
    public bool AutoSave { get; set; } = true;

    public string FontFamily { get; set; } = "Courier New";
    public string ThemeName { get; set; } = "Carta";
    public bool Typewriter { get; set; } = false;

    /// <summary>Colori scelti a mano per i tipi di elemento: nome tipo -> #RRGGBB.</summary>
    public Dictionary<string, string> ElementColors { get; set; } = new Dictionary<string, string>();

    /// <summary>Menu degli elementi quando si va a capo.</summary>
    public bool AskElementOnEnter { get; set; } = true;
    /// <summary>Numero di pagina e linea di stacco nel margine del foglio.</summary>
    public bool ShowPageBreaks { get; set; } = true;

    /// <summary>Quante copie datate tenere accanto al file a ogni salvataggio.</summary>
    public int BackupCount { get; set; } = 10;

    /// <summary>Controllo aggiornamenti all'avvio (una volta al giorno, senza scaricare nulla).</summary>
    public bool CheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
    public string SkippedVersion { get; set; } = string.Empty;

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
        ps.RevisionMarks = RevisionMarks;
        ps.TextColors = PdfTextColors;
        ps.Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark;
        ps.CopyFor = string.IsNullOrWhiteSpace(CopyFor) ? null : CopyFor;
        return ps;
    }

    public void FromPageSetup(IO.PageSetup ps)
    {
        Paper = ps.PaperName;
        SceneNumbers = ps.SceneNumbers;
        PageNumbers = ps.PageNumbers;
        IncludeTitlePage = ps.IncludeTitlePage;
        RevisionMarks = ps.RevisionMarks;
        PdfTextColors = ps.TextColors;
        Watermark = ps.Watermark ?? string.Empty;
        CopyFor = ps.CopyFor ?? string.Empty;
    }

    /// <summary>
    /// Copia datata del file prima di sovrascriverlo, in una sottocartella accanto all'originale.
    /// Restano le ultime BackupCount: se rovini una scena, torni indietro di un'ora.
    /// </summary>
    public void MakeBackup(string path)
    {
        try
        {
            if (BackupCount <= 0 || !File.Exists(path)) return;

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;

            var backupDir = Path.Combine(dir, "Versioni DraftLite");
            Directory.CreateDirectory(backupDir);

            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, Path.Combine(backupDir, name + "-" + stamp + ext), true);

            var old = Directory.GetFiles(backupDir, name + "-*" + ext)
                               .OrderByDescending(f => f)
                               .Skip(BackupCount)
                               .ToList();
            foreach (var f in old)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { /* il backup non deve mai impedire il salvataggio */ }
    }
}
