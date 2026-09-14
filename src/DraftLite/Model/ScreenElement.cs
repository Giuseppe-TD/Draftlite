using System;

namespace DraftLite.Model;

/// <summary>Un paragrafo della sceneggiatura: tipo + testo su una riga logica.</summary>
public sealed class ScreenElement
{
    public ElementType Type { get; set; } = ElementType.Action;
    public string Text { get; set; } = string.Empty;

    public ScreenElement() { }

    public ScreenElement(ElementType type, string text)
    {
        Type = type;
        Text = text ?? string.Empty;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public ScreenElement Clone() => new ScreenElement(Type, Text);

    public override string ToString() => Type + ": " + Text;
}
