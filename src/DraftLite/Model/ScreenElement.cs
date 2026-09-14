using System;

namespace DraftLite.Model;

/// <summary>
/// Un paragrafo della sceneggiatura: tipo + testo su una riga logica,
/// piu' i dati che gli stanno attaccati (nota, sinossi, colore scheda, numero di scena).
/// </summary>
public sealed class ScreenElement
{
    public ElementType Type { get; set; } = ElementType.Action;
    public string Text { get; set; } = string.Empty;

    /// <summary>Appunto ancorato all'elemento (ScriptNote). Null se non c'e'.</summary>
    public string Note { get; set; }

    /// <summary>Sinossi della scena, usata dalle schede. Solo per le intestazioni di scena.</summary>
    public string Synopsis { get; set; }

    /// <summary>Colore della scheda scena, in formato #RRGGBB.</summary>
    public string Color { get; set; }

    /// <summary>Numero di scena bloccato (1, 12A, 12B...). Null se la numerazione e' libera.</summary>
    public string Number { get; set; }

    /// <summary>La battuta va affiancata alla precedente (dialogo simultaneo).</summary>
    public bool Dual { get; set; }

    public ScreenElement() { }

    public ScreenElement(ElementType type, string text)
    {
        Type = type;
        Text = text ?? string.Empty;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public ScreenElement Clone() => new ScreenElement(Type, Text)
    {
        Note = Note,
        Synopsis = Synopsis,
        Color = Color,
        Number = Number,
        Dual = Dual
    };

    /// <summary>Metadati (tutto tranne tipo e testo): quello che l'editor deve tenere ancorato.</summary>
    public ElementMeta Meta => ElementMeta.From(this);

    public void ApplyMeta(ElementMeta m)
    {
        Note = m?.Note;
        Synopsis = m?.Synopsis;
        Color = m?.Color;
        Number = m?.Number;
    }

    public override string ToString() => Type + ": " + Text;
}

/// <summary>Dati ancorati a un paragrafo, tenuti a parte perche' non vivono nel testo.</summary>
public sealed class ElementMeta
{
    public string Note;
    public string Synopsis;
    public string Color;
    public string Number;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Note) &&
        string.IsNullOrWhiteSpace(Synopsis) &&
        string.IsNullOrWhiteSpace(Color) &&
        string.IsNullOrWhiteSpace(Number);

    public static ElementMeta From(ScreenElement e)
    {
        if (e == null) return null;
        var m = new ElementMeta { Note = e.Note, Synopsis = e.Synopsis, Color = e.Color, Number = e.Number };
        return m.IsEmpty ? null : m;
    }

    public ElementMeta Clone() => new ElementMeta { Note = Note, Synopsis = Synopsis, Color = Color, Number = Number };
}
