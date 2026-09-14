using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Import/export .fdx e .fdxt (Final Draft, anche i modelli). E' XML: leggiamo i Paragraph
/// del Content principale e scriviamo un file che Final Draft apre senza storcere il naso.
/// Vengono mantenuti anche numeri di scena, note (ScriptNote), sinossi e colore scheda
/// (SceneProperties) e il dialogo simultaneo (DualDialogue).
/// Gli elementi che DraftLite non gestisce (Shot, General, ecc.) diventano Azione.
/// </summary>
public static class FdxIO
{
    // ---------------------------------------------------------------- LETTURA

    public static Screenplay Load(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root == null || !string.Equals(root.Name.LocalName, "FinalDraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il file non sembra un documento Final Draft (.fdx).");

        var sp = new Screenplay();

        var content = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Content");
        if (content != null)
        {
            foreach (var p in content.Descendants().Where(e => e.Name.LocalName == "Paragraph"))
            {
                // i Paragraph dentro ScriptNote/Summary sono contenuto delle note, non elementi
                if (p.Ancestors().Any(a => a.Name.LocalName is "ScriptNote" or "Summary")) continue;

                var typeAttr = (string)p.Attribute("Type") ?? "Action";
                var text = ReadOwnText(p);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var el = new ScreenElement(MapType(typeAttr), text)
                {
                    Number = (string)p.Attribute("Number"),
                    Note = ReadNote(p)
                };

                var props = p.Elements().FirstOrDefault(x => x.Name.LocalName == "SceneProperties");
                if (props != null)
                {
                    var title = (string)props.Attribute("Title");
                    if (!string.IsNullOrWhiteSpace(title)) el.Synopsis = title.Trim();

                    var summary = props.Descendants().Where(x => x.Name.LocalName == "Text").Select(x => x.Value);
                    var longText = string.Join(" ", summary).Trim();
                    if (longText.Length > 0)
                        el.Synopsis = string.IsNullOrWhiteSpace(el.Synopsis) ? longText : el.Synopsis + " - " + longText;

                    var color = FromFdxColor((string)props.Attribute("Color"));
                    if (color != null) el.Color = color;
                }

                // il secondo parlante dentro un blocco DualDialogue e' quello affiancato
                if (el.Type == ElementType.Character &&
                    p.Ancestors().Any(a => a.Name.LocalName == "DualDialogue"))
                {
                    var group = p.Ancestors().First(a => a.Name.LocalName == "DualDialogue");
                    var firstChar = group.Descendants()
                        .First(x => x.Name.LocalName == "Paragraph" &&
                                    ((string)x.Attribute("Type") ?? "") == "Character");
                    if (!ReferenceEquals(firstChar, p)) el.Dual = true;
                }

                sp.Elements.Add(el);
            }
        }

        var tp = root.Elements().FirstOrDefault(e => e.Name.LocalName == "TitlePage");
        if (tp != null) ReadTitlePage(tp, sp.TitlePage);

        return sp;
    }

