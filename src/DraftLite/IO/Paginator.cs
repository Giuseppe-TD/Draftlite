using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DraftLite.Model;

namespace DraftLite.IO;

public sealed class LayoutLine
{
    public string Text = string.Empty;
    /// <summary>Pezzi formattati della riga (grassetto/corsivo/sottolineato).</summary>
    public List<TextRun> Runs;
    public int Row;            // riga 0-based dentro la pagina
    public int Col;            // colonna 0-based in caratteri dal margine sinistro
    public bool Bold;
    public bool RightAlign;    // allineata al margine destro (transizioni)
    public string SceneNumber; // numero scena da stampare ai lati, se richiesto
    public bool Revised;       // riga cambiata dall'ultima bozza: asterisco a margine
    public int ElementIndex = -1;  // elemento di provenienza (serve all'editor per le interruzioni)
}

public sealed class LayoutPage
{
    public List<LayoutLine> Lines = new List<LayoutLine>();
    public bool AnyRevised => Lines.Any(l => l.Revised);
}

/// <summary>
/// Impagina la sceneggiatura secondo le regole standard: 55 righe per pagina,
/// dialoghi spezzati con (MORE) / (CONT'D), intestazioni di scena mai orfane a fine pagina,
/// dialoghi simultanei su due colonne, numeri di scena ai margini e asterischi di revisione.
/// Questo e' anche il conteggio pagine mostrato nella barra di stato: quello che vedi, stampi.
/// </summary>
public static class Paginator
{
    // colonne del dialogo simultaneo (in caratteri dal margine sinistro)
    private const int DualLeftCol = 3;
    private const int DualRightCol = 32;
    private const int DualWidth = 25;

