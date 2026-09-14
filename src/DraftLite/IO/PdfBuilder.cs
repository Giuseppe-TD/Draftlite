using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using DraftLite.Model;

namespace DraftLite.IO;

/// <summary>
/// Generatore PDF minimale, senza dipendenze esterne.
/// Usa i font standard PDF Courier / Courier-Bold / Courier-Oblique (i "base 14"):
/// nessun font da incorporare, nessun pacchetto NuGet, output identico ovunque.
/// E' esattamente quello che serve a una sceneggiatura: testo monospaziato a coordinate fisse.
/// </summary>
public sealed class PdfBuilder
{
    public const double DefaultFontSize = 12.0;

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

    public int PageCount => _pages.Count;

    public void BeginPage()
    {
        _cur = new MemoryStream();
        _pages.Add(_cur);
    }

    /// <summary>Scrive testo con la baseline a yFromTop punti dal bordo superiore.</summary>
    public void DrawText(double xPt, double yFromTopPt, string text,
                         bool bold = false, bool italic = false,
                         double gray = 0.0, double fontSize = DefaultFontSize)
    {
        if (_cur == null) BeginPage();
        if (string.IsNullOrEmpty(text)) return;

        double y = HeightPt - yFromTopPt;
        string font = FontFor(bold, italic);

        Raw("q ");
        if (gray > 0) Raw(Num(gray) + " g ");
        Raw("BT /" + font + " " + Num(fontSize) + " Tf 1 0 0 1 " + Num(xPt) + " " + Num(y) + " Tm ");
        WriteLiteral(text);
        Raw(" Tj ET Q\n");
    }

    private static string FontFor(bool bold, bool italic)
        => bold && italic ? "F4" : bold ? "F2" : italic ? "F3" : "F1";

    /// <summary>
    /// Una riga fatta di pezzi con formattazione diversa: ogni pezzo avanza di
    /// una larghezza fissa, perche' il Courier ha il passo costante.
    /// </summary>
    public void DrawRuns(double xPt, double yFromTopPt, IList<TextRun> runs,
                         bool forceBold = false, double fontSize = DefaultFontSize)
    {
        if (runs == null || runs.Count == 0) return;
        double charWidth = fontSize * 0.6;
        double x = xPt;

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;
            DrawText(x, yFromTopPt, run.Text, forceBold || run.Bold, run.Italic, 0, fontSize);

            double w = run.Text.Length * charWidth;
            if (run.Underline && run.Text.Trim().Length > 0)
                DrawLine(x, yFromTopPt + fontSize * 0.16, x + w, yFromTopPt + fontSize * 0.16, 0, 0.6);

            x += w;
        }
    }

    /// <summary>Testo ruotato attorno al suo punto iniziale: serve per la filigrana in diagonale.</summary>
    public void DrawRotatedText(double xPt, double yFromTopPt, string text, double angleDeg,
                                double fontSize, double gray, bool bold = true)
    {
        if (_cur == null) BeginPage();
        if (string.IsNullOrEmpty(text)) return;

        double y = HeightPt - yFromTopPt;
        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        string font = bold ? "F2" : "F1";

        Raw("q ");
        Raw(Num(gray) + " g ");
        Raw("BT /" + font + " " + Num(fontSize) + " Tf ");
        Raw(Num(cos) + " " + Num(sin) + " " + Num(-sin) + " " + Num(cos) + " " + Num(xPt) + " " + Num(y) + " Tm ");
        WriteLiteral(text);
        Raw(" Tj ET Q\n");
    }

    /// <summary>Riga orizzontale sottile (usata dai report).</summary>
    public void DrawLine(double x1, double yFromTop1, double x2, double yFromTop2, double gray = 0.6, double width = 0.5)
    {
        if (_cur == null) BeginPage();
        Raw("q " + Num(gray) + " G " + Num(width) + " w " +
            Num(x1) + " " + Num(HeightPt - yFromTop1) + " m " +
            Num(x2) + " " + Num(HeightPt - yFromTop2) + " l S Q\n");
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
            case '‘': return 0x91;
            case '’': return 0x92;
            case '“': return 0x93;
            case '”': return 0x94;
            case '•': return 0x95;
            case '–': return 0x96;
            case '—': return 0x97;
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
        var objects = new List<byte[]>();

        int pageCount = _pages.Count;
        int firstPageObj = 7;   // 1 catalog, 2 pages, 3..6 font

        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));

        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(firstPageObj + i * 2).Append(" 0 R");
        }
        objects.Add(Ascii("<< /Type /Pages /Count " + pageCount + " /Kids [" + kids + "] >>"));

        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Oblique /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-BoldOblique /Encoding /WinAnsiEncoding >>"));

        string resources = "<< /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R >> >>";
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
