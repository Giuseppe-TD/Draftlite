using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Import/export Fountain (.fountain / .spmd / .txt). Formato testo puro, standard aperto,
/// lo legge anche Final Draft. Implementazione volutamente centrata sugli elementi che
/// DraftLite gestisce: scene, azione, personaggio, parentetica, dialogo, transizione.
/// Sezioni (#), synopsis (=) e note ([[...]]) vengono ignorate in lettura.
/// </summary>
public static class FountainIO
{
    // ---------------------------------------------------------------- LETTURA

    public static Screenplay Load(string path)
        => Parse(File.ReadAllText(path, DetectEncoding(path)));

    public static Screenplay Parse(string text)
    {
        var sp = new Screenplay();
        if (text == null) return sp;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        int i = 0;

        i = ParseTitlePage(lines, sp.TitlePage);

        bool prevBlank = true;
        var pendingAction = new List<string>();

        void FlushAction()
        {
            if (pendingAction.Count == 0) return;
            sp.Elements.Add(new ScreenElement(ElementType.Action, string.Join(" ", pendingAction).Trim()));
            pendingAction.Clear();
        }

        while (i < lines.Length)
        {
            var raw = lines[i];
            var t = raw.Trim();

            if (t.Length == 0)
            {
                FlushAction();
                prevBlank = true;
                i++;
                continue;
            }

            // Note e blocchi che non ci interessano
            if (t.StartsWith("[[") || t.StartsWith("=") || t.StartsWith("#") ||
                t.StartsWith("~") || t == "===")
            {
                i++;
                prevBlank = false;
                continue;
            }

            // --- forzature esplicite Fountain
            if (t.StartsWith(".") && !t.StartsWith(".."))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.SceneHeading, t.Substring(1).Trim().ToUpperInvariant()));
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith("!"))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Action, t.Substring(1).Trim()));
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith(">") && !t.EndsWith("<"))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Transition, t.Substring(1).Trim().ToUpperInvariant()));
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith(">") && t.EndsWith("<"))   // testo centrato: lo trattiamo come azione
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Action, t.Trim('>', '<').Trim()));
                i++; prevBlank = false; continue;
            }

            bool forcedCharacter = t.StartsWith("@");
            string cue = forcedCharacter ? t.Substring(1).Trim() : t;

            // --- intestazione di scena
            if (prevBlank && !forcedCharacter && Screenplay.LooksLikeSceneHeading(t))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.SceneHeading, t.ToUpperInvariant()));
                i++; prevBlank = false; continue;
            }

            // --- transizione non forzata (TUTTA MAIUSCOLA e finisce con TO:)
            if (prevBlank && !forcedCharacter && IsUpper(t) && Screenplay.TransitionPattern.IsMatch(t))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Transition, t));
                i++; prevBlank = false; continue;
            }

            // --- battuta: personaggio + eventuali parentetiche + dialogo
            bool nextNotBlank = (i + 1 < lines.Length) && lines[i + 1].Trim().Length > 0;
            if (prevBlank && nextNotBlank && (forcedCharacter || IsCharacterCue(cue)))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Character, cue.ToUpperInvariant()));
                i++;

                var speech = new List<string>();
                void FlushSpeech()
                {
                    if (speech.Count == 0) return;
                    sp.Elements.Add(new ScreenElement(ElementType.Dialogue, string.Join(" ", speech).Trim()));
                    speech.Clear();
                }

                while (i < lines.Length && lines[i].Trim().Length > 0)
                {
                    var d = lines[i].Trim();
                    if (d.StartsWith("(") && d.EndsWith(")"))
                    {
                        FlushSpeech();
                        sp.Elements.Add(new ScreenElement(ElementType.Parenthetical, d));
                    }
                    else
                    {
                        speech.Add(d.TrimEnd('^').Trim());
                    }
                    i++;
                }
                FlushSpeech();
                prevBlank = false;
                continue;
            }

            // --- tutto il resto e' azione
            pendingAction.Add(t);
            prevBlank = false;
            i++;
        }

        FlushAction();
        return sp;
    }

    private static int ParseTitlePage(string[] lines, TitlePage tp)
    {
        int i = 0;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        if (i >= lines.Length) return i;

        // il frontespizio Fountain esiste solo se la prima riga utile e' "Chiave: valore"
        var first = lines[i];
        int colon = first.IndexOf(':');
        if (colon <= 0 || first.StartsWith(" ") || first.StartsWith("\t")) return 0;
        var firstKey = first.Substring(0, colon).Trim().ToLowerInvariant();
        if (!IsTitleKey(firstKey)) return 0;

        string key = null;
        var value = new List<string>();

        void Commit()
        {
            if (key == null) return;
            var v = string.Join(" ", value.Select(x => x.Trim()).Where(x => x.Length > 0)).Trim();
            switch (key)
            {
                case "title": tp.Title = v; break;
                case "credit": tp.Credit = v; break;
                case "author":
                case "authors": tp.Author = v; break;
                case "source": tp.Source = v; break;
                case "draft date":
                case "date": tp.DraftDate = v; break;
                case "contact": tp.Contact = v; break;
                case "copyright": tp.Copyright = v; break;
            }
            key = null;
            value.Clear();
        }

        while (i < lines.Length)
        {
            var raw = lines[i];
            if (raw.Trim().Length == 0) { i++; break; }

            bool indented = raw.StartsWith(" ") || raw.StartsWith("\t");
            int c = raw.IndexOf(':');
            if (!indented && c > 0)
            {
                Commit();
                key = raw.Substring(0, c).Trim().ToLowerInvariant();
                var rest = raw.Substring(c + 1).Trim();
                if (rest.Length > 0) value.Add(rest);
            }
            else
            {
                value.Add(raw.Trim());
            }
            i++;
        }
        Commit();
        return i;
    }

    private static bool IsTitleKey(string k) => k is "title" or "credit" or "author" or "authors"
        or "source" or "draft date" or "date" or "contact" or "copyright" or "notes";

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

    private static bool IsCharacterCue(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 60) return false;
        if (s.EndsWith(":")) return false;
        if (Screenplay.LooksLikeSceneHeading(s)) return false;
        if (Screenplay.TransitionPattern.IsMatch(s)) return false;
        return IsUpper(s);
    }

    private static Encoding DetectEncoding(string path)
    {
        using var fs = File.OpenRead(path);
        var bom = new byte[4];
        int n = fs.Read(bom, 0, 4);
        if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;
        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
        return Encoding.UTF8;
    }

    // ---------------------------------------------------------------- SCRITTURA

    public static void Save(string path, Screenplay sp)
        => File.WriteAllText(path, Write(sp), new UTF8Encoding(false));

    public static string Write(Screenplay sp)
    {
        var sb = new StringBuilder();
        var tp = sp.TitlePage;

        if (!tp.IsEmpty)
        {
            if (!string.IsNullOrWhiteSpace(tp.Title)) sb.Append("Title: ").Append(tp.Title).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.Credit)) sb.Append("Credit: ").Append(tp.Credit).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.Author)) sb.Append("Author: ").Append(tp.Author).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.Source)) sb.Append("Source: ").Append(tp.Source).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.DraftDate)) sb.Append("Draft date: ").Append(tp.DraftDate).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.Contact)) sb.Append("Contact: ").Append(tp.Contact).Append('\n');
            if (!string.IsNullOrWhiteSpace(tp.Copyright)) sb.Append("Copyright: ").Append(tp.Copyright).Append('\n');
            sb.Append('\n');
        }

        var els = sp.Compacted();
        ElementType prev = ElementType.Action;
        bool first = true;

        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];
            bool attached = !first &&
                            (e.Type == ElementType.Dialogue || e.Type == ElementType.Parenthetical) &&
                            (prev == ElementType.Character || prev == ElementType.Parenthetical || prev == ElementType.Dialogue);

            if (!first && !attached) sb.Append('\n');

            switch (e.Type)
            {
                case ElementType.SceneHeading:
                    sb.Append(Screenplay.LooksLikeSceneHeading(e.Text) ? e.Text : "." + e.Text);
                    break;
                case ElementType.Character:
                    sb.Append(IsUpper(e.Text) ? e.Text : "@" + e.Text);
                    break;
                case ElementType.Transition:
                    sb.Append(Screenplay.TransitionPattern.IsMatch(e.Text) && IsUpper(e.Text) ? e.Text : "> " + e.Text);
                    break;
                case ElementType.Action:
                    // un'azione tutta maiuscola verrebbe riletta come personaggio: forziamola
                    sb.Append(IsUpper(e.Text) || Screenplay.LooksLikeSceneHeading(e.Text) ? "!" + e.Text : e.Text);
                    break;
                default:
                    sb.Append(e.Text);
                    break;
            }
            sb.Append('\n');

            prev = e.Type;
            first = false;
        }

        return sb.ToString();
    }
}