    public static List<LayoutPage> Paginate(Screenplay sp, PageSetup setup)
    {
        var els = sp.Compacted();
        var revised = sp.Revision != null ? sp.Revision.MarkChanged(els) : new bool[els.Count];
        var sceneNumbers = sp.SceneNumbers();

        var pages = new List<LayoutPage>();
        var page = new LayoutPage();
        pages.Add(page);
        int row = 0;
        int maxRows = Math.Max(10, setup.LinesPerPage);
        int sceneCount = 0;
        int currentElement = 0;

        void NewPage()
        {
            page = new LayoutPage();
            pages.Add(page);
            row = 0;
        }

        void EmitRuns(List<TextRun> runs, ElementStyle st, bool rev, string sceneNumber = null, int? colOverride = null)
        {
            page.Lines.Add(new LayoutLine
            {
                Text = StyledText.LineText(runs),
                Runs = runs,
                Row = row++,
                ElementIndex = currentElement,
                Col = colOverride ?? st.Col,
                Bold = st.Bold,
                RightAlign = st.RightAlign && colOverride == null,
                SceneNumber = sceneNumber,
                Revised = rev
            });
        }

        void Emit(string text, ElementStyle st, bool rev, string sceneNumber = null, int? colOverride = null)
            => EmitRuns(new List<TextRun> { new TextRun(text) }, st, rev, sceneNumber, colOverride);

        int i = 0;
        while (i < els.Count)
        {
            var e = els[i];
            currentElement = i;

            // ---- interruzione di pagina forzata (riga di soli "=", come in Fountain)
            if (Screenplay.IsPageBreak(e))
            {
                if (row > 0) NewPage();
                i++;
                continue;
            }

            // ---- battuta: personaggio + parentetiche + dialogo
            if (e.Type == ElementType.Character)
            {
                var group = new List<ScreenElement> { e };
                var groupRev = new List<bool> { revised[i] };
                int j = i + 1;
                while (j < els.Count &&
                       (els[j].Type == ElementType.Dialogue || els[j].Type == ElementType.Parenthetical))
                {
                    group.Add(els[j]);
                    groupRev.Add(revised[j]);
                    j++;
                }

                // il parlante successivo e' marcato come simultaneo? vanno affiancati
                if (j < els.Count && els[j].Type == ElementType.Character && els[j].Dual)
                {
                    var second = new List<ScreenElement> { els[j] };
                    var secondRev = new List<bool> { revised[j] };
                    int k = j + 1;
                    while (k < els.Count &&
                           (els[k].Type == ElementType.Dialogue || els[k].Type == ElementType.Parenthetical))
                    {
                        second.Add(els[k]);
                        secondRev.Add(revised[k]);
                        k++;
                    }
                    EmitDual(group, groupRev, second, secondRev);
                    i = k;
                    continue;
                }

                EmitDialogue(group, groupRev);
                i = j;
                continue;
            }

            // ---- elemento semplice
            var style = ElementStyle.Get(e.Type);
            var wrapped = StyledText.Wrap(e.Text, style.Cols);
            int space = row == 0 ? 0 : style.SpaceBeforeLines;
            int keepWith = e.Type == ElementType.SceneHeading ? 2 : 0;

            if (row + space + wrapped.Count + keepWith > maxRows && row > 0)
            {
                NewPage();
                space = 0;
            }

            row += space;

            string sn = null;
            if (e.Type == ElementType.SceneHeading)
            {
                if (setup.SceneNumbers && sceneCount < sceneNumbers.Count) sn = sceneNumbers[sceneCount];
                sceneCount++;
            }

            foreach (var line in wrapped)
            {
                if (row >= maxRows) NewPage();
                EmitRuns(line, style, revised[i], sn);
                sn = null;
            }
            i++;
        }

        // ------------------------------------------------------------------
        void EmitDialogue(List<ScreenElement> group, List<bool> groupRev)
        {
            var chStyle = ElementStyle.Get(ElementType.Character);
            string name = StyledText.Plain(group[0].Text);
            bool nameRev = groupRev[0];

            var speech = new List<(List<TextRun> Runs, ElementStyle Style, bool IsParen, bool Rev)>();
            for (int k = 1; k < group.Count; k++)
            {
                var st = ElementStyle.Get(group[k].Type);
                foreach (var l in StyledText.Wrap(group[k].Text, st.Cols))
                    speech.Add((l, st, group[k].Type == ElementType.Parenthetical, groupRev[k]));
            }

            if (speech.Count == 0)
            {
                int sp0 = row == 0 ? 0 : chStyle.SpaceBeforeLines;
                if (row + sp0 + 1 > maxRows) { NewPage(); sp0 = 0; }
                row += sp0;
                Emit(name, chStyle, nameRev);
                return;
            }

            int pos = 0;
            bool cont = false;

            while (pos < speech.Count)
            {
                int space = row == 0 ? 0 : chStyle.SpaceBeforeLines;
                if (row + space + 2 > maxRows) { NewPage(); space = 0; }
                row += space;

                Emit(cont ? WithContd(name) : name, chStyle, nameRev);

                int canFit = maxRows - row;
                int remaining = speech.Count - pos;

                if (remaining <= canFit)
                {
                    for (int k = 0; k < remaining; k++)
                        EmitRuns(speech[pos + k].Runs, speech[pos + k].Style, speech[pos + k].Rev);
                    pos = speech.Count;
                    break;
                }

                int take = canFit - 1;
                if (take > 1 && speech[pos + take - 1].IsParen) take--;
                if (take < 1)
                {
                    page.Lines.RemoveAt(page.Lines.Count - 1);
                    row--;
                    NewPage();
                    continue;
                }

                for (int k = 0; k < take; k++)
                    EmitRuns(speech[pos + k].Runs, speech[pos + k].Style, speech[pos + k].Rev);
                pos += take;

                Emit("(MORE)", chStyle, false);
                cont = true;
                NewPage();
            }
        }

        // ------------------------------------------------------------------
        void EmitDual(List<ScreenElement> a, List<bool> aRev, List<ScreenElement> b, List<bool> bRev)
        {
            var chStyle = ElementStyle.Get(ElementType.Character);

            List<(List<TextRun> Runs, int Col, bool Rev, bool IsName)> Column(List<ScreenElement> g, List<bool> rev, int col)
            {
                var lines = new List<(List<TextRun>, int, bool, bool)>();
                var name = StyledText.Plain(g[0].Text);
                int nameCol = col + Math.Max(0, (DualWidth - name.Length) / 2);
                lines.Add((new List<TextRun> { new TextRun(name) }, nameCol, rev[0], true));

                for (int k = 1; k < g.Count; k++)
                {
                    bool paren = g[k].Type == ElementType.Parenthetical;
                    int width = paren ? DualWidth - 4 : DualWidth;
                    foreach (var l in StyledText.Wrap(g[k].Text, width))
                        lines.Add((l, col + (paren ? 2 : 0), rev[k], false));
                }
                return lines;
            }

            var left = Column(a, aRev, DualLeftCol);
            var right = Column(b, bRev, DualRightCol);
            int height = Math.Max(left.Count, right.Count);

            int space = row == 0 ? 0 : chStyle.SpaceBeforeLines;
            if (row + space + Math.Min(height, 4) > maxRows) { NewPage(); space = 0; }
            row += space;

            for (int k = 0; k < height; k++)
            {
                if (row >= maxRows) NewPage();
                int r = row++;

                if (k < left.Count)
                    page.Lines.Add(new LayoutLine
                    {
                        Text = StyledText.LineText(left[k].Runs), Runs = left[k].Runs,
                        Row = r, Col = left[k].Col, Revised = left[k].Rev
                    });

                if (k < right.Count)
                    page.Lines.Add(new LayoutLine
                    {
                        Text = StyledText.LineText(right[k].Runs), Runs = right[k].Runs,
                        Row = r, Col = right[k].Col, Revised = right[k].Rev
                    });
            }
        }

        if (pages.Count > 1 && pages[pages.Count - 1].Lines.Count == 0)
            pages.RemoveAt(pages.Count - 1);

        return pages;
    }

    private static string WithContd(string name)
        => name.IndexOf("CONT'D", StringComparison.OrdinalIgnoreCase) >= 0 ? name : name + " (CONT'D)";

    /// <summary>Word wrap su larghezza in caratteri (Courier = passo fisso).</summary>
    public static List<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) { result.Add(string.Empty); return result; }
        if (width < 5) width = 5;

        var words = text.Replace('\t', ' ').Split(' ').Where(w => w.Length > 0).ToArray();
        if (words.Length == 0) { result.Add(string.Empty); return result; }

        var sb = new StringBuilder();
        foreach (var w in words)
        {
            var word = w;
            while (word.Length > width)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                result.Add(word.Substring(0, width));
                word = word.Substring(width);
            }
            if (sb.Length == 0) sb.Append(word);
            else if (sb.Length + 1 + word.Length <= width) sb.Append(' ').Append(word);
            else { result.Add(sb.ToString()); sb.Clear(); sb.Append(word); }
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        if (result.Count == 0) result.Add(string.Empty);
        return result;
    }

    /// <summary>Numero di pagine (stima reale, stessa logica del PDF).</summary>
    public static int CountPages(Screenplay sp, PageSetup setup) => Paginate(sp, setup).Count;
}
