using System;
using System.Collections.Generic;
using System.IO;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Export PDF in formato standard di sceneggiatura: Courier 12, 55 righe per pagina,
/// numeri di pagina in alto a destra, frontespizio opzionale.
/// </summary>
public static class PdfExporter
{
    private const double CharW = ElementStyle.CharWidthPt;   // 7.2 pt = 10 cpi
    private const double LineH = ElementStyle.LineHeightPt;  // 12 pt

    public static void Export(string path, Screenplay sp, PageSetup setup)
    {
        var bytes = Build(sp, setup);
        File.WriteAllBytes(path, bytes);
    }

    public static byte[] Build(Screenplay sp, PageSetup setup)
    {
        var pdf = new PdfBuilder(setup.WidthPt, setup.HeightPt)
        {
            Title = sp.TitlePage.Title,
            Author = sp.TitlePage.Author
        };

        double left = setup.LeftMarginInch * 72.0;
        double top = setup.TopMarginInch * 72.0;
        double rightEdge = left + ElementStyle.TextWidthInch * 72.0;

        if (setup.IncludeTitlePage && !sp.TitlePage.IsEmpty)
            DrawTitlePage(pdf, sp.TitlePage, setup);

        var pages = Paginator.Paginate(sp, setup);

        for (int p = 0; p < pages.Count; p++)
        {
            pdf.BeginPage();

            if (setup.PageNumbers && p > 0)
            {
                var num = (p + 1) + ".";
                pdf.DrawText(rightEdge - num.Length * CharW, 0.5 * 72.0 + LineH, num);
            }

            foreach (var line in pages[p].Lines)
            {
                double y = top + (line.Row + 1) * LineH;
                double x = line.RightAlign
                    ? rightEdge - line.Text.Length * CharW
                    : left + line.Col * CharW;

                pdf.DrawText(x, y, line.Text, line.Bold);

                if (!string.IsNullOrEmpty(line.SceneNumber))
                {
                    pdf.DrawText(left - (line.SceneNumber.Length + 1) * CharW, y, line.SceneNumber, line.Bold);
                    pdf.DrawText(rightEdge + CharW, y, line.SceneNumber, line.Bold);
                }
            }
        }

        if (pages.Count == 0) pdf.BeginPage();

        return pdf.Build();
    }

    private static void DrawTitlePage(PdfBuilder pdf, TitlePage tp, PageSetup setup)
    {
        pdf.BeginPage();

        double center(string s) => (setup.WidthPt - s.Length * CharW) / 2.0;
        double left = setup.LeftMarginInch * 72.0;
        double top = setup.TopMarginInch * 72.0;

        int row = (int)(setup.LinesPerPage * 0.32);

        void Center(string s, bool bold = false)
        {
            if (s == null) s = string.Empty;
            pdf.DrawText(center(s), top + (row + 1) * LineH, s, bold);
            row++;
        }

        var title = (tp.Title ?? string.Empty).ToUpperInvariant();
        foreach (var l in Paginator.Wrap(title, 50)) Center(l, true);

        if (!string.IsNullOrWhiteSpace(tp.Credit)) { row += 2; Center(tp.Credit); }
        if (!string.IsNullOrWhiteSpace(tp.Author)) { row += 1; Center(tp.Author); }
        if (!string.IsNullOrWhiteSpace(tp.Source)) { row += 2; foreach (var l in Paginator.Wrap(tp.Source, 50)) Center(l); }

        // piede pagina: contatti a sinistra, data bozza a destra
        int footRow = setup.LinesPerPage - 5;
        if (!string.IsNullOrWhiteSpace(tp.Contact))
        {
            int r = footRow;
            foreach (var l in (tp.Contact ?? string.Empty).Replace("\r", "").Split('\n'))
            {
                pdf.DrawText(left, top + (r + 1) * LineH, l.Trim());
                r++;
            }
        }
        if (!string.IsNullOrWhiteSpace(tp.DraftDate))
        {
            double rightEdge = left + ElementStyle.TextWidthInch * 72.0;
            pdf.DrawText(rightEdge - tp.DraftDate.Length * CharW, top + (footRow + 1) * LineH, tp.DraftDate);
        }
        if (!string.IsNullOrWhiteSpace(tp.Copyright))
            pdf.DrawText(left, top + (setup.LinesPerPage + 1) * LineH - LineH, tp.Copyright);
    }
}
