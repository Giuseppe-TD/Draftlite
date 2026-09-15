using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftLite.Model;

/// <summary>Geometria del documento in pollici, indipendente dallo zoom.</summary>
public sealed class DocumentLayout
{
    public double PaperWidth { get; set; } = 8.5;
    public double PaperHeight { get; set; } = 11;
    public double LeftMargin { get; set; } = 1.5;
    public double RightMargin { get; set; } = 1;
    public double TopMargin { get; set; } = 1;
    public double BottomMargin { get; set; } = 1;
    public Dictionary<ElementType, ParagraphLayout> Paragraphs { get; set; } = new();
    public double TextWidth => PaperWidth - LeftMargin - RightMargin;

    public void Normalize()
    {
        PaperWidth = Valid(PaperWidth, 5, 20, 8.5);
        PaperHeight = Valid(PaperHeight, 5, 24, 11);
        LeftMargin = Valid(LeftMargin, 0, PaperWidth - 2, 1.5);
        RightMargin = Valid(RightMargin, 0, PaperWidth - LeftMargin - 1, 0.5);
        TopMargin = Valid(TopMargin, 0, PaperHeight - 2, 1);
        BottomMargin = Valid(BottomMargin, 0, PaperHeight - TopMargin - 1, 0.5);
        Paragraphs ??= new();
        foreach (var key in Paragraphs.Keys.ToList())
        {
            var p = Paragraphs[key];
            if (p == null || !double.IsFinite(p.Left) || !double.IsFinite(p.Right) ||
                p.Left < 0 || p.Right > PaperWidth || p.Right - p.Left < 0.1)
                Paragraphs.Remove(key);
        }
    }
    private static double Valid(double n, double min, double max, double fallback)
        => double.IsFinite(n) && n >= min && n <= max ? n : fallback;
    public DocumentLayout Clone() => new()
    {
        PaperWidth = PaperWidth, PaperHeight = PaperHeight,
        LeftMargin = LeftMargin, RightMargin = RightMargin,
        TopMargin = TopMargin, BottomMargin = BottomMargin,
        Paragraphs = Paragraphs.ToDictionary(p => p.Key, p => p.Value.Clone())
    };
    public ElementStyle Style(ElementType type)
    {
        var original = ElementStyle.Get(type);
        if (Paragraphs.TryGetValue(type, out var p))
            return original.WithGeometry(p.Left - LeftMargin, p.Right - p.Left, TextWidth);
        double left = original.LeftInch * TextWidth / ElementStyle.TextWidthInch;
        double width = original.WidthInch * TextWidth / ElementStyle.TextWidthInch;
        return original.WithGeometry(left, width, TextWidth);
    }
}

public sealed class ParagraphLayout
{
    public double Left { get; set; }
    public double Right { get; set; }
    public ParagraphLayout Clone() => (ParagraphLayout)MemberwiseClone();
}
