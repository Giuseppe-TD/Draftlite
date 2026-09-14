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
/// Import/export .fdx (Final Draft). E' XML: leggiamo i Paragraph del Content
/// principale e scriviamo un file che Final Draft apre senza storcere il naso.
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
                var typeAttr = (string)p.Attribute("Type") ?? "Action";
                var text = ReadText(p);
                if (string.IsNullOrWhiteSpace(text)) continue;
                sp.Elements.Add(new ScreenElement(MapType(typeAttr), text));
            }
        }

        var tp = root.Elements().FirstOrDefault(e => e.Name.LocalName == "TitlePage");
        if (tp != null) ReadTitlePage(tp, sp.TitlePage);

        return sp;
    }

    private static string ReadText(XElement paragraph)
    {
        var sb = new StringBuilder();
        foreach (var t in paragraph.Descendants().Where(e => e.Name.LocalName == "Text"))
            sb.Append(t.Value);
        return sb.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static void ReadTitlePage(XElement tp, TitlePage target)
    {
        var rows = tp.Descendants()
                     .Where(e => e.Name.LocalName == "Paragraph")
                     .Select(ReadText)
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

    // ---------------------------------------------------------------- SCRITTURA

    public static void Save(string path, Screenplay sp)
    {
        var content = new XElement("Content");
        foreach (var e in sp.Compacted())
        {
            content.Add(new XElement("Paragraph",
                new XAttribute("Type", FdxType(e.Type)),
                new XElement("Text", e.Text)));
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
}
