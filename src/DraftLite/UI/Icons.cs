using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>
/// Icone della barra strumenti disegnate a runtime: nessun file da distribuire,
/// nessun font di simboli da sperare che ci sia, e seguono il colore del tema.
/// </summary>
public static class Icons
{
    private const int S = 16;

    private static Bitmap New(out Graphics g)
    {
        var bmp = new Bitmap(S, S);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        return bmp;
    }

    /// <summary>Foglio con l'angolo piegato, base di quasi tutte le icone.</summary>
    private static void Sheet(Graphics g, Color ink, bool fold = true)
    {
        using var pen = new Pen(ink, 1.2f);
        var r = new Rectangle(3, 1, 10, 13);
        g.DrawRectangle(pen, r);
        if (fold)
        {
            g.DrawLine(pen, 9, 1, 13, 5);
            g.DrawLine(pen, 9, 1, 9, 5);
            g.DrawLine(pen, 9, 5, 13, 5);
        }
    }

    private static void Lines(Graphics g, Color ink, params int[] ys)
    {
        using var pen = new Pen(Color.FromArgb(170, ink), 1f);
        foreach (var y in ys) g.DrawLine(pen, 5, y, 11, y);
    }

    public static Bitmap Cards(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        using (var pen = new Pen(ink, 1.2f))
        {
            g.DrawRectangle(pen, 1, 3, 6, 9);
            g.DrawRectangle(pen, 9, 3, 6, 9);
            using var fill = new SolidBrush(Color.FromArgb(70, ink));
            g.FillRectangle(fill, 2, 4, 5, 2);
            g.FillRectangle(fill, 10, 4, 5, 2);
        }
        return bmp;
    }

    public static Bitmap Note(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        {
            Sheet(g, ink);
            Lines(g, ink, 8, 10, 12);
            using var accent = new SolidBrush(Color.FromArgb(220, 190, 110, 40));
            g.FillRectangle(accent, 3, 1, 2, 13);
        }
        return bmp;
    }

    public static Bitmap TitlePage(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        {
            Sheet(g, ink, false);
            using var pen = new Pen(ink, 1.4f);
            g.DrawLine(pen, 5, 5, 11, 5);
            using var thin = new Pen(Color.FromArgb(150, ink), 1f);
            g.DrawLine(thin, 6, 8, 10, 8);
            g.DrawLine(thin, 6, 11, 10, 11);
        }
        return bmp;
    }

    public static Bitmap Stats(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        using (var brush = new SolidBrush(ink))
        using (var axis = new Pen(Color.FromArgb(150, ink), 1f))
        {
            g.DrawLine(axis, 2, 14, 14, 14);
            g.FillRectangle(brush, 3, 9, 3, 5);
            g.FillRectangle(brush, 7, 5, 3, 9);
            g.FillRectangle(brush, 11, 2, 3, 12);
        }
        return bmp;
    }

    public static Bitmap Pdf(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        {
            Sheet(g, ink, false);
            using var arrow = new Pen(Color.FromArgb(200, 60, 60), 1.6f);
            g.DrawLine(arrow, 8, 6, 8, 12);
            g.DrawLine(arrow, 5, 9, 8, 12);
            g.DrawLine(arrow, 11, 9, 8, 12);
        }
        return bmp;
    }

    public static Bitmap Scene(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        using (var pen = new Pen(ink, 1.2f))
        using (var fill = new SolidBrush(Color.FromArgb(60, ink)))
        {
            g.DrawRectangle(pen, 1, 4, 14, 8);
            g.FillRectangle(fill, 2, 5, 13, 2);
            g.DrawLine(pen, 1, 4, 15, 2);
        }
        return bmp;
    }

    /// <summary>Lettera stilizzata: serve per grassetto, corsivo e sottolineato.</summary>
    public static Bitmap Letter(string ch, Color ink, FontStyle style)
    {
        var bmp = New(out var g);
        using (g)
        using (var font = new Font("Georgia", 10f, style, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(ink))
        {
            var size = g.MeasureString(ch, font);
            g.DrawString(ch, font, brush, (S - size.Width) / 2f, (S - size.Height) / 2f);
            if (style.HasFlag(FontStyle.Underline)) { }
        }
        return bmp;
    }

    public static Bitmap Dual(Color ink)
    {
        var bmp = New(out var g);
        using (g)
        using (var pen = new Pen(Color.FromArgb(180, ink), 1f))
        using (var fill = new SolidBrush(Color.FromArgb(90, ink)))
        {
            g.FillRectangle(fill, 2, 3, 5, 2);
            g.FillRectangle(fill, 9, 3, 5, 2);
            g.DrawLine(pen, 2, 7, 6, 7);
            g.DrawLine(pen, 2, 10, 6, 10);
            g.DrawLine(pen, 9, 7, 13, 7);
            g.DrawLine(pen, 9, 10, 13, 10);
        }
        return bmp;
    }
}
