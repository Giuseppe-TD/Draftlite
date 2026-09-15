using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Export PDF in formato standard di sceneggiatura: Courier 12, 55 righe per pagina,
/// numeri di pagina in alto a destra, frontespizio opzionale, asterischi di revisione,
/// filigrana e copie nominative. Genera anche i sides per attore e il report statistico.
/// </summary>
public static class PdfExporter
{
    private const double CharW = ElementStyle.CharWidthPt;   // 7.2 pt = 10 cpi
    private const double LineH = ElementStyle.LineHeightPt;  // 12 pt

    public static void Export(string path, Screenplay sp, PageSetup setup)
        => File.WriteAllBytes(path, Build(sp, setup));

    public static byte[] Build(Screenplay sp, PageSetup setup)
    {
        var pdf = new PdfBuilder(setup.WidthPt, setup.HeightPt)
        {
            Title = sp.TitlePage.Title,
            Author = sp.TitlePage.Author
        };

        double left = setup.LeftMarginInch * 72.0;
        double top = setup.TopMarginInch * 72.0;
        double rightEdge = left + (sp.Layout?.TextWidth ?? ElementStyle.TextWidthInch) * 72.0;

        if (setup.IncludeTitlePage && !sp.TitlePage.IsEmpty)
        {
            DrawTitlePage(pdf, sp, setup);
            Decorate(pdf, setup);
        }

        var pages = Paginator.Paginate(sp, setup);
        bool revisionOn = setup.RevisionMarks && sp.Revision != null && sp.Revision.Active;

        for (int p = 0; p < pages.Count; p++)
        {
            pdf.BeginPage();

            if (setup.PageNumbers && p > 0)
            {
                var num = (p + 1) + ".";
                pdf.DrawText(rightEdge - num.Length * CharW, 0.5 * 72.0 + LineH, num);
            }

            if (revisionOn)
            {
                var label = (sp.Revision.Color ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(sp.Revision.Date)) label += "  " + sp.Revision.Date.Trim();
                if (label.Length > 0)
                    pdf.DrawText(left, 0.5 * 72.0 + LineH, label, false, false, 0.35, 9);
            }

            foreach (var line in pages[p].Lines)
            {
                double y = top + (line.Row + 1) * LineH;
                double x = line.RightAlign
                    ? left + line.RightInch * 72 - line.Text.Length * CharW
                    : left + line.Col * CharW;

                if (line.Runs != null && line.Runs.Count > 0)
                    pdf.DrawRuns(x, y, line.Runs, line.Bold, PdfBuilder.DefaultFontSize, setup.TextColors);
                else
                    pdf.DrawText(x, y, line.Text, line.Bold);

                if (!string.IsNullOrEmpty(line.SceneNumber))
                {
                    pdf.DrawText(left - (line.SceneNumber.Length + 1) * CharW, y, line.SceneNumber, line.Bold);
                    pdf.DrawText(rightEdge + CharW, y, line.SceneNumber, line.Bold);
                }

                if (revisionOn && line.Revised)
                    pdf.DrawText(rightEdge + 3 * CharW, y, "*", true);
            }

            Decorate(pdf, setup);
        }

        if (pages.Count == 0) pdf.BeginPage();

        return pdf.Build();
    }

    /// <summary>Filigrana in diagonale e riga della copia nominativa.</summary>
    private static void Decorate(PdfBuilder pdf, PageSetup setup)
    {
        if (!string.IsNullOrWhiteSpace(setup.Watermark))
        {
            var text = setup.Watermark.Trim().ToUpperInvariant();
            double diagonal = Math.Sqrt(setup.WidthPt * setup.WidthPt + setup.HeightPt * setup.HeightPt);

            // il testo occupa circa due terzi della diagonale, qualunque sia la sua lunghezza
            double size = Math.Min(52, Math.Max(13, diagonal * 0.66 / Math.Max(6, text.Length * 0.6)));
            double length = text.Length * size * 0.6;
            double angle = Math.Min(45, Math.Atan2(setup.HeightPt, setup.WidthPt) * 180.0 / Math.PI);
            double rad = angle * Math.PI / 180.0;

            double x = setup.WidthPt / 2 - Math.Cos(rad) * length / 2;
            double yFromTop = setup.HeightPt / 2 + Math.Sin(rad) * length / 2 - size * 0.35;
            pdf.DrawRotatedText(x, yFromTop, text, angle, size, 0.86);
        }

        if (!string.IsNullOrWhiteSpace(setup.CopyFor))
        {
            double left = setup.LeftMarginInch * 72.0;
            pdf.DrawText(left, setup.HeightPt - 28, "Copia riservata a: " + setup.CopyFor.Trim(),
                         false, false, 0.45, 8);
        }
    }

