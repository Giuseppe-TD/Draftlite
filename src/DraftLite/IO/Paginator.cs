using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DraftLite.Model;

namespace DraftLite.IO;

public sealed class LayoutLine
{
    public string Text = string.Empty;
    public int Row;            // riga 0-based dentro la pagina
    public int Col;            // colonna 0-based in caratteri dal margine sinistro
    public bool Bold;
    public bool RightAlign;    // allineata al margine destro (transizioni)
    public string SceneNumber; // numero scena da stampare ai lati, se richiesto
}

public sealed class LayoutPage
{
    public List<LayoutLine> Lines = new List<LayoutLine>();
}

/// <summary>
/// Impagina la sceneggiatura secondo le regole standard: 55 righe per pagina,
/// dialoghi spezzati con (MORE) / (CONT'D), intestazioni di scena mai orfane a fine pagina.
/// Questo e' anche il conteggio pagine mostrato nella barra di stato: quello che vedi, stampi.
/// </summary>
public static class Paginator
{
    public static List<LayoutPage> Paginate(Screenplay sp, PageSetup setup)
    {
        var els = sp.Compacted();
        var pages = new List<LayoutPage>();
        var page = new LayoutPage();
        pages.Add(page);
        int row = 0;
        int maxRows = Math.Max(10, setup.LinesPerPage);
        int sceneNo = 0;

        void NewPage()
        {
            page = new LayoutPage();
            pages.Add(page);
            row = 0;
        }

        void Emit(string text, ElementStyle st, string sceneNumber = null)
        {
            page.Lines.Add(new LayoutLine
            {
                Text = text,
                Row = row++,
                Col = st.Col,
                Bold = st.Bold,
                RightAlign = st.RightAlign,
                SceneNumber = sceneNumber
            });
        }

        int i = 0;
        while (i < els.Count)
        {
            var e = els[i];

            // ---- battuta completa: personaggio + parentetiche + dialogo
            if (e.Type == ElementType.Character)
            {
                var group = new List<ScreenElement> { e };
                int j = i + 1;
                while (j < els.Count &&
                       (els[j].Type == ElementType.Dialogue || els[j].Type == ElementType.Parenthetical))
                {
                    group.Add(els[j]);
                    j++;
                }
                EmitDialogue(group);
                i = j;
                continue;
            }

            // ---- elemento semplice
            var style = ElementStyle.Get(e.Type);
            var wrapped = Wrap(e.Text, style.Cols);
            int space = row == 0 ? 0 : style.SpaceBeforeLines;

            // una scena non resta da sola in fondo alla pagina: servono almeno 2 righe dopo
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
                sceneNo++;
                if (setup.SceneNumbers) sn = sceneNo.ToString();
            }

            foreach (var line in wrapped)
            {
                if (row >= maxRows) { NewPage(); }
                Emit(line, style, sn);
                sn = null;
            }
            i++;
        }

        // ------------------------------------------------------------------
        void EmitDialogue(List<ScreenElement> group)
        {
            var chStyle = ElementStyle.Get(ElementType.Character);
            var diStyle = ElementStyle.Get(ElementType.Dialogue);
            var paStyle = ElementStyle.Get(ElementType.Parenthetical);

            string name = group[0].Text;

            // tutte le righe del parlato, con il loro stile
            var speech = new List<(string Text, ElementStyle Style, bool IsParen)>();
            for (int k = 1; k < group.Count; k++)
            {
                var st = ElementStyle.Get(group[k].Type);
                foreach (var l in Wrap(group[k].Text, st.Cols))
                    speech.Add((l, st, group[k].Type == ElementType.Parenthetical));
            }

            if (speech.Count == 0)
            {
                int sp0 = row == 0 ? 0 : chStyle.SpaceBeforeLines;
                if (row + sp0 + 1 > maxRows) { NewPage(); sp0 = 0; }
                row += sp0;
                Emit(name, chStyle);
                return;
            }

            int pos = 0;
            bool cont = false;

            while (pos < speech.Count)
            {
                int space = row == 0 ? 0 : chStyle.SpaceBeforeLines;

                // servono almeno: nome + 1 riga di parlato (+ MORE se si spezza)
                if (row + space + 2 > maxRows)
                {
                    NewPage();
                    space = 0;
                }
                row += space;

                Emit(cont ? WithContd(name) : name, chStyle);

                int canFit = maxRows - row;
                int remaining = speech.Count - pos;

                if (remaining <= canFit)
                {
                    for (int k = 0; k < remaining; k++)
                        Emit(speech[pos + k].Text, speech[pos + k].Style);
                    pos = speech.Count;
                    break;
                }

                int take = canFit - 1;                       // una riga resta per (MORE)
                if (take > 1 && speech[pos + take - 1].IsParen) take--;   // mai una parentetica appesa
                if (take < 1)
                {
                    // non ci sta nulla: togli il nome appena scritto e vai a pagina nuova
                    page.Lines.RemoveAt(page.Lines.Count - 1);
                    row--;
                    NewPage();
                    continue;
                }

                for (int k = 0; k < take; k++)
                    Emit(speech[pos + k].Text, speech[pos + k].Style);
                pos += take;

                Emit("(MORE)", chStyle);
                cont = true;
                NewPage();
            }
        }

        // rimuovi un'eventuale pagina finale vuota
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
            while (word.Length > width)              // parola piu' lunga della colonna
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
