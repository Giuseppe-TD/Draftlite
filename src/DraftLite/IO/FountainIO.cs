using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Import/export Fountain (.fountain / .spmd / .txt). Formato testo puro, standard aperto,
/// lo legge anche Final Draft. Oltre agli elementi gestisce le estensioni standard che
/// DraftLite usa: note [[...]], sinossi con =, numeri di scena #12A# e dialogo simultaneo ^.
/// Le sezioni (#) vengono ignorate in lettura; il colore delle schede esiste solo nel .dlite.
/// </summary>
public static class FountainIO
{
    private static readonly Regex SceneNumberPattern = new Regex(@"\s*#([\w.\-]+)#\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineNotePattern = new Regex(@"\[\[(.+?)\]\]", RegexOptions.Compiled | RegexOptions.Singleline);

    // ---------------------------------------------------------------- LETTURA

    public static Screenplay Load(string path)
        => Parse(File.ReadAllText(path, DetectEncoding(path)));

    public static Screenplay Parse(string text)
    {
        var sp = new Screenplay();
        if (text == null) return sp;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        int i = ParseTitlePage(lines, sp.TitlePage);

        bool prevBlank = true;
        var pendingAction = new List<string>();
        var pendingNotes = new List<string>();

        ScreenElement Last() => sp.Elements.Count > 0 ? sp.Elements[sp.Elements.Count - 1] : null;

        void AttachNotes()
        {
            if (pendingNotes.Count == 0) return;
            var target = Last();
            if (target != null)
            {
                var joined = string.Join("\n", pendingNotes);
                target.Note = string.IsNullOrWhiteSpace(target.Note) ? joined : target.Note + "\n" + joined;
            }
            pendingNotes.Clear();
        }

        void FlushAction()
        {
            if (pendingAction.Count == 0) return;
            sp.Elements.Add(new ScreenElement(ElementType.Action, string.Join(" ", pendingAction).Trim()));
            pendingAction.Clear();
            AttachNotes();
        }

        /// estrae le note inline e restituisce il testo ripulito
        string TakeNotes(string s)
        {
            var m = InlineNotePattern.Matches(s);
            if (m.Count == 0) return s;
            foreach (Match x in m) pendingNotes.Add(x.Groups[1].Value.Trim());
            return InlineNotePattern.Replace(s, string.Empty).Trim();
        }

        while (i < lines.Length)
        {
            var raw = lines[i];
            var t = raw.Trim();

            if (t.Length == 0)
            {
                FlushAction();
                AttachNotes();
                prevBlank = true;
                i++;
                continue;
            }

            // nota su riga propria
            if (t.StartsWith("[[") && t.EndsWith("]]"))
            {
                pendingNotes.Add(t.Substring(2, t.Length - 4).Trim());
                if (pendingAction.Count == 0) AttachNotes();
                i++;
                continue;
            }

            // sinossi: appartiene alla scena appena letta
            if (t.StartsWith("="))
            {
                var syn = t.TrimStart('=').Trim();
                var target = sp.Elements.LastOrDefault(e => e.Type == ElementType.SceneHeading);
                if (target != null && syn.Length > 0)
                    target.Synopsis = string.IsNullOrWhiteSpace(target.Synopsis) ? syn : target.Synopsis + " " + syn;
                i++;
                continue;
            }

            // sezioni e altri blocchi che non ci interessano
            if (t.StartsWith("#") || t.StartsWith("~") || t == "===")
            {
                i++;
                prevBlank = false;
                continue;
            }

            t = TakeNotes(t);
            if (t.Length == 0) { i++; continue; }

            // --- forzature esplicite Fountain
            if (t.StartsWith(".") && !t.StartsWith(".."))
            {
                FlushAction();
                sp.Elements.Add(MakeScene(t.Substring(1).Trim()));
                AttachNotes();
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith("!"))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Action, t.Substring(1).Trim()));
                AttachNotes();
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith(">") && !t.EndsWith("<"))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Transition, t.Substring(1).Trim().ToUpperInvariant()));
                AttachNotes();
                i++; prevBlank = false; continue;
            }
            if (t.StartsWith(">") && t.EndsWith("<"))   // testo centrato: lo trattiamo come azione
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Action, t.Trim('>', '<').Trim()));
                AttachNotes();
                i++; prevBlank = false; continue;
            }

            bool forcedCharacter = t.StartsWith("@");
            string cue = forcedCharacter ? t.Substring(1).Trim() : t;

            // --- intestazione di scena
            if (prevBlank && !forcedCharacter && Screenplay.LooksLikeSceneHeading(t))
            {
                FlushAction();
                sp.Elements.Add(MakeScene(t));
                AttachNotes();
                i++; prevBlank = false; continue;
            }

            // --- transizione non forzata (TUTTA MAIUSCOLA e finisce con TO: o formula chiusa)
            if (prevBlank && !forcedCharacter && IsUpper(t) && Screenplay.TransitionPattern.IsMatch(t))
            {
                FlushAction();
                sp.Elements.Add(new ScreenElement(ElementType.Transition, t));
                AttachNotes();
                i++; prevBlank = false; continue;
            }

            // --- battuta: personaggio + eventuali parentetiche + dialogo
            bool nextNotBlank = (i + 1 < lines.Length) && lines[i + 1].Trim().Length > 0;
            if (prevBlank && nextNotBlank && (forcedCharacter || IsCharacterCue(cue)))
            {
                FlushAction();

                bool dual = cue.TrimEnd().EndsWith("^");
                if (dual) cue = cue.TrimEnd().TrimEnd('^').Trim();

                sp.Elements.Add(new ScreenElement(ElementType.Character, cue.ToUpperInvariant()) { Dual = dual });
                AttachNotes();
                i++;

                var speech = new List<string>();
                void FlushSpeech()
                {
                    if (speech.Count == 0) return;
                    sp.Elements.Add(new ScreenElement(ElementType.Dialogue, string.Join(" ", speech).Trim()));
                    speech.Clear();
                    AttachNotes();
                }

                while (i < lines.Length && lines[i].Trim().Length > 0)
                {
                    var d = TakeNotes(lines[i].Trim());
                    if (d.Length == 0) { i++; continue; }

                    if (d.StartsWith("(") && d.EndsWith(")"))
                    {
                        FlushSpeech();
                        sp.Elements.Add(new ScreenElement(ElementType.Parenthetical, d));
                        AttachNotes();
                    }
                    else
                    {
                        speech.Add(d);
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
        AttachNotes();
        return sp;
    }

    private static ScreenElement MakeScene(string text)
    {
        string number = null;
        var m = SceneNumberPattern.Match(text);
        if (m.Success)
        {
            number = m.Groups[1].Value;
            text = text.Substring(0, m.Index).Trim();
        }
        return new ScreenElement(ElementType.SceneHeading, text.ToUpperInvariant()) { Number = number };
    }

    private static int ParseTitlePage(string[] lines, TitlePage tp)
    {
        int i = 0;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        if (i >= lines.Length) return i;

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
        s = s.TrimEnd('^').Trim();
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

            // colori ed evidenziazioni non esistono in Fountain: restano solo nel .dlite
            e.Text = StyledText.WithoutColors(e.Text);

            switch (e.Type)
            {
                case ElementType.SceneHeading:
                    sb.Append(Screenplay.LooksLikeSceneHeading(e.Text) ? e.Text : "." + e.Text);
                    if (!string.IsNullOrWhiteSpace(e.Number)) sb.Append(" #").Append(e.Number.Trim()).Append('#');
                    break;

                case ElementType.Character:
                    sb.Append(IsUpper(e.Text) ? e.Text : "@" + e.Text);
                    if (e.Dual) sb.Append(" ^");
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

            if (!string.IsNullOrWhiteSpace(e.Note))
                foreach (var line in e.Note.Replace("\r", "").Split('\n'))
                    if (line.Trim().Length > 0)
                        sb.Append("[[").Append(line.Trim()).Append("]]").Append('\n');

            if (e.Type == ElementType.SceneHeading && !string.IsNullOrWhiteSpace(e.Synopsis))
                sb.Append("= ").Append(e.Synopsis.Replace("\n", " ").Trim()).Append('\n');

            prev = e.Type;
            first = false;
        }

        return sb.ToString();
    }
}