    private static void DrawTitlePage(PdfBuilder pdf, Screenplay sp, PageSetup setup)
    {
        var tp = sp.TitlePage;
        pdf.BeginPage();

        double Center(string s) => (setup.WidthPt - s.Length * CharW) / 2.0;
        double left = setup.LeftMarginInch * 72.0;
        double top = setup.TopMarginInch * 72.0;

        int row = (int)(setup.LinesPerPage * 0.32);

        void CenterLine(string s, bool bold = false)
        {
            s ??= string.Empty;
            pdf.DrawText(Center(s), top + (row + 1) * LineH, s, bold);
            row++;
        }

        var title = (tp.Title ?? string.Empty).ToUpperInvariant();
        foreach (var l in Paginator.Wrap(title, 50)) CenterLine(l, true);

        if (!string.IsNullOrWhiteSpace(tp.Credit)) { row += 2; CenterLine(tp.Credit); }
        if (!string.IsNullOrWhiteSpace(tp.Author)) { row += 1; CenterLine(tp.Author); }
        if (!string.IsNullOrWhiteSpace(tp.Source)) { row += 2; foreach (var l in Paginator.Wrap(tp.Source, 50)) CenterLine(l); }

        int footRow = setup.LinesPerPage - 5;
        if (!string.IsNullOrWhiteSpace(tp.Contact))
        {
            int r = footRow;
            foreach (var l in tp.Contact.Replace("\r", "").Split('\n'))
            {
                pdf.DrawText(left, top + (r + 1) * LineH, l.Trim());
                r++;
            }
        }

        double rightEdge = left + (sp.Layout?.TextWidth ?? ElementStyle.TextWidthInch) * 72.0;
        if (!string.IsNullOrWhiteSpace(tp.DraftDate))
            pdf.DrawText(rightEdge - tp.DraftDate.Length * CharW, top + (footRow + 1) * LineH, tp.DraftDate);

        if (sp.Revision != null && sp.Revision.Active && !string.IsNullOrWhiteSpace(sp.Revision.Color))
        {
            var label = "Bozza " + sp.Revision.Color.Trim();
            pdf.DrawText(rightEdge - label.Length * CharW, top + (footRow + 2) * LineH, label);
        }

        if (!string.IsNullOrWhiteSpace(tp.Copyright))
            pdf.DrawText(left, top + setup.LinesPerPage * LineH, tp.Copyright);
    }

    // ------------------------------------------------------------------ SIDES

    /// <summary>PDF con le sole scene in cui compare il personaggio.</summary>
    public static void ExportSides(string path, Screenplay sp, string character, PageSetup setup)
    {
        var sides = sp.Sides(character);
        var s = setup.Clone();
        s.SceneNumbers = true;               // servono i numeri originali per ritrovarsi
        s.IncludeTitlePage = true;
        File.WriteAllBytes(path, Build(sides, s));
    }

    // ------------------------------------------------------------------ REPORT

    /// <summary>Report statistico stampabile: riepilogo, personaggi, scene.</summary>
    public static void ExportReport(string path, Screenplay sp, PageSetup setup)
        => File.WriteAllBytes(path, BuildReport(sp, setup));

