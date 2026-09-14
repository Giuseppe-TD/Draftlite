using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>
/// Il righello in pollici sopra il foglio, come nei programmi di scrittura:
/// la striscia chiara e' l'area di testo (6 pollici), le tacche stanno ogni mezzo pollice
/// e i due indicatori mostrano dove comincia e dove finisce l'elemento sotto il cursore
/// (il dialogo rientra, il personaggio sta piu' in dentro, l'azione tiene tutta la riga).
/// </summary>
public sealed class RulerStrip : Control
{
    private Theme _theme = Theme.Paper1;
    private readonly Font _numberFont = new Font("Segoe UI", 6.75f);

    /// <summary>Bordo sinistro del foglio, in pixel, dentro questo controllo.</summary>
    public int PageLeft { get; set; }
    /// <summary>Larghezza del foglio in pixel (compresi i margini disegnati).</summary>
    public int PageWidth { get; set; }
    /// <summary>Bordo sinistro dell'area di testo, in pixel.</summary>
    public int TextLeft { get; set; }
    /// <summary>Pixel per pollice, zoom compreso.</summary>
    public float PixelsPerInch { get; set; } = 96f;

    public double MarkerLeftInch { get; set; }
    public double MarkerRightInch { get; set; } = 6.0;

    public RulerStrip()
    {
        // la posizione la decide MainForm.LayoutPage: deve stare esattamente
        // sopra al foglio, non su tutta la scrivania
        Height = 20;
        TabStop = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _numberFont?.Dispose();
        base.Dispose(disposing);
    }

    public void ApplyTheme(Theme t)
    {
        _theme = t;
        BackColor = t.Desk;
        Invalidate();
    }

    /// <summary>Aggiorna righello e indicatori in un colpo solo.</summary>
    public void Update(int pageLeft, int pageWidth, int textLeft, float scale, double leftInch, double rightInch)
    {
        PageLeft = pageLeft;
        PageWidth = pageWidth;
        TextLeft = textLeft;
        PixelsPerInch = Math.Max(24f, scale);
        MarkerLeftInch = leftInch;
        MarkerRightInch = rightInch;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(_theme.Desk);

        if (PageWidth <= 0) return;

        int top = 4;
        int h = Height - top - 4;

        // fondo della pagina
        using (var paper = new SolidBrush(_theme.Paper))
            g.FillRectangle(paper, PageLeft, top, PageWidth, h);

        // margini (fuori dall'area di testo) leggermente piu' scuri
        float textRight = TextLeft + 6f * PixelsPerInch;
        using (var margin = new SolidBrush(Color.FromArgb(60, _theme.Rule)))
        {
            if (TextLeft > PageLeft) g.FillRectangle(margin, PageLeft, top, TextLeft - PageLeft, h);
            if (PageLeft + PageWidth > textRight)
                g.FillRectangle(margin, textRight, top, PageLeft + PageWidth - textRight, h);
        }

        using (var border = new Pen(_theme.Rule))
            g.DrawRectangle(border, PageLeft, top, PageWidth - 1, h - 1);

        // tacche: numero a ogni pollice, trattino a ogni mezzo
        using (var tick = new Pen(Color.FromArgb(150, _theme.Faint)))
        using (var brush = new SolidBrush(_theme.Faint))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            for (int half = 0; half <= 12; half++)
            {
                float x = TextLeft + half * PixelsPerInch / 2f;
                if (x < PageLeft || x > PageLeft + PageWidth) continue;

                if (half % 2 == 0)
                {
                    int inch = half / 2;
                    if (inch > 0 && inch < 6)
                        g.DrawString(inch.ToString(), _numberFont, brush,
                            new RectangleF(x - 8, top, 16, h), fmt);
                    else
                        g.DrawLine(tick, x, top + 2, x, top + h - 2);
                }
                else g.DrawLine(tick, x, top + h / 2f - 2, x, top + h / 2f + 2);
            }
        }

        // indicatori del rientro dell'elemento corrente
        float left = TextLeft + (float)MarkerLeftInch * PixelsPerInch;
        float right = TextLeft + (float)MarkerRightInch * PixelsPerInch;
        using (var fill = new SolidBrush(Color.FromArgb(70, _theme.Accent)))
            g.FillRectangle(fill, left, top + 1, Math.Max(1, right - left), h - 2);

        Marker(g, left, top, h, true);
        Marker(g, right, top, h, false);
    }

    private void Marker(Graphics g, float x, int top, int h, bool pointsRight)
    {
        using var fill = new SolidBrush(_theme.Accent);
        float d = pointsRight ? 1f : -1f;
        var pts = new[]
        {
            new PointF(x, top + h - 1),
            new PointF(x, top + h - 7),
            new PointF(x + d * 5, top + h - 1)
        };
        g.FillPolygon(fill, pts);
    }
}
