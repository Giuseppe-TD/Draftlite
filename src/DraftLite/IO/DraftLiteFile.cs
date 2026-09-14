using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Formato nativo .dlite: JSON leggibile, versionabile con git, facile da rigenerare
/// a mano se un giorno serve. Nessun binario, nessuna sorpresa.
/// </summary>
public static class DraftLiteFile
{
    public const string Extension = ".dlite";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class Dto
    {
        public string Format { get; set; } = "draftlite";
        public int Version { get; set; } = 1;
        public string Generator { get; set; } = "DraftLite";
        public TitlePage TitlePage { get; set; } = new TitlePage();
        public System.Collections.Generic.List<ScreenElement> Elements { get; set; } = new();
    }

    public static void Save(string path, Screenplay sp)
    {
        var dto = new Dto
        {
            TitlePage = sp.TitlePage,
            Elements = sp.Elements
        };
        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static Screenplay Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var dto = JsonSerializer.Deserialize<Dto>(json, Options);
        if (dto == null) throw new InvalidDataException("File .dlite non valido o vuoto.");
        var sp = new Screenplay
        {
            TitlePage = dto.TitlePage ?? new TitlePage(),
            Elements = dto.Elements ?? new System.Collections.Generic.List<ScreenElement>()
        };
        foreach (var e in sp.Elements)
            e.Text ??= string.Empty;
        return sp;
    }
}
