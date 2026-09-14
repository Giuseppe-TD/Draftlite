using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Importazione da Word (.docx), RTF e testo semplice: tanti copioni nascono li'.
/// Se il documento ha i rientri giusti gli elementi si riconoscono da quelli, altrimenti
/// si ricade sulle maiuscole e sulla posizione, che e' come li leggerebbe un umano.
/// Nessuna libreria esterna: il .docx e' uno zip di XML e l'RTF si scandisce a mano.
/// </summary>
public static class TextImporter
{
    private sealed class Para
    {
        public string Text = string.Empty;
        public int Indent;        // twips dal margine sinistro
        public bool RightAligned;
        public bool Centered;
    }

    public static bool CanImport(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".docx" or ".rtf" or ".txt" or ".text";
    }

    public static Screenplay Load(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".docx" => FromParagraphs(ReadDocx(path), Path.GetFileNameWithoutExtension(path)),
            ".rtf" => FromParagraphs(ReadRtf(File.ReadAllText(path, Encoding.UTF8)), Path.GetFileNameWithoutExtension(path)),
            _ => FromPlainText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileNameWithoutExtension(path))
        };
    }

    // ---------------------------------------------------------------- DOCX

    private static List<Para> ReadDocx(string path)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var result = new List<Para>();

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("Il file .docx non contiene word/document.xml.");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        foreach (var p in doc.Descendants(w + "p"))
        {
            var sb = new StringBuilder();
            foreach (var node in p.Descendants())
            {
                if (node.Name == w + "t") sb.Append(node.Value);
                else if (node.Name == w + "tab") sb.Append(' ');
                else if (node.Name == w + "br") sb.Append(' ');
            }

            var para = new Para { Text = CollapseSpaces(sb.ToString()) };

            var ind = p.Element(w + "pPr")?.Element(w + "ind");
            if (ind != null)
            {
                var left = (string)ind.Attribute(w + "left") ?? (string)ind.Attribute(w + "start");
                if (int.TryParse(left, out int v)) para.Indent = v;
                var firstLine = (string)ind.Attribute(w + "firstLine");
                if (int.TryParse(firstLine, out int f)) para.Indent += f;
            }

            var jc = (string)p.Element(w + "pPr")?.Element(w + "jc")?.Attribute(w + "val");
            para.RightAligned = jc == "right" || jc == "end";
            para.Centered = jc == "center";

            result.Add(para);
        }
        return result;
    }

    // ---------------------------------------------------------------- RTF

    /// <summary>
    /// Scanner RTF ridotto all'osso: interessano solo il testo, \li (rientro) e \qr,
    /// piu' gli escape \'hh e \uN. I gruppi di controllo {\*\...} vengono saltati.
    /// </summary>
    private static List<Para> ReadRtf(string rtf)
    {
        var result = new List<Para>();
        var sb = new StringBuilder();
        int indent = 0, pendingIndent = 0;
        bool right = false, pendingRight = false;
        var indentStack = new Stack<(int Indent, bool Right)>();
        int skipGroupDepth = -1;
        int depth = 0;
        int i = 0;

        void EndParagraph()
        {
            result.Add(new Para
            {
                Text = CollapseSpaces(sb.ToString()),
                Indent = pendingIndent,
                RightAligned = pendingRight
            });
            sb.Clear();
            pendingIndent = indent;
            pendingRight = right;
        }

        while (i < rtf.Length)
        {
            char c = rtf[i];

            if (c == '{')
            {
                depth++;
                indentStack.Push((indent, right));
                i++;
                continue;
            }
            if (c == '}')
            {
                if (skipGroupDepth >= 0 && depth == skipGroupDepth) skipGroupDepth = -1;
                if (indentStack.Count > 0) { var s = indentStack.Pop(); indent = s.Indent; right = s.Right; }
                depth--;
                i++;
                continue;
            }
            if (c == '\\')
            {
                i++;
                if (i >= rtf.Length) break;
                char k = rtf[i];

                if (k == '*') { skipGroupDepth = depth; i++; continue; }
                if (k == '\'' && i + 2 < rtf.Length)
                {
                    var hex = rtf.Substring(i + 1, 2);
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                        if (skipGroupDepth < 0) sb.Append(Latin1ToChar(code));
                    i += 3;
                    continue;
                }
                if (k == '\\' || k == '{' || k == '}')
                {
                    if (skipGroupDepth < 0) sb.Append(k);
                    i++;
                    continue;
                }
                if (k == '\n' || k == '\r') { i++; continue; }

                // parola di controllo
                int start = i;
                while (i < rtf.Length && char.IsLetter(rtf[i])) i++;
                string word = rtf.Substring(start, i - start);

                bool negative = i < rtf.Length && rtf[i] == '-';
                if (negative) i++;
                int numStart = i;
                while (i < rtf.Length && char.IsDigit(rtf[i])) i++;
                int? param = null;
                if (i > numStart && int.TryParse(rtf.Substring(numStart, i - numStart), out int pv))
                    param = negative ? -pv : pv;
                if (i < rtf.Length && rtf[i] == ' ') i++;

                switch (word)
                {
                    case "par":
                        if (skipGroupDepth < 0) EndParagraph();
                        break;
                    case "pard":
                        indent = 0; right = false;
                        pendingIndent = 0; pendingRight = false;
                        break;
                    case "li":
                        indent = param ?? 0;
                        pendingIndent = indent;
                        break;
                    case "qr":
                        right = true; pendingRight = true;
                        break;
                    case "ql":
                    case "qj":
                    case "qc":
                        right = false; pendingRight = false;
                        break;
                    case "u":
                        if (skipGroupDepth < 0 && param.HasValue)
                        {
                            int code = param.Value;
                            if (code < 0) code += 65536;
                            sb.Append((char)code);
                            if (i < rtf.Length && rtf[i] == '?') i++;
                        }
                        break;
                    case "tab":
                        if (skipGroupDepth < 0) sb.Append(' ');
                        break;
                    case "fonttbl":
                    case "colortbl":
                    case "stylesheet":
                    case "info":
                    case "pict":
                        skipGroupDepth = depth;
                        break;
                }
                continue;
            }

            if (c == '\r' || c == '\n') { i++; continue; }
            if (skipGroupDepth < 0) sb.Append(c);
            i++;
        }

        if (sb.Length > 0) EndParagraph();
        return result;
    }

    private static char Latin1ToChar(int code) => code < 256 ? (char)code : '?';

    // ---------------------------------------------------------------- CLASSIFICAZIONE

    private static Screenplay FromParagraphs(List<Para> paras, string fallbackTitle)
    {
        paras = paras.Where(p => p != null).ToList();

        // il documento e' impaginato davvero? servono rientri usati con criterio
        int indented = paras.Count(p => p.Indent > 500 && p.Text.Length > 0);
        int withText = Math.Max(1, paras.Count(p => p.Text.Length > 0));
        bool useIndent = (double)indented / withText > 0.12;

        var sp = new Screenplay();
        var lines = paras.Select(p => p.Text).ToList();

        var types = useIndent ? ClassifyByIndent(paras) : ClassifyByText(lines);

        for (int i = 0; i < paras.Count; i++)
        {
            var text = paras[i].Text.Trim();
            if (text.Length == 0) continue;
            var type = types[i];
            var style = ElementStyle.Get(type);
            sp.Elements.Add(new ScreenElement(type, style.UpperCase ? text.ToUpperInvariant() : text));
        }

        CleanUp(sp);
        if (string.IsNullOrWhiteSpace(sp.TitlePage.Title)) sp.TitlePage.Title = fallbackTitle ?? string.Empty;
        return sp;
    }

    public static Screenplay FromPlainText(string text, string fallbackTitle)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        // due strade: il parser Fountain (preciso se le righe vuote ci sono)
        // e la classificazione riga per riga (regge anche i testi scritti alla buona).
        // Vince quella che riconosce piu' struttura; a pari merito, Fountain.
        var viaFountain = FountainIO.Parse(normalized);
        var viaLines = FromLines(normalized);

        int scoreFountain = Structured(viaFountain);
        int scoreLines = Structured(viaLines);
        var sp = scoreFountain >= scoreLines ? viaFountain : viaLines;

        if (string.IsNullOrWhiteSpace(sp.TitlePage.Title)) sp.TitlePage.Title = fallbackTitle ?? string.Empty;
        return sp;
    }

    private static int Structured(Screenplay sp)
        => sp.Elements.Count(e => e.Type != ElementType.Action && !e.IsEmpty);

    private static Screenplay FromLines(string normalized)
    {
        var sp = new Screenplay();
        var lines = normalized.Split('\n').Select(l => l.TrimEnd()).ToList();
        var types = ClassifyByText(lines);
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0) continue;
            var style = ElementStyle.Get(types[i]);
            sp.Elements.Add(new ScreenElement(types[i], style.UpperCase ? t.ToUpperInvariant() : t));
        }
        CleanUp(sp);
        return sp;
    }

    private static List<ElementType> ClassifyByIndent(List<Para> paras)
    {
        var result = new List<ElementType>(paras.Count);
        ElementType prev = ElementType.Action;

        foreach (var p in paras)
        {
            var t = p.Text.Trim();
            ElementType type;

            if (t.Length == 0)
            {
                type = ElementType.Action;
            }
            else if (p.RightAligned || p.Indent >= 5000)
            {
                type = ElementType.Transition;
            }
            else if (Screenplay.LooksLikeSceneHeading(t) && IsUpper(t))
            {
                type = ElementType.SceneHeading;
            }
            else if (t.StartsWith("(") && t.EndsWith(")") &&
                     (prev == ElementType.Character || prev == ElementType.Dialogue))
            {
                type = ElementType.Parenthetical;
            }
            else if (p.Indent >= 2600 || (p.Centered && IsUpper(t) && t.Length < 40))
            {
                type = IsUpper(t) ? ElementType.Character : ElementType.Dialogue;
            }
            else if (p.Indent >= 1900)
            {
                type = ElementType.Parenthetical;
            }
            else if (p.Indent >= 700)
            {
                type = ElementType.Dialogue;
            }
            else if (IsUpper(t) && Screenplay.TransitionPattern.IsMatch(t))
            {
                type = ElementType.Transition;
            }
            else
            {
                type = ElementType.Action;
            }

            result.Add(type);
            if (t.Length > 0) prev = type;
        }
        return result;
    }

    /// <summary>Classifica righe di testo semplice (usata anche quando si incolla nell'editor).</summary>
    public static List<ElementType> ClassifyPlainLines(IList<string> lines)
        => ClassifyByText(lines.ToList());

    /// <summary>Classificazione senza rientri: maiuscole, parentesi e posizione nel discorso.</summary>
    private static List<ElementType> ClassifyByText(List<string> lines)
    {
        var result = new List<ElementType>(lines.Count);
        ElementType prev = ElementType.Action;

        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            ElementType type;

            if (t.Length == 0)
            {
                result.Add(ElementType.Action);
                prev = ElementType.Action;
                continue;
            }

            bool nextEmpty = i + 1 >= lines.Count || lines[i + 1].Trim().Length == 0;

            if (IsUpper(t) && Screenplay.LooksLikeSceneHeading(t)) type = ElementType.SceneHeading;
            else if (IsUpper(t) && Screenplay.TransitionPattern.IsMatch(t)) type = ElementType.Transition;
            else if (t.StartsWith("(") && t.EndsWith(")") &&
                     (prev == ElementType.Character || prev == ElementType.Dialogue)) type = ElementType.Parenthetical;
            else if (IsUpper(t) && t.Length <= 40 && !nextEmpty) type = ElementType.Character;
            else if (prev == ElementType.Character || prev == ElementType.Parenthetical) type = ElementType.Dialogue;
            else type = ElementType.Action;

            result.Add(type);
            prev = type;
        }
        return result;
    }

    private static bool IsUpper(string s)
    {
        bool hasLetter = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
                if (char.IsLower(c)) return false;
            }
        }
        return hasLetter;
    }

    private static string CollapseSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var c in s)
        {
            if (c == '\t' || c == ' ' || c == ' ')
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
            }
            else { sb.Append(c); space = false; }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Ritocchi dopo l'import: un personaggio senza battuta e' un'azione.</summary>
    private static void CleanUp(Screenplay sp)
    {
        var els = sp.Elements;
        for (int i = 0; i < els.Count; i++)
        {
            if (els[i].Type != ElementType.Character) continue;
            bool hasSpeech = i + 1 < els.Count &&
                             (els[i + 1].Type == ElementType.Dialogue || els[i + 1].Type == ElementType.Parenthetical);
            if (!hasSpeech) els[i].Type = ElementType.Action;
        }
    }
}