    /// <summary>Testo del paragrafo, escluso quello che appartiene a note e sinossi.</summary>
    private static string ReadOwnText(XElement paragraph)
    {
        var sb = new StringBuilder();
        foreach (var t in paragraph.Elements().Where(e => e.Name.LocalName == "Text"))
            sb.Append(t.Value);
        return sb.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string ReadNote(XElement paragraph)
    {
        var notes = paragraph.Elements().Where(e => e.Name.LocalName == "ScriptNote").ToList();
        if (notes.Count == 0) return null;

        var parts = new List<string>();
        foreach (var n in notes)
        {
            var text = string.Join(" ", n.Descendants()
                .Where(x => x.Name.LocalName == "Text")
                .Select(x => x.Value)).Trim();
            if (text.Length > 0) parts.Add(text);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static void ReadTitlePage(XElement tp, TitlePage target)
    {
        var rows = tp.Descendants()
                     .Where(e => e.Name.LocalName == "Paragraph")
                     .Select(ReadOwnText)
                     .Where(s => s.Length > 0)
                     .ToList();
        if (rows.Count == 0) return;

        target.Title = rows[0];
        for (int i = 1; i < rows.Count; i++)
        {
            var low = rows[i].ToLowerInvariant();
            if (low is "scritto da" or "written by" or "di" or "by" or "una sceneggiatura di")
            {
                target.Credit = rows[i];
                if (i + 1 < rows.Count) { target.Author = rows[i + 1]; i++; }
            }
            else if (string.IsNullOrWhiteSpace(target.Author) && i == 1)
            {
                target.Author = rows[i];
            }
            else if (string.IsNullOrWhiteSpace(target.Contact))
            {
                target.Contact = rows[i];
            }
        }
    }

    private static ElementType MapType(string t) => t.Trim().ToLowerInvariant() switch
    {
        "scene heading" => ElementType.SceneHeading,
        "action" => ElementType.Action,
        "character" => ElementType.Character,
        "parenthetical" => ElementType.Parenthetical,
        "dialogue" => ElementType.Dialogue,
        "transition" => ElementType.Transition,
        _ => ElementType.Action
    };

    private static string FdxType(ElementType t) => t switch
    {
        ElementType.SceneHeading => "Scene Heading",
        ElementType.Action => "Action",
        ElementType.Character => "Character",
        ElementType.Parenthetical => "Parenthetical",
        ElementType.Dialogue => "Dialogue",
        ElementType.Transition => "Transition",
        _ => "Action"
    };

    /// <summary>Final Draft usa #AARRGGBB; noi #RRGGBB.</summary>
    private static string FromFdxColor(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return null;
        c = c.Trim().TrimStart('#');
        if (c.Length == 8) c = c.Substring(2);
        if (c.Length != 6) return null;
        return "#" + c.ToUpperInvariant();
    }

    private static string ToFdxColor(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return null;
        c = c.Trim().TrimStart('#');
        if (c.Length == 6) return "#FF" + c.ToUpperInvariant();
        if (c.Length == 8) return "#" + c.ToUpperInvariant();
        return null;
    }

    // ---------------------------------------------------------------- SCRITTURA

    public static void Save(string path, Screenplay sp)
    {
        var content = new XElement("Content");
        var els = sp.Compacted();

        XElement BuildParagraph(ScreenElement e)
        {
            var p = new XElement("Paragraph", new XAttribute("Type", FdxType(e.Type)));
            if (!string.IsNullOrWhiteSpace(e.Number)) p.Add(new XAttribute("Number", e.Number.Trim()));

            if (e.Type == ElementType.SceneHeading &&
                (!string.IsNullOrWhiteSpace(e.Synopsis) || !string.IsNullOrWhiteSpace(e.Color)))
            {
                var props = new XElement("SceneProperties");
                if (!string.IsNullOrWhiteSpace(e.Synopsis)) props.Add(new XAttribute("Title", e.Synopsis.Trim()));
                var col = ToFdxColor(e.Color);
                if (col != null) props.Add(new XAttribute("Color", col));
                p.Add(props);
            }

            p.Add(new XElement("Text", e.Text));

            if (!string.IsNullOrWhiteSpace(e.Note))
            {
                p.Add(new XElement("ScriptNote",
                    new XElement("Paragraph",
                        new XElement("Text", e.Note.Replace("\r", "").Replace('\n', ' ')))));
            }
            return p;
        }

        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];

            // blocco di dialogo simultaneo: il parlante marcato Dual sta insieme al precedente
            if (e.Type == ElementType.Character && e.Dual && content.Elements().Any())
            {
                int startPrev = FindPreviousCharacterStart(content);
                if (startPrev >= 0)
                {
                    var moved = content.Elements().Skip(startPrev).ToList();
                    foreach (var m in moved) m.Remove();

                    var dual = new XElement("DualDialogue");
                    foreach (var m in moved) dual.Add(m);
                    dual.Add(BuildParagraph(e));

                    int j = i + 1;
                    while (j < els.Count &&
                           (els[j].Type == ElementType.Dialogue || els[j].Type == ElementType.Parenthetical))
                    {
                        dual.Add(BuildParagraph(els[j]));
                        j++;
                    }
                    content.Add(dual);
                    i = j - 1;
                    continue;
                }
            }

            content.Add(BuildParagraph(e));
        }

        var root = new XElement("FinalDraft",
            new XAttribute("DocumentType", "Script"),
            new XAttribute("Template", "No"),
            new XAttribute("Version", "1"),
            content);

        var tp = sp.TitlePage;
        if (!tp.IsEmpty)
        {
            var tpContent = new XElement("Content");
            void Row(string s, string align = "Center")
            {
                tpContent.Add(new XElement("Paragraph",
                    new XAttribute("Alignment", align),
                    new XElement("Text", s ?? string.Empty)));
            }

            Row(string.Empty); Row(string.Empty); Row(string.Empty);
            Row(string.Empty); Row(string.Empty); Row(string.Empty);
            Row((tp.Title ?? string.Empty).ToUpperInvariant());
            Row(string.Empty);
            if (!string.IsNullOrWhiteSpace(tp.Credit)) Row(tp.Credit);
            if (!string.IsNullOrWhiteSpace(tp.Author)) Row(tp.Author);
            if (!string.IsNullOrWhiteSpace(tp.Source)) { Row(string.Empty); Row(tp.Source); }
            if (!string.IsNullOrWhiteSpace(tp.DraftDate)) { Row(string.Empty); Row(tp.DraftDate); }
            if (!string.IsNullOrWhiteSpace(tp.Contact)) { Row(string.Empty); Row(tp.Contact, "Left"); }
            if (!string.IsNullOrWhiteSpace(tp.Copyright)) Row(tp.Copyright, "Left");

            root.Add(new XElement("TitlePage", tpContent));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "no"), root);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false)
        };
        using var w = XmlWriter.Create(path, settings);
        doc.Save(w);
    }

    /// <summary>Indice del Paragraph "Character" che apre l'ultima battuta scritta.</summary>
    private static int FindPreviousCharacterStart(XElement content)
    {
        var items = content.Elements().ToList();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var type = (string)items[i].Attribute("Type") ?? string.Empty;
            if (type == "Character") return i;
            if (type is "Dialogue" or "Parenthetical") continue;
            return -1;
        }
        return -1;
    }
}