    public static byte[] BuildReport(Screenplay sp, PageSetup setup)
    {
        var st = ScreenplayStats.Compute(sp, setup);
        var pdf = new PdfBuilder(setup.WidthPt, setup.HeightPt)
        {
            Title = "Statistiche - " + st.Title,
            Author = st.Author
        };

        const double size = 10.0;
        const double cw = size * 0.6;
        const double lh = 12.0;
        double left = 56;
        double top = 56;
        double usableWidth = setup.WidthPt - left * 2;
        int cols = (int)(usableWidth / cw);
        int rowsPerPage = (int)((setup.HeightPt - top - 50) / lh);

        int row = 0;
        pdf.BeginPage();

        void Line(string text, bool bold = false, double gray = 0)
        {
            if (row >= rowsPerPage) { pdf.BeginPage(); row = 0; }
            pdf.DrawText(left, top + (row + 1) * lh, text ?? string.Empty, bold, false, gray, size);
            row++;
        }

        void Rule()
        {
            if (row >= rowsPerPage) { pdf.BeginPage(); row = 0; }
            pdf.DrawLine(left, top + row * lh + 4, left + usableWidth, top + row * lh + 4, 0.7, 0.4);
            row++;
        }

        void Blank(int n = 1) { for (int i = 0; i < n; i++) { if (row < rowsPerPage) row++; else { pdf.BeginPage(); row = 0; } } }

        string Fit(string s, int width)
        {
            s ??= string.Empty;
            if (s.Length <= width) return s.PadRight(width);
            return width <= 1 ? s.Substring(0, width) : s.Substring(0, width - 1) + ".";
        }

        Line("STATISTICHE DEL COPIONE", true);
        Rule();
        Line(Fit("Titolo", 22) + (string.IsNullOrWhiteSpace(st.Title) ? "(senza titolo)" : st.Title));
        Line(Fit("Autore", 22) + (string.IsNullOrWhiteSpace(st.Author) ? "-" : st.Author));
        Line(Fit("Pagine", 22) + st.Pages + "   (" + st.Paper + ", " + st.LinesPerPage + " righe)");
        Line(Fit("Durata stimata", 22) + string.Format("{0:00}:{1:00}", (int)st.Duration.TotalHours, st.Duration.Minutes) +
             "   (1 pagina = 1 minuto)");
        Line(Fit("Scene", 22) + st.SceneCount + "   (interni " + st.IntCount + " / esterni " + st.ExtCount + ")");
        Line(Fit("Giorno / notte", 22) + st.DayCount + " / " + st.NightCount);
        Line(Fit("Personaggi parlanti", 22) + st.Characters.Count);
        Line(Fit("Battute totali", 22) + st.TotalSpeeches);
        Line(Fit("Parole di dialogo", 22) + st.DialogueWords);
        Line(Fit("Parole di azione", 22) + st.ActionWords);
        if (st.SceneCount > 0)
            Line(Fit("Lunghezza media scena", 22) + ((double)st.Pages / st.SceneCount).ToString("0.0") + " pagine");

        Blank(2);
        Line("PERSONAGGI", true);
        Rule();
        Line(Fit("PERSONAGGIO", 26) + Fit("BATTUTE", 10) + Fit("PAROLE", 10) + Fit("% PARLATO", 12) +
             Fit("SCENE", 8) + "1a SCENA", true);
        foreach (var c in st.Characters)
        {
            Line(Fit(c.Name, 26) + Fit(c.Speeches.ToString(), 10) + Fit(c.Words.ToString(), 10) +
                 Fit(c.SharePercent.ToString("0.0") + "%", 12) + Fit(c.SceneCount.ToString(), 8) + c.FirstScene);
        }

        Blank(2);
        Line("SCENE", true);
        Rule();
        Line(Fit("#", 6) + Fit("PAG.", 6) + Fit("INTESTAZIONE", 46) + "PERSONAGGI", true);
        foreach (var sc in st.Scenes)
        {
            Line(Fit(sc.Number, 6) + Fit(sc.Page > 0 ? sc.Page.ToString() : "-", 6) +
                 Fit(sc.Heading, 46) + Fit(string.Join(", ", sc.Characters), Math.Max(10, cols - 58)));
            if (!string.IsNullOrWhiteSpace(sc.Synopsis))
                Line(new string(' ', 12) + Fit(sc.Synopsis, Math.Max(20, cols - 12)), false, 0.4);
        }

        return pdf.Build();
    }
}
