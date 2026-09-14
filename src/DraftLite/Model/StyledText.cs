using System;
using System.Collections.Generic;
using System.Text;

namespace DraftLite.Model;

/// <summary>Un pezzo di testo con la sua formattazione.</summary>
public sealed class TextRun
{
    public string Text = string.Empty;
    public bool Bold;
    public bool Italic;
    public bool Underline;

    public TextRun() { }

    public TextRun(string text, bool bold = false, bool italic = false, bool underline = false)
    {
        Text = text ?? string.Empty;
        Bold = bold;
        Italic = italic;
        Underline = underline;
    }

    public bool SameStyle(TextRun other)
        => other != null && Bold == other.Bold && Italic == other.Italic && Underline == other.Underline;

    public TextRun Clone() => new TextRun(Text, Bold, Italic, Underline);
}

/// <summary>
/// Grassetto, corsivo e sottolineato dentro il testo, scritti come li scrive Fountain:
/// **grassetto**, *corsivo*, _sottolineato_. Restano nel testo dell'elemento, quindi
/// sopravvivono al salvataggio in tutti i formati senza strutture parallele.
/// </summary>
public static class StyledText
{
    /// <summary>Testo senza marcatori: quello che si vede e che si conta.</summary>
    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.IndexOf('*') < 0 && text.IndexOf('_') < 0) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var run in Parse(text)) sb.Append(run.Text);
        return sb.ToString();
    }

    public static bool HasMarkup(string text)
        => !string.IsNullOrEmpty(text) && (text.IndexOf('*') >= 0 || text.IndexOf('_') >= 0);

    /// <summary>Spezza il testo nei suoi pezzi formattati.</summary>
    public static List<TextRun> Parse(string text)
    {
        var runs = new List<TextRun>();
        if (string.IsNullOrEmpty(text)) { runs.Add(new TextRun(string.Empty)); return runs; }

        bool bold = false, italic = false, underline = false;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;
            runs.Add(new TextRun(buffer.ToString(), bold, italic, underline));
            buffer.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // \* e \_ sono asterischi e trattini veri
            if (c == '\\' && i + 1 < text.Length && (text[i + 1] == '*' || text[i + 1] == '_'))
            {
                buffer.Append(text[i + 1]);
                i++;
                continue;
            }

            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }

            if (c == '*')
            {
                Flush();
                italic = !italic;
                continue;
            }

            if (c == '_')
            {
                Flush();
                underline = !underline;
                continue;
            }

            buffer.Append(c);
        }
        Flush();

        if (runs.Count == 0) runs.Add(new TextRun(string.Empty));
        return runs;
    }

    /// <summary>Rimette i marcatori attorno ai pezzi formattati.</summary>
    public static string Write(IEnumerable<TextRun> runs)
    {
        var sb = new StringBuilder();
        bool bold = false, italic = false, underline = false;

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;

            // chiudi quello che non serve piu', nell'ordine inverso di apertura
            if (underline && !run.Underline) { sb.Append('_'); underline = false; }
            if (italic && !run.Italic) { sb.Append('*'); italic = false; }
            if (bold && !run.Bold) { sb.Append("**"); bold = false; }

            if (!bold && run.Bold) { sb.Append("**"); bold = true; }
            if (!italic && run.Italic) { sb.Append('*'); italic = true; }
            if (!underline && run.Underline) { sb.Append('_'); underline = true; }

            foreach (var c in run.Text)
            {
                if (c == '*' || c == '_') sb.Append('\\');
                sb.Append(c);
            }
        }

        if (underline) sb.Append('_');
        if (italic) sb.Append('*');
        if (bold) sb.Append("**");

        return sb.ToString();
    }

    /// <summary>
    /// Manda a capo il testo formattato a una certa larghezza in caratteri.
    /// Si lavora sul testo piatto tenendo lo stile di ogni singolo carattere: cosi'
    /// gli spazi fra una parola e l'altra non si perdono quando cambia la formattazione.
    /// </summary>
    public static List<List<TextRun>> Wrap(string markedText, int width)
    {
        if (width < 5) width = 5;

        var runs = Parse(markedText);
        var sb = new StringBuilder();
        var styleOf = new List<TextRun>();
        foreach (var r in runs)
            foreach (var c in r.Text) { sb.Append(c); styleOf.Add(r); }

        var plain = sb.ToString();
        var result = new List<List<TextRun>>();

        // parole con la loro posizione nel testo piatto
        var words = new List<(int Start, int Len)>();
        int i = 0;
        while (i < plain.Length)
        {
            while (i < plain.Length && plain[i] == ' ') i++;
            int wStart = i;
            while (i < plain.Length && plain[i] != ' ') i++;
            if (i > wStart) words.Add((wStart, i - wStart));
        }

        if (words.Count == 0)
        {
            result.Add(new List<TextRun> { new TextRun(string.Empty) });
            return result;
        }

        var line = new List<TextRun>();
        int lineLen = 0;

        void Add(int start, int len)
        {
            for (int k = 0; k < len; k++)
            {
                var style = styleOf[start + k];
                var last = line.Count > 0 ? line[line.Count - 1] : null;
                if (last != null && last.SameStyle(style)) last.Text += plain[start + k];
                else line.Add(new TextRun(plain[start + k].ToString(), style.Bold, style.Italic, style.Underline));
            }
            lineLen += len;
        }

        void NewLine()
        {
            result.Add(line);
            line = new List<TextRun>();
            lineLen = 0;
        }

        foreach (var (wStart, wLen) in words)
        {
            int start = wStart, len = wLen;

            // parola piu' lunga della colonna: si spezza
            while (len > width)
            {
                if (lineLen > 0) NewLine();
                Add(start, width);
                start += width;
                len -= width;
                NewLine();
            }

            if (lineLen == 0)
            {
                Add(start, len);
            }
            else if (lineLen + 1 + len <= width)
            {
                // lo spazio prende lo stile del carattere che lo precede
                var prevStyle = styleOf[Math.Max(0, start - 1)];
                var last = line.Count > 0 ? line[line.Count - 1] : null;
                if (last != null && last.SameStyle(prevStyle)) last.Text += ' ';
                else line.Add(new TextRun(" ", prevStyle.Bold, prevStyle.Italic, prevStyle.Underline));
                lineLen++;
                Add(start, len);
            }
            else
            {
                NewLine();
                Add(start, len);
            }
        }

        if (line.Count > 0 || result.Count == 0) result.Add(line);

        while (result.Count > 1 && LineLength(result[result.Count - 1]) == 0)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    public static int LineLength(List<TextRun> line)
    {
        int n = 0;
        foreach (var r in line) n += r.Text.Length;
        return n;
    }

    public static string LineText(List<TextRun> line)
    {
        var sb = new StringBuilder();
        foreach (var r in line) sb.Append(r.Text);
        return sb.ToString();
    }
}
