using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DraftLite.UI;

/// <summary>
/// Le icone della barra multifunzione, disegnate a runtime dentro una griglia 32x32
/// e poi scalate alla misura richiesta (16 per i pulsanti piccoli, 28 per quelli grandi).
/// Nessun file da distribuire, nessun font di simboli da sperare che ci sia,
/// e seguono il colore del tema: sul tema scuro l'inchiostro diventa chiaro.
/// </summary>
public static class RibbonIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

    /// <summary>
    /// Svuota la cache. Le immagini non si distruggono: qualcuno potrebbe averle
    /// ancora sotto il pennello, e al resto ci pensa il garbage collector.
    /// La chiave contiene gia' i colori, quindi cambiando tema le icone nuove
    /// si affiancano alle vecchie e questa non serve quasi mai.
    /// </summary>
    public static void Clear() => Cache.Clear();

    /// <summary>
    /// Piu' pulsanti diversi possono usare lo stesso disegno: qui i nomi che si
    /// appoggiano a un'icona gia' fatta.
    /// </summary>
    private static string Alias(string key) => key switch
    {
        "saveas" => "save",
        "export2" => "export",
        "stats-pdf" => "stats",
        "numbers2" => "numbers",
        "sceneheading" => "scene",
        "breaks" => "pagebreak",
        "askelement" => "elements",
        null => "default",
        _ => key
    };

    public static Bitmap Get(string key, Color ink, Color accent, int size)
    {
        key = Alias(key);
        var id = key + "|" + ink.ToArgb() + "|" + accent.ToArgb() + "|" + size;
        if (Cache.TryGetValue(id, out var cached)) return cached;

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.ScaleTransform(size / 32f, size / 32f);
            try { Draw(g, key, ink, accent); }
            catch { /* un'icona mancante non deve buttare giu' la finestra */ }
        }

        Cache[id] = bmp;
        return bmp;
    }

    // ------------------------------------------------------------- primitive

    private static Pen P(Color c, float w = 2f) =>
        new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    /// <summary>Foglio di carta: base di mezze icone.</summary>
    private static void Sheet(Graphics g, Color ink, bool fold = true)
    {
        using var pen = P(ink, 1.8f);
        if (fold)
        {
            var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(7, 3), new PointF(19, 3), new PointF(25, 9),
                new PointF(25, 29), new PointF(7, 29)
            });
            path.CloseFigure();
            g.DrawPath(pen, path);
            g.DrawLines(pen, new[] { new PointF(19, 3), new PointF(19, 9), new PointF(25, 9) });
            path.Dispose();
        }
        else g.DrawRectangle(pen, 7, 3, 18, 26);
    }

    private static void TextLines(Graphics g, Color ink, params float[] ys)
    {
        using var pen = P(Color.FromArgb(150, ink), 1.6f);
        foreach (var y in ys) g.DrawLine(pen, 11, y, 21, y);
    }

    /// <summary>Lettera al centro dell'icona: B, I, U, Aa...</summary>
    private static void Glyph(Graphics g, string s, Color c, float em, FontStyle style, float dy = 0f)
    {
        using var font = new Font("Segoe UI", em, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(c);
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(s, font, brush, new RectangleF(0, dy, 32, 32), fmt);
    }

    private static void Arrow(Graphics g, Color c, float x, float y, float dx, float dy, float w = 2f)
    {
        using var pen = P(c, w);
        using var cap = new AdjustableArrowCap(2f, 2.2f, true);
        pen.CustomEndCap = cap;
        g.DrawLine(pen, x, y, x + dx, y + dy);
    }

    /// <summary>Punta di freccia piena: per le frecce curve, dove il pennello non basta.</summary>
    private static void Head(Graphics g, Color c, float cx, float cy, float dx, float dy, float size = 4.5f)
    {
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.01) return;
        float ux = (float)(dx / len), uy = (float)(dy / len);
        float px = -uy, py = ux;

        using var fill = new SolidBrush(c);
        g.FillPolygon(fill, new[]
        {
            new PointF(cx + ux * size, cy + uy * size),
            new PointF(cx + px * size * 0.8f, cy + py * size * 0.8f),
            new PointF(cx - px * size * 0.8f, cy - py * size * 0.8f)
        });
    }

    private static void Page(Graphics g, Color ink, float x, float y, float w, float h, float lw = 1.6f)
    {
        using var pen = P(ink, lw);
        g.DrawRectangle(pen, x, y, w, h);
    }

    // ------------------------------------------------------------- catalogo

    private static void Draw(Graphics g, string key, Color ink, Color accent)
    {
        var soft = Color.FromArgb(160, ink);

        switch (key)
        {
            // ---------------------------------------------------- file
            case "new":
                Sheet(g, ink);
                TextLines(g, ink, 16, 20, 24);
                break;

            case "open":
            {
                using var pen = P(ink, 1.8f);
                g.DrawLines(pen, new[]
                {
                    new PointF(3, 26), new PointF(3, 7), new PointF(12, 7),
                    new PointF(15, 11), new PointF(25, 11)
                });
                using var body = new SolidBrush(Color.FromArgb(55, accent));
                var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(3, 26), new PointF(8, 14), new PointF(30, 14), new PointF(25, 26)
                });
                path.CloseFigure();
                g.FillPath(body, path);
                g.DrawPath(pen, path);
                path.Dispose();
                break;
            }

            case "save":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 5, 5, 22, 22);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 10, 5, 12, 9);
                g.DrawRectangle(pen, 10, 5, 12, 9);
                g.DrawRectangle(pen, 9, 18, 14, 9);
                break;
            }

            case "import":
                Sheet(g, ink, false);
                Arrow(g, accent, 16, 8, 0, 11);
                break;

            case "export":
                Sheet(g, ink, false);
                Arrow(g, accent, 16, 19, 0, -11);
                break;

            case "pdf":
                Sheet(g, ink, false);
                Glyph(g, "PDF", Color.FromArgb(198, 62, 52), 8f, FontStyle.Bold, 3f);
                break;

            case "print":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 4, 12, 24, 11);
                g.DrawRectangle(pen, 9, 4, 14, 8);
                using var fill = new SolidBrush(Color.FromArgb(60, accent));
                g.FillRectangle(fill, 9, 20, 14, 9);
                g.DrawRectangle(pen, 9, 20, 14, 9);
                break;
            }

            // ---------------------------------------------------- appunti
            case "paste":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 6, 6, 20, 23);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 11, 2, 10, 7);
                g.DrawRectangle(pen, 11, 2, 10, 7);
                TextLines(g, ink, 16, 20, 24);
                break;
            }

            case "cut":
            {
                using var pen = P(ink, 1.8f);
                g.DrawLine(pen, 9, 4, 21, 22);
                g.DrawLine(pen, 21, 4, 11, 20);
                g.DrawEllipse(pen, 6, 22, 7, 7);
                g.DrawEllipse(pen, 19, 22, 7, 7);
                break;
            }

            case "copy":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 4, 4, 17, 20);
                using var fill = new SolidBrush(Color.FromArgb(55, accent));
                g.FillRectangle(fill, 11, 9, 17, 20);
                g.DrawRectangle(pen, 11, 9, 17, 20);
                break;
            }

            // ---------------------------------------------------- testo
            case "bold": Glyph(g, "B", ink, 22f, FontStyle.Bold); break;
            case "italic": Glyph(g, "I", ink, 22f, FontStyle.Italic | FontStyle.Bold); break;
            case "underline":
            {
                Glyph(g, "U", ink, 20f, FontStyle.Regular, -2f);
                using var pen = P(ink, 2f);
                g.DrawLine(pen, 9, 27, 23, 27);
                break;
            }
            case "strike":
            {
                Glyph(g, "S", ink, 20f, FontStyle.Regular);
                using var pen = P(ink, 2f);
                g.DrawLine(pen, 7, 16, 25, 16);
                break;
            }
            case "case": Glyph(g, "aA", ink, 16f, FontStyle.Bold); break;

            case "font":
            {
                Glyph(g, "A", ink, 20f, FontStyle.Regular, -3f);
                using var pen = P(soft, 1.6f);
                g.DrawLine(pen, 8, 27, 24, 27);
                break;
            }

            case "textcolor":
            {
                Glyph(g, "A", ink, 18f, FontStyle.Bold, -5f);
                using var fill = new SolidBrush(Color.FromArgb(192, 62, 52));
                g.FillRectangle(fill, 6, 24, 20, 5);
                break;
            }

            case "highlight":
            {
                using var fill = new SolidBrush(Color.FromArgb(150, 244, 226, 90));
                g.FillRectangle(fill, 5, 18, 22, 7);
                using var pen = P(ink, 1.8f);
                g.DrawLines(pen, new[]
                {
                    new PointF(9, 18), new PointF(17, 5), new PointF(24, 9), new PointF(17, 22)
                });
                using var bar = new SolidBrush(Color.FromArgb(214, 190, 40));
                g.FillRectangle(bar, 5, 26, 22, 3);
                break;
            }

            case "revert":
            {
                // freccia circolare antioraria: si torna indietro
                using var pen = P(ink, 2f);
                g.DrawArc(pen, 6, 6, 20, 20, 230, 260);
                Head(g, accent, 8, 9, -1, 1, 5f);
                break;
            }

            // ---------------------------------------------------- elementi
            case "elements":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 4, 5, 24, 22);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 7, 8, 18, 3);
                using var thin = P(soft, 1.5f);
                g.DrawLine(thin, 7, 15, 25, 15);
                g.DrawLine(thin, 11, 19, 21, 19);
                g.DrawLine(thin, 11, 23, 21, 23);
                break;
            }

            case "scene":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 3, 10, 26, 17);
                using var fill = new SolidBrush(Color.FromArgb(75, accent));
                g.FillRectangle(fill, 4, 11, 25, 5);
                g.DrawLine(pen, 3, 10, 29, 5);
                g.DrawLine(pen, 9, 9, 13, 4);
                g.DrawLine(pen, 17, 7, 21, 2);
                break;
            }

            case "dual":
            {
                using var fill = new SolidBrush(Color.FromArgb(85, accent));
                g.FillRectangle(fill, 4, 6, 10, 4);
                g.FillRectangle(fill, 18, 6, 10, 4);
                using var pen = P(soft, 1.6f);
                g.DrawLine(pen, 4, 15, 13, 15);
                g.DrawLine(pen, 4, 20, 13, 20);
                g.DrawLine(pen, 4, 25, 11, 25);
                g.DrawLine(pen, 18, 15, 27, 15);
                g.DrawLine(pen, 18, 20, 27, 20);
                g.DrawLine(pen, 18, 25, 25, 25);
                break;
            }

            case "character":
            {
                using var pen = P(ink, 1.8f);
                g.DrawEllipse(pen, 11, 4, 10, 10);
                g.DrawArc(pen, 6, 16, 20, 20, 190, 160);
                break;
            }

            case "castlist":
            {
                using var pen = P(ink, 1.7f);
                g.DrawEllipse(pen, 4, 5, 8, 8);
                g.DrawArc(pen, 1, 14, 14, 14, 195, 150);
                using var thin = P(soft, 1.6f);
                g.DrawLine(thin, 18, 9, 29, 9);
                g.DrawLine(thin, 18, 16, 29, 16);
                g.DrawLine(thin, 18, 23, 29, 23);
                break;
            }

            case "rename":
            {
                Glyph(g, "Ab", ink, 14f, FontStyle.Regular, -4f);
                Arrow(g, accent, 7, 24, 18, 0);
                break;
            }

            // ---------------------------------------------------- inserisci
            case "note":
            {
                Sheet(g, ink, false);
                using var accentFill = new SolidBrush(Color.FromArgb(214, 160, 60));
                g.FillRectangle(accentFill, 7, 3, 4, 26);
                TextLines(g, ink, 12, 17, 22);
                break;
            }

            case "bookmark":
            {
                using var pen = P(ink, 1.8f);
                var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(9, 3), new PointF(23, 3), new PointF(23, 29),
                    new PointF(16, 22), new PointF(9, 29)
                });
                path.CloseFigure();
                using var fill = new SolidBrush(Color.FromArgb(60, accent));
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                path.Dispose();
                break;
            }

            case "image":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 4, 7, 24, 18);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(5, 24), new PointF(13, 14), new PointF(19, 21),
                    new PointF(22, 18), new PointF(27, 24)
                });
                path.CloseFigure();
                g.FillPath(fill, path);
                g.DrawEllipse(pen, 19, 10, 4, 4);
                path.Dispose();
                break;
            }

            case "pagebreak":
            {
                using var pen = P(soft, 1.7f);
                g.DrawRectangle(pen, 6, 2, 20, 10);
                g.DrawRectangle(pen, 6, 20, 20, 10);
                using var dash = new Pen(accent, 2f) { DashStyle = DashStyle.Dash };
                g.DrawLine(dash, 2, 16, 30, 16);
                break;
            }

            case "symbol": Glyph(g, "§ «", ink, 15f, FontStyle.Regular); break;

            case "titlepage":
            {
                Sheet(g, ink, false);
                using var pen = P(ink, 2.2f);
                g.DrawLine(pen, 11, 11, 21, 11);
                using var thin = P(soft, 1.5f);
                g.DrawLine(thin, 13, 17, 19, 17);
                g.DrawLine(thin, 13, 22, 19, 22);
                break;
            }

            // ---------------------------------------------------- viste
            case "script":
                Sheet(g, ink, false);
                TextLines(g, ink, 9, 13, 17, 21, 25);
                break;

            case "cards":
            {
                using var pen = P(ink, 1.7f);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.DrawRectangle(pen, 2, 7, 13, 18);
                g.FillRectangle(fill, 3, 8, 12, 4);
                g.DrawRectangle(pen, 17, 7, 13, 18);
                g.FillRectangle(fill, 18, 8, 12, 4);
                break;
            }

            case "sceneview":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 3, 5, 26, 22);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 4, 6, 25, 5);
                g.DrawLine(pen, 3, 18, 29, 18);
                g.DrawLine(pen, 12, 11, 12, 27);
                break;
            }

            case "navigator":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 3, 5, 26, 22);
                using var fill = new SolidBrush(Color.FromArgb(75, accent));
                g.FillRectangle(fill, 4, 6, 8, 20);
                g.DrawLine(pen, 12, 5, 12, 27);
                break;
            }

            case "normalview":
                Page(g, ink, 6, 3, 20, 26, 1.8f);
                TextLines(g, ink, 10, 15, 20);
                break;

            case "pageview":
            {
                using var pen = P(soft, 1.6f);
                g.DrawRectangle(pen, 9, 1, 17, 22);
                using var pen2 = P(ink, 1.8f);
                g.DrawRectangle(pen2, 4, 8, 17, 22);
                break;
            }

            case "split-v":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 3, 5, 26, 22);
                using var thick = new Pen(accent, 2.4f);
                g.DrawLine(thick, 16, 5, 16, 27);
                break;
            }

            case "split-h":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 3, 5, 26, 22);
                using var thick = new Pen(accent, 2.4f);
                g.DrawLine(thick, 3, 16, 29, 16);
                break;
            }

            case "unsplit":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 3, 5, 26, 22);
                Arrow(g, accent, 16, 8, 0, 4);
                Arrow(g, accent, 16, 24, 0, -4);
                break;
            }

            case "ruler":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 2, 11, 28, 10);
                using var thin = P(soft, 1.5f);
                for (int x = 7; x < 30; x += 5) g.DrawLine(thin, x, 11, x, 16);
                break;
            }

            case "invisibles": Glyph(g, "¶", ink, 22f, FontStyle.Regular); break;

            case "zoom":
            {
                using var pen = P(ink, 2f);
                g.DrawEllipse(pen, 5, 5, 16, 16);
                g.DrawLine(pen, 20, 20, 28, 28);
                break;
            }

            case "zoomin":
            {
                using var pen = P(ink, 2f);
                g.DrawEllipse(pen, 5, 5, 16, 16);
                g.DrawLine(pen, 20, 20, 28, 28);
                using var plus = P(accent, 2f);
                g.DrawLine(plus, 13, 9, 13, 17);
                g.DrawLine(plus, 9, 13, 17, 13);
                break;
            }

            case "zoomout":
            {
                using var pen = P(ink, 2f);
                g.DrawEllipse(pen, 5, 5, 16, 16);
                g.DrawLine(pen, 20, 20, 28, 28);
                using var minus = P(accent, 2f);
                g.DrawLine(minus, 9, 13, 17, 13);
                break;
            }

            case "typewriter":
            {
                using var pen = P(ink, 1.7f);
                g.DrawRectangle(pen, 3, 13, 26, 13);
                g.DrawRectangle(pen, 9, 4, 14, 9);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 4, 14, 25, 4);
                break;
            }

            case "day":
            {
                using var pen = P(ink, 1.9f);
                g.DrawEllipse(pen, 11, 11, 10, 10);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen,
                        (float)(16 + Math.Cos(a) * 13), (float)(16 + Math.Sin(a) * 13),
                        (float)(16 + Math.Cos(a) * 15.5), (float)(16 + Math.Sin(a) * 15.5));
                }
                break;
            }

            case "night":
            {
                using var pen = P(ink, 1.9f);
                var path = new GraphicsPath();
                path.AddArc(4, 4, 24, 24, 110, 200);
                path.AddArc(11, 6, 20, 20, 300, -190);
                path.CloseFigure();
                g.DrawPath(pen, path);
                path.Dispose();
                break;
            }

            case "midnight":
            {
                using var fill = new SolidBrush(Color.FromArgb(210, ink));
                g.FillEllipse(fill, 7, 9, 18, 18);
                using var star = new SolidBrush(accent);
                g.FillEllipse(star, 22, 3, 3, 3);
                g.FillEllipse(star, 27, 9, 2.5f, 2.5f);
                g.FillEllipse(star, 16, 2, 2.5f, 2.5f);
                break;
            }

            case "appearance":
            {
                using var pen = P(ink, 1.8f);
                g.DrawEllipse(pen, 4, 4, 24, 24);
                using var fill = new SolidBrush(Color.FromArgb(120, accent));
                var path = new GraphicsPath();
                path.AddArc(4, 4, 24, 24, 90, 180);
                path.CloseFigure();
                g.FillPath(fill, path);
                path.Dispose();
                break;
            }

            // ---------------------------------------------------- modifica
            case "undo":
            {
                using var pen = P(ink, 2.2f);
                g.DrawArc(pen, 7, 9, 18, 16, 180, 180);   // arcobaleno da (7,17) a (25,17)
                g.DrawLine(pen, 25, 17, 25, 23);
                Head(g, ink, 7, 18, 0, 1, 5f);
                break;
            }

            case "redo":
            {
                using var pen = P(ink, 2.2f);
                g.DrawArc(pen, 7, 9, 18, 16, 180, 180);
                g.DrawLine(pen, 7, 17, 7, 23);
                Head(g, ink, 25, 18, 0, 1, 5f);
                break;
            }

            case "delete":
            {
                using var pen = P(ink, 1.9f);
                g.DrawLines(pen, new[]
                {
                    new PointF(6, 8), new PointF(8, 28), new PointF(24, 28), new PointF(26, 8)
                });
                g.DrawLine(pen, 3, 8, 29, 8);
                g.DrawLine(pen, 12, 4, 20, 4);
                using var thin = P(soft, 1.6f);
                g.DrawLine(thin, 13, 13, 13, 23);
                g.DrawLine(thin, 19, 13, 19, 23);
                break;
            }

            case "selectall":
            {
                using var dash = new Pen(ink, 1.8f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(dash, 3, 5, 26, 22);
                using var fill = new SolidBrush(Color.FromArgb(60, accent));
                g.FillRectangle(fill, 4, 6, 25, 21);
                TextLines(g, ink, 12, 17, 22);
                break;
            }

            case "selectscene":
            {
                using var dash = new Pen(accent, 1.8f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(dash, 3, 3, 26, 14);
                using var fill = new SolidBrush(Color.FromArgb(60, accent));
                g.FillRectangle(fill, 4, 4, 25, 13);
                using var thin = P(soft, 1.6f);
                g.DrawLine(thin, 6, 8, 26, 8);
                g.DrawLine(thin, 6, 13, 20, 13);
                g.DrawLine(thin, 6, 23, 26, 23);
                g.DrawLine(thin, 6, 28, 20, 28);
                break;
            }

            case "find":
            {
                using var pen = P(ink, 2.1f);
                g.DrawEllipse(pen, 4, 4, 17, 17);
                g.DrawLine(pen, 20, 20, 28, 28);
                break;
            }

            case "goto":
            {
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 4, 4, 24, 24);
                Arrow(g, accent, 9, 16, 12, 0, 2.2f);
                break;
            }

            case "track":
            {
                using var pen = P(soft, 1.7f);
                g.DrawLine(pen, 4, 9, 28, 9);
                using var strike = P(Color.FromArgb(198, 62, 52), 1.9f);
                g.DrawLine(strike, 4, 17, 20, 17);
                g.DrawLine(strike, 4, 17, 20, 17);
                using var add = P(Color.FromArgb(46, 140, 80), 1.9f);
                g.DrawLine(add, 4, 25, 24, 25);
                break;
            }

            // ---------------------------------------------------- strumenti
            case "stats":
            {
                using var brush = new SolidBrush(accent);
                using var axis = P(soft, 1.7f);
                g.DrawLine(axis, 4, 28, 28, 28);
                g.FillRectangle(brush, 6, 18, 5, 10);
                g.FillRectangle(brush, 14, 10, 5, 18);
                g.FillRectangle(brush, 22, 4, 5, 24);
                break;
            }

            case "revision":
            {
                Sheet(g, ink, false);
                using var star = new SolidBrush(Color.FromArgb(198, 62, 52));
                using var f = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
                g.DrawString("*", f, star, 22, 2);
                TextLines(g, ink, 13, 18, 23);
                break;
            }

            case "numbers":
            {
                using var pen = P(soft, 1.6f);
                g.DrawLine(pen, 14, 8, 29, 8);
                g.DrawLine(pen, 14, 16, 29, 16);
                g.DrawLine(pen, 14, 24, 29, 24);

                using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(ink);
                using var fmt = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("1", font, brush, new RectangleF(2, 2, 11, 12), fmt);
                g.DrawString("2", font, brush, new RectangleF(2, 10, 11, 12), fmt);
                g.DrawString("3", font, brush, new RectangleF(2, 18, 11, 12), fmt);
                break;
            }

            case "sides":
            {
                using var pen = P(soft, 1.7f);
                g.DrawRectangle(pen, 3, 6, 15, 22);
                using var pen2 = P(ink, 1.8f);
                g.DrawRectangle(pen2, 12, 3, 15, 22);
                using var fill = new SolidBrush(Color.FromArgb(70, accent));
                g.FillRectangle(fill, 15, 8, 9, 3);
                break;
            }

            case "help": Glyph(g, "?", ink, 24f, FontStyle.Bold); break;

            case "update":
            {
                using var pen = P(ink, 2f);
                g.DrawArc(pen, 6, 6, 20, 20, 50, 260);
                Head(g, accent, 24, 9, 1, -1, 5f);
                break;
            }

            case "settings":
            {
                // tre cursori: si capisce subito che sono impostazioni
                using var pen = P(soft, 1.7f);
                using var knob = new SolidBrush(accent);
                g.DrawLine(pen, 3, 8, 29, 8);
                g.DrawLine(pen, 3, 16, 29, 16);
                g.DrawLine(pen, 3, 24, 29, 24);
                g.FillEllipse(knob, 18, 5, 7, 7);
                g.FillEllipse(knob, 7, 13, 7, 7);
                g.FillEllipse(knob, 20, 21, 7, 7);
                break;
            }

            // ---------------------------------------------------- temi
            case "paper":
            {
                using var fill = new SolidBrush(Color.FromArgb(90, accent));
                g.FillRectangle(fill, 7, 4, 18, 24);
                using var pen = P(ink, 1.8f);
                g.DrawRectangle(pen, 7, 4, 18, 24);
                TextLines(g, ink, 11, 16, 21);
                break;
            }

            case "sepia":
            {
                using var pen = P(ink, 1.8f);
                g.DrawEllipse(pen, 5, 5, 22, 22);
                using var fill = new SolidBrush(Color.FromArgb(150, accent));
                var path = new GraphicsPath();
                path.AddArc(5, 5, 22, 22, 270, 180);
                path.CloseFigure();
                g.FillPath(fill, path);
                path.Dispose();
                break;
            }

            // ---------------------------------------------------- paragrafo
            case "align-left":
            case "align-center":
            case "align-right":
            {
                using var pen = P(ink, 1.9f);
                for (int i = 0; i < 4; i++)
                {
                    float y = 7 + i * 6;
                    float w = (i % 2 == 0) ? 24 : 15;
                    float x = key == "align-left" ? 4 : key == "align-right" ? 28 - w : 16 - w / 2;
                    g.DrawLine(pen, x, y, x + w, y);
                }
                break;
            }

            case "spacing":
            {
                using var pen = P(soft, 1.8f);
                g.DrawLine(pen, 12, 7, 28, 7);
                g.DrawLine(pen, 12, 16, 28, 16);
                g.DrawLine(pen, 12, 25, 28, 25);
                Arrow(g, accent, 6, 7, 0, 18);
                Arrow(g, accent, 6, 25, 0, -18);
                break;
            }

            default:
                Glyph(g, "•", ink, 20f, FontStyle.Bold);
                break;
        }
    }
}
