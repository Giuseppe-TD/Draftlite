using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DraftLite.IO;

/// <summary>
/// Generatore PDF minimale, senza dipendenze esterne.
/// Usa i font standard PDF Courier / Courier-Bold / Courier-Oblique (i "base 14"):
/// nessun font da incorporare, nessun pacchetto NuGet, output identico ovunque.
/// E' esattamente quello che serve a una sceneggiatura: testo monospaziato a coordinate fisse.
/// </summary>
public sealed class PdfBuilder
{
    private const double FontSize = 12.0;
    private readonly List<MemoryStream> _pages = new List<MemoryStream>();
    private MemoryStream _cur;

    public double WidthPt { get; }
    public double HeightPt { get; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;

    public PdfBuilder(double widthPt, double heightPt)
    {
        WidthPt = widthPt;
        HeightPt = heightPt;
    }

    public void BeginPage()
    {
        _cur = new MemoryStream();
        _pages.Add(_cur);
    }

    /// <summary>Scrive testo con la baseline a yFromTop punti dal bordo superiore.</summary>
    public void DrawText(double xPt, double yFromTopPt, string text, bool bold = false, bool italic = false)
    {
        if (_cur == null) BeginPage();
        if (string.IsNullOrEmpty(text)) return;

        double y = HeightPt - yFromTopPt;
        string font = bold ? "F2" : italic ? "F3" : "F1";

        Raw("BT /" + font + " " + Num(FontSize) + " Tf 1 0 0 1 " + Num(xPt) + " " + Num(y) + " Tm ");
        WriteLiteral(text);
        Raw(" Tj ET\n");
    }

    private void Raw(string ascii)
    {
        var b = Encoding.ASCII.GetBytes(ascii);
        _cur.Write(b, 0, b.Length);
    }

    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Stringa PDF letterale in WinAnsiEncoding, con escape ottale per tutto il resto.</summary>
    private void WriteLiteral(string text)
    {
        _cur.WriteByte((byte)'(');
        foreach (var ch in text)
        {
            int code = ToWinAnsi(ch);
            if (code < 0) code = '?';
            if (code == '(' || code == ')' || code == '\\')
            {
                _cur.WriteByte((byte)'\\');
                _cur.WriteByte((byte)code);
            }
            else if (code < 32 || code > 126)
            {
                var oct = "\\" + Convert.ToString(code, 8).PadLeft(3, '0');
                foreach (var c in oct) _cur.WriteByte((byte)c);
            }
            else
            {
                _cur.WriteByte((byte)code);
            }
        }
        _cur.WriteByte((byte)')');
    }

    private static int ToWinAnsi(char ch)
    {
        if (ch < 128) return ch;
        switch (ch)
        {
            case '€': return 0x80; // euro
            case '‚': return 0x82;
            case 'ƒ': return 0x83;
            case '„': return 0x84;
            case '…': return 0x85; // ...
            case '†': return 0x86;
            case '‡': return 0x87;
            case 'ˆ': return 0x88;
            case '‰': return 0x89;
            case 'Š': return 0x8A;
            case '‹': return 0x8B;
            case 'Œ': return 0x8C;
            case 'Ž': return 0x8E;
            case '‘': return 0x91; // '
            case '’': return 0x92; // '
            case '“': return 0x93; // "
            case '”': return 0x94; // "
            case '•': return 0x95;
            case '–': return 0x96; // en dash
            case '—': return 0x97; // em dash
            case '˜': return 0x98;
            case '™': return 0x99;
            case 'š': return 0x9A;
            case '›': return 0x9B;
            case 'œ': return 0x9C;
            case 'ž': return 0x9E;
            case 'Ÿ': return 0x9F;
        }
        if (ch >= 0x00A0 && ch <= 0x00FF) return ch;   // Latin-1 coincide con WinAnsi
        return -1;
    }

    // ---------------------------------------------------------------- BUILD

    public byte[] Build()
    {
        var objects = new List<byte[]>();   // objects[0] => oggetto 1

        int pageCount = _pages.Count;
        int firstPageObj = 6;               // 1 catalog, 2 pages, 3..5 font

        // 1 - Catalog
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));

        // 2 - Pages
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(firstPageObj + i * 2).Append(" 0 R");
        }
        objects.Add(Ascii("<< /Type /Pages /Count " + pageCount + " /Kids [" + kids + "] >>"));

        // 3..5 - font standard
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Oblique /Encoding /WinAnsiEncoding >>"));

        string resources = "<< /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >>";
        string mediaBox = "[0 0 " + Num(WidthPt) + " " + Num(HeightPt) + "]";

        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = firstPageObj + i * 2;
            int contentObj = pageObj + 1;

            objects.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox " + mediaBox +
                              " /Resources " + resources + " /Contents " + contentObj + " 0 R >>"));

            var raw = _pages[i].ToArray();
            var deflated = Deflate(raw);
            var head = Ascii("<< /Length " + deflated.Length + " /Filter /FlateDecode >>\nstream\n");
            var tail = Ascii("\nendstream");
            objects.Add(Concat(head, deflated, tail));
        }

        // Info
        int infoObj = objects.Count + 1;
        objects.Add(Concat(Ascii("<< /Producer (DraftLite) /Creator (DraftLite) /Title "),
                           LiteralBytes(Title ?? string.Empty),
                           Ascii(" /Author "),
                           LiteralBytes(Author ?? string.Empty),
                           Ascii(" >>")));

        using var ms = new MemoryStream();
        void W(byte[] b) => ms.Write(b, 0, b.Length);
        void WS(string s) => W(Encoding.ASCII.GetBytes(s));

        WS("%PDF-1.4\n");
        W(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        var offsets = new long[objects.Count + 1];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = ms.Position;
            WS((i + 1) + " 0 obj\n");
            W(objects[i]);
            WS("\nendobj\n");
        }

        long xref = ms.Position;
        WS("xref\n0 " + (objects.Count + 1) + "\n");
        WS("0000000000 65535 f \n");
        for (int i = 1; i <= objects.Count; i++)
            WS(offsets[i].ToString("0000000000") + " 00000 n \n");

        WS("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /Info " + infoObj + " 0 R >>\n");
        WS("startxref\n" + xref + "\n%%EOF\n");

        return ms.ToArray();
    }

    private byte[] LiteralBytes(string s)
    {
        var save = _cur;
        _cur = new MemoryStream();
        WriteLiteral(s);
        var result = _cur.ToArray();
        _cur = save;
        return result;
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var r = new byte[len];
        int o = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
        return r;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var outMs = new MemoryStream();
        using (var z = new ZLibStream(outMs, CompressionLevel.Optimal, true))
            z.Write(data, 0, data.Length);
        return outMs.ToArray();
    }
}
