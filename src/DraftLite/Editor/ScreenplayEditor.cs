using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.Editor;

/// <summary>
/// Il cuore di DraftLite: un RichTextBox che sa cos'e' una sceneggiatura.
/// Ogni paragrafo e' un elemento; il tipo dell'elemento e' codificato nella
/// formattazione del paragrafo (rientro + spazio prima + allineamento), quindi
/// resta corretto anche dopo copia/incolla, annulla e ripristina.
/// </summary>
public sealed class ScreenplayEditor : RichTextBox
{
    private Font _fontNormal;
    private Font _fontBold;
    private readonly ListBox _suggest;
    private readonly ListBox _chooser;
    private readonly List<string> _suggestValues = new List<string>();
    private readonly System.Windows.Forms.Timer _indexTimer;

    private List<string> _characters = new List<string>();
    private List<string> _sceneHeadings = new List<string>();
    private bool _internal;
    private ElementType _lastType = ElementType.Action;
    private string _textCache;
    private bool _textCacheValid;
    private bool _fromTyping;
    private bool _indexValid;
    private List<(int CharIndex, int Number, string Text)> _sceneIndex = new List<(int, int, string)>();

    // dati ancorati ai paragrafi (note, sinossi, colore scheda, numero di scena):
    // non stanno nel testo, quindi vanno riallineati quando il testo cambia
    private List<ElementMeta> _meta = new List<ElementMeta>();
    private List<string> _metaKeys = new List<string>();
    private readonly Dictionary<string, ElementMeta> _orphanMeta = new Dictionary<string, ElementMeta>();

    private bool _typewriter;
    private bool _hadNotes;
    private Color _noteBack = Color.FromArgb(255, 246, 200);

    /// <summary>Colore del testo per ogni tipo di elemento (li decide il tema).</summary>
    private readonly Dictionary<ElementType, Color> _elementColors = new Dictionary<ElementType, Color>
    {
        [ElementType.SceneHeading] = Color.Black,
        [ElementType.Action] = Color.Black,
        [ElementType.Character] = Color.Black,
        [ElementType.Parenthetical] = Color.Black,
        [ElementType.Dialogue] = Color.Black,
        [ElementType.Transition] = Color.Black
    };

    /// <summary>Chiede quale elemento inserire quando si va a capo.</summary>
    public bool AskElementOnEnter { get; set; } = true;

    /// <summary>
    /// Copia del testo valida fino alla prossima modifica: leggere Text chiama Windows
    /// ogni volta, e in un copione da centomila caratteri farlo a ogni tasto si sente.
    /// </summary>
    private string CachedText
    {
        get
        {
            if (!_textCacheValid) { _textCache = Text; _textCacheValid = true; }
            return _textCache;
        }
    }

    public static readonly string[] TimesOfDay =
    {
        "GIORNO", "NOTTE", "MATTINA", "POMERIGGIO", "SERA", "ALBA", "TRAMONTO",
        "PIU' TARDI", "CONTINUA", "FLASHBACK"
    };

    public static readonly string[] ScenePrefixes =
    {
        "INT. ", "EST. ", "INT./EST. "
    };

    /// <summary>Estensioni rapide per il nome personaggio.</summary>
    public static readonly string[] CharacterExtensions =
    {
        "(V.O.)", "(F.C.)", "(O.S.)", "(CONT'D)"
    };

    public event EventHandler CurrentTypeChanged;
    /// <summary>Scatta dopo il reindex (personaggi, scene) a digitazione ferma.</summary>
    public event EventHandler DocumentIndexed;

    public ScreenplayEditor()
    {
        _fontNormal = new Font("Courier New", 12f, FontStyle.Regular, GraphicsUnit.Point);
        _fontBold = new Font("Courier New", 12f, FontStyle.Bold, GraphicsUnit.Point);

        Font = _fontNormal;
        WordWrap = true;
        AcceptsTab = true;
        DetectUrls = false;
        BorderStyle = BorderStyle.None;
        ScrollBars = RichTextBoxScrollBars.Vertical;
        HideSelection = false;
        BackColor = Color.White;
        ForeColor = Color.Black;
        AutoWordSelection = false;

        _suggest = new ListBox
        {
            Visible = false,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            Width = 260,
            Height = 120
        };
        _suggest.Click += (s, e) => { AcceptSuggestion(); Focus(); };

        _chooser = new ListBox
        {
            Visible = false,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            Width = 210,
            Height = 130
        };
        _chooser.Click += (s, e) => { AcceptChooser(); Focus(); };

        _indexTimer = new System.Windows.Forms.Timer { Interval = 900 };
        _indexTimer.Tick += (s, e) => { _indexTimer.Stop(); RebuildIndex(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyPageWidth();
        if (TextLength == 0) ApplyTypeToCurrentParagraph(ElementType.SceneHeading, false);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        AttachPopup();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        AttachPopup();
    }

    /// <summary>Il popup dei suggerimenti vive sul form, cosi' non viene tagliato dai pannelli.</summary>
    private void AttachPopup()
    {
        var host = (Control)FindForm() ?? Parent;
        if (host == null || _suggest.Parent == host) return;

        _suggest.Parent?.Controls.Remove(_suggest);
        host.Controls.Add(_suggest);
        _suggest.Visible = false;
        _suggest.BringToFront();

        _chooser.Parent?.Controls.Remove(_chooser);
        host.Controls.Add(_chooser);
        _chooser.Visible = false;
        _chooser.BringToFront();
    }

    /// <summary>Larghezza di riga = 6 pollici, come sulla pagina stampata (60 caratteri).</summary>
    public void ApplyPageWidth()
    {
        if (!IsHandleCreated) return;
        // La larghezza di riga si imposta direttamente in twip (6" = 8640):
        // RichTextBox.RightMargin vorrebbe pixel e li converte con il DPI del display,
        // che su schermi scalati non e' quello del controllo.
        int twips = (int)Math.Round(ElementStyle.TextWidthInch * ElementStyle.TwipsPerInch);
        NativeMethods.SendMessage(Handle, NativeMethods.EM_SETTARGETDEVICE, IntPtr.Zero, (IntPtr)twips);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyPageWidth();
        ReapplySceneBold();
    }

    /// <summary>
    /// Cambiando DPI, WinForms riassegna il Font a tutto il documento e cancella il
    /// grassetto delle intestazioni di scena: il tipo resta (sta nel paragrafo), l'aspetto no.
    /// </summary>
    private void ReapplySceneBold()
    {
        if (!IsHandleCreated) return;
        int selStart = SelectionStart, selLen = SelectionLength;
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            foreach (var r in ScanUpperLines())
            {
                if (r.Type != ElementType.SceneHeading) continue;
                Select(r.CharIndex, r.Text.Length);
                SelectionFont = _fontBold;
            }
        }
        finally
        {
            Select(selStart, selLen);
            RestoreCaretFormat(selLen);
            ResumeDrawing();
            _internal = saved;
        }
    }

    public int PageWidthPixels => (int)Math.Round((ElementStyle.TextWidthInch + 0.45) * DeviceDpi * ZoomFactor);

    // ------------------------------------------------------------------ TIPI

    private PARAFORMAT2 GetParaFormat()
    {
        var pf = PARAFORMAT2.Create();
        NativeMethods.SendMessage(Handle, NativeMethods.EM_GETPARAFORMAT,
            (IntPtr)NativeMethods.SCF_SELECTION, ref pf);
        return pf;
    }

    private void SetParaFormat(ElementStyle st)
    {
        var pf = PARAFORMAT2.Create();
        pf.dwMask = NativeMethods.PFM_STARTINDENT | NativeMethods.PFM_RIGHTINDENT |
                    NativeMethods.PFM_OFFSET | NativeMethods.PFM_ALIGNMENT |
                    NativeMethods.PFM_SPACEBEFORE | NativeMethods.PFM_SPACEAFTER;
        pf.dxStartIndent = st.LeftTwips;
        pf.dxRightIndent = st.RightTwips;
        pf.dxOffset = 0;
        pf.dySpaceBefore = st.SpaceBeforeTwips;
        pf.dySpaceAfter = 0;
        pf.wAlignment = st.RightAlign ? NativeMethods.PFA_RIGHT : NativeMethods.PFA_LEFT;
        NativeMethods.SendMessage(Handle, NativeMethods.EM_SETPARAFORMAT,
            (IntPtr)NativeMethods.SCF_SELECTION, ref pf);
    }

    private static ElementType TypeFromParaFormat(PARAFORMAT2 pf)
    {
        if (pf.wAlignment == NativeMethods.PFA_RIGHT) return ElementType.Transition;

        int ind = pf.dxStartIndent;
        const int tol = 120;

        if (Math.Abs(ind - ElementStyle.Get(ElementType.Character).LeftTwips) < tol) return ElementType.Character;
        if (Math.Abs(ind - ElementStyle.Get(ElementType.Parenthetical).LeftTwips) < tol) return ElementType.Parenthetical;
        if (Math.Abs(ind - ElementStyle.Get(ElementType.Dialogue).LeftTwips) < tol) return ElementType.Dialogue;

        if (ind < tol)
            return pf.dySpaceBefore >= 360 ? ElementType.SceneHeading : ElementType.Action;

        return ElementType.Action;
    }

    public ElementType CurrentType
    {
        get
        {
            if (!IsHandleCreated) return ElementType.Action;
            int s = SelectionStart, l = SelectionLength;
            if (l == 0) return TypeFromParaFormat(GetParaFormat());

            // con una selezione aperta leggiamo il formato del primo paragrafo:
            // il flag evita che il collasso temporaneo rientri negli eventi
            bool saved = _internal;
            _internal = true;
            try
            {
                Select(s, 0);
                var t = TypeFromParaFormat(GetParaFormat());
                Select(s, l);
                return t;
            }
            finally { _internal = saved; }
        }
    }

    /// <summary>Applica un tipo a tutti i paragrafi toccati dalla selezione.</summary>
    public void ApplyType(ElementType type)
    {
        if (!IsHandleCreated) return;
        int selStart = SelectionStart, selLen = SelectionLength;

        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            if (selLen == 0)
            {
                ApplyTypeToCurrentParagraph(type, true);
                Select(Math.Min(selStart, TextLength), 0);
            }
            else
            {
                // gli offset si calcolano una volta sola: passare a maiuscolo non
                // cambia la lunghezza del testo, quindi restano validi per tutto il giro
                var t = CachedText;
                int from = ParagraphStartAt(t, selStart);
                int last = ParagraphStartAt(t, Math.Min(selStart + selLen, t.Length));

                var blocks = new List<(int Start, string Text)>();
                int p = from;
                while (p <= last)
                {
                    int end = ParagraphEndAt(t, p);
                    blocks.Add((p, t.Substring(p, end - p)));
                    if (end >= t.Length) break;
                    p = end + 1;
                }

                foreach (var b in blocks)
                    ApplyTypeToParagraph(b.Start, b.Text, type, true);

                Select(selStart, Math.Min(selLen, Math.Max(0, TextLength - selStart)));
            }
        }
        finally
        {
            ResumeDrawing();
            _internal = saved;
        }
        RaiseTypeChanged(true);
    }

    private void ApplyTypeToCurrentParagraph(ElementType type, bool convertCase)
    {
        var t = CachedText;
        int caret = Math.Min(SelectionStart, t.Length);
        int start = ParagraphStartAt(t, caret);
        int end = ParagraphEndAt(t, caret);

        ApplyTypeToParagraph(start, t.Substring(start, end - start), type, convertCase);

        var st = ElementStyle.Get(type);
        Select(Math.Min(caret, TextLength), 0);
        SelectionFont = st.Bold ? _fontBold : _fontNormal;
        if (_elementColors.TryGetValue(type, out var caretColor)) SelectionColor = caretColor;
    }

    private void ApplyTypeToParagraph(int start, string text, ElementType type, bool convertCase)
    {
        var st = ElementStyle.Get(type);

        if (convertCase && st.UpperCase)
        {
            var up = text.ToUpperInvariant();
            if (up != text && up.Length == text.Length)
            {
                bool wasInternal = _internal;
                _internal = true;
                Select(start, text.Length);
                SelectedText = up;
                _internal = wasInternal;
                text = up;
            }
        }

        Select(start, text.Length);
        SetParaFormat(st);
        SelectionFont = st.Bold ? _fontBold : _fontNormal;
        if (_elementColors.TryGetValue(type, out var color)) SelectionColor = color;
    }

    private static int ParagraphStartAt(string t, int index)
    {
        if (index > t.Length) index = t.Length;
        int s = index;
        while (s > 0 && t[s - 1] != '\n') s--;
        return s;
    }

    private static int ParagraphEndAt(string t, int index)
    {
        if (index > t.Length) index = t.Length;
        int e = index;
        while (e < t.Length && t[e] != '\n') e++;
        return e;
    }

    public string CurrentParagraphText
    {
        get
        {
            var t = CachedText;
            int caret = Math.Min(SelectionStart, t.Length);
            int s = ParagraphStartAt(t, caret);
            int e = ParagraphEndAt(t, caret);
            return t.Substring(s, e - s);
        }
    }

    // ------------------------------------------------------------------ CONTENUTO

    public List<ScreenElement> GetElements()
    {
        var result = new List<ScreenElement>();
        var t = CachedText;
        var lines = t.Split('\n');
        if (!IsHandleCreated)
        {
            foreach (var l in lines) result.Add(new ScreenElement(ElementType.Action, l));
            return result;
        }

        SyncMeta();

        int selStart = SelectionStart, selLen = SelectionLength;
        var pending = selLen == 0 ? SelectionFont : null;
        var scroll = GetScrollPos();
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            int pos = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                Select(pos, 0);
                var type = TypeFromParaFormat(GetParaFormat());

                // il dialogo simultaneo vive nel testo come "^" finale, alla maniera di Fountain:
                // cosi' sopravvive a copia, incolla e annulla senza bisogno di agganci
                bool dual = type == ElementType.Character && line.TrimEnd().EndsWith("^");
                var readable = dual ? line.TrimEnd().TrimEnd('^').TrimEnd() : line;

                _readingType = type;
                var el = new ScreenElement(type, ReadStyledText(pos, readable, ElementStyle.Get(type).Bold))
                {
                    Dual = dual
                };

                if (i < _meta.Count) el.ApplyMeta(_meta[i]);
                result.Add(el);
                pos += line.Length + 1;
            }
        }
        finally
        {
            Select(selStart, selLen);
            SetScrollPos(scroll);
            RestoreCaretFormat(selLen, pending);
            pending?.Dispose();
            ResumeDrawing();
            _internal = saved;
        }
        return result;
    }

    /// <summary>
    /// Rilegge un paragrafo tenendosi il grassetto, il corsivo e il sottolineato
    /// messi a mano, e li riscrive come marcatori nel testo. Quasi sempre il
    /// paragrafo ha un solo stile: in quel caso basta una lettura sola.
    /// </summary>
    private string ReadStyledText(int start, string plain, bool baseBold)
    {
        if (string.IsNullOrEmpty(plain)) return plain;

        var runs = new List<TextRun>();
        var typeColor = _elementColors.TryGetValue(_readingType, out var tc) ? tc : ForeColor;
        ReadRange(start, 0, plain.Length, plain, baseBold, typeColor, runs);
        return StyledText.Write(runs);
    }

    private ElementType _readingType = ElementType.Action;

    /// <summary>
    /// Trova i confini degli stili dimezzando l'intervallo: se un tratto ha un formato
    /// solo, Windows lo dice con una lettura sola. Cosi' un paragrafo costa poche letture
    /// invece di una per carattere.
    /// </summary>
    private void ReadRange(int origin, int from, int len, string plain, bool baseBold,
                           Color typeColor, List<TextRun> runs)
    {
        if (len <= 0) return;

        Select(origin + from, len);
        var font = SelectionFont;
        var color = SelectionColor;
        var back = SelectionBackColor;

        // un tratto uniforme si legge in un colpo solo; se e' misto lo si dimezza
        bool uniform = font != null && !color.IsEmpty && !back.IsEmpty;

        if (uniform || len == 1)
        {
            bool b = font != null && font.Bold && !baseBold;
            bool it = font != null && font.Italic;
            bool u = font != null && font.Underline;
            font?.Dispose();

            string userColor = (!color.IsEmpty && color.ToArgb() != typeColor.ToArgb() &&
                                color.ToArgb() != ForeColor.ToArgb()) ? ToHex(color) : null;

            string highlight = (!back.IsEmpty && back.ToArgb() != BackColor.ToArgb() &&
                                back.ToArgb() != _noteBack.ToArgb()) ? ToHex(back) : null;

            var piece = plain.Substring(from, len);
            var candidate = new TextRun(piece, b, it, u, userColor, highlight);
            var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
            if (last != null && last.SameStyle(candidate)) last.Text += piece;
            else runs.Add(candidate);
            return;
        }

        font?.Dispose();
        int half = len / 2;
        ReadRange(origin, from, half, plain, baseBold, typeColor, runs);
        ReadRange(origin, from + half, len - half, plain, baseBold, typeColor, runs);
    }

    /// <summary>
    /// Grassetto, corsivo o sottolineato sul testo selezionato (o sul punto in cui si sta
    /// per scrivere). Si tocca solo quel singolo attributo: colore, carattere e gli altri
    /// stili restano dove sono, e basta un messaggio solo anche su selezioni enormi.
    /// </summary>
    public void ToggleStyle(FontStyle style)
    {
        if (!IsHandleCreated) return;

        uint mask = style == FontStyle.Bold ? NativeMethods.CFM_BOLD
                  : style == FontStyle.Italic ? NativeMethods.CFM_ITALIC
                  : NativeMethods.CFM_UNDERLINE;

        var cur = CHARFORMAT2.Create();
        NativeMethods.SendMessage(Handle, NativeMethods.EM_GETCHARFORMAT,
            (IntPtr)NativeMethods.SCF_SELECTION, ref cur);

        // attivo per tutta la selezione? allora si spegne, altrimenti si accende
        bool uniformlyOn = (cur.dwMask & mask) != 0 && (cur.dwEffects & mask) != 0;

        var cf = CHARFORMAT2.Create();
        cf.dwMask = mask;
        cf.dwEffects = uniformlyOn ? 0u : mask;
        NativeMethods.SendMessage(Handle, NativeMethods.EM_SETCHARFORMAT,
            (IntPtr)NativeMethods.SCF_SELECTION, ref cf);

        _textCacheValid = false;
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Colore del testo sulla selezione. Null rimette quello del tipo di elemento.</summary>
    public void ApplyTextColor(Color? color)
    {
        if (!IsHandleCreated) return;
        if (SelectionLength == 0 && color == null) return;

        var type = CurrentType;
        var fallback = _elementColors.TryGetValue(type, out var c) ? c : ForeColor;
        SelectionColor = color ?? fallback;

        _textCacheValid = false;
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Evidenziatore sulla selezione. Null lo toglie.</summary>
    public void ApplyHighlight(Color? color)
    {
        if (!IsHandleCreated || SelectionLength == 0) return;
        SelectionBackColor = color ?? BackColor;

        _textCacheValid = false;
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Colore e evidenziatore sotto il cursore, per accendere i pulsanti giusti.</summary>
    public (Color? Text, Color? Highlight) CurrentColors()
    {
        if (!IsHandleCreated) return (null, null);
        var type = CurrentType;
        var typeColor = _elementColors.TryGetValue(type, out var c) ? c : ForeColor;

        var col = SelectionColor;
        var back = SelectionBackColor;
        return (!col.IsEmpty && col.ToArgb() != typeColor.ToArgb() ? col : (Color?)null,
                !back.IsEmpty && back.ToArgb() != BackColor.ToArgb() && back.ToArgb() != _noteBack.ToArgb()
                    ? back : (Color?)null);
    }

    /// <summary>Cambia solo il carattere, lasciando intatti stili e colori.</summary>
    private void ApplyFontFaceToAll(string family, float points)
    {
        if (!IsHandleCreated) return;
        var cf = CHARFORMAT2.Create();
        cf.dwMask = NativeMethods.CFM_FACE | NativeMethods.CFM_SIZE;
        cf.yHeight = (int)Math.Round(points * 20);   // twip
        cf.SetFace(family);
        NativeMethods.SendMessage(Handle, NativeMethods.EM_SETCHARFORMAT,
            (IntPtr)NativeMethods.SCF_ALL, ref cf);
    }

    public void SetElements(IList<ScreenElement> els) => SetElements(els, true);

    public void SetElements(IList<ScreenElement> els, bool clearUndo)
    {
        bool savedFlag = _internal;
        _internal = true;
        var zoom = ZoomFactor;
        int keepCaret = clearUndo ? 0 : SelectionStart;
        var keepScroll = GetScrollPos();
        try
        {
            if (els == null || els.Count == 0)
                els = new List<ScreenElement> { new ScreenElement(ElementType.SceneHeading, string.Empty) };

            Rtf = BuildRtf(els);
            Select(0, 0);
            if (clearUndo) ClearUndo();

            _meta = els.Select(x => x.Meta).ToList();
            _metaKeys = BuildKeys(els);
            if (Math.Abs(ZoomFactor - zoom) > 0.001f) ZoomFactor = zoom;   // l'RTF resetta lo zoom
            ApplyPageWidth();

            if (!clearUndo)
            {
                Select(Math.Max(0, Math.Min(keepCaret, TextLength)), 0);
                SetScrollPos(keepScroll);
            }
        }
        finally { _internal = savedFlag; _textCacheValid = false; _indexValid = false; }

        RefreshNoteHighlights();
        RebuildIndex();
        RaiseTypeChanged(true);
    }

    /// <summary>Testo dei paragrafi come finisce davvero nel controllo (dual compreso).</summary>
    private static List<string> BuildKeys(IList<ScreenElement> els)
    {
        var keys = new List<string>(els.Count);
        foreach (var e in els)
        {
            var st = ElementStyle.Get(e.Type);
            var text = StyledText.Plain(e.Text ?? string.Empty);
            if (st.UpperCase) text = text.ToUpperInvariant();
            if (e.Type == ElementType.Character && e.Dual) text += " ^";
            keys.Add(text);
        }
        return keys;
    }

    private string BuildRtf(IList<ScreenElement> els)
    {
        var family = (_fontNormal != null ? _fontNormal.Name : "Courier New").Replace("{", "").Replace("}", "").Replace("\\", "");

        // la tavolozza si riempie mentre si scrive il corpo, e finisce in testa al file
        var palette = new List<Color>();
        int ColorIndex(Color c)
        {
            for (int k = 0; k < palette.Count; k++)
                if (palette[k].ToArgb() == c.ToArgb()) return k + 1;
            palette.Add(c);
            return palette.Count;
        }

        var sb = new StringBuilder();
        var order = ElementStyle.All.ToList();

        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];
            var st = ElementStyle.Get(e.Type);
            var text = e.Text ?? string.Empty;
            bool plainOnly = st.UpperCase;
            if (plainOnly) text = StyledText.Plain(text).ToUpperInvariant();
            if (e.Type == ElementType.Character && e.Dual) text += " ^";

            var typeColor = _elementColors.TryGetValue(e.Type, out var tc) ? tc : ForeColor;
            int colorIndex = ColorIndex(typeColor);

            sb.Append(@"\pard\f0\fs24");
            sb.Append(@"\li").Append(st.LeftTwips);
            sb.Append(@"\ri").Append(st.RightTwips);
            sb.Append(@"\sb").Append(st.SpaceBeforeTwips);
            sb.Append(@"\sa0");
            sb.Append(st.RightAlign ? @"\qr" : @"\ql");
            sb.Append(@"\cf").Append(colorIndex);

            if (plainOnly || !StyledText.HasMarkup(text))
            {
                sb.Append(st.Bold ? @"\b" : @"\b0");
                sb.Append(' ');
                AppendRtfText(sb, plainOnly ? text : StyledText.Plain(text));
            }
            else
            {
                foreach (var run in StyledText.Parse(text))
                {
                    if (run.Text.Length == 0) continue;
                    sb.Append(run.Bold || st.Bold ? @"\b" : @"\b0");
                    sb.Append(run.Italic ? @"\i" : @"\i0");
                    sb.Append(run.Underline ? @"\ul" : @"\ulnone");

                    var runColor = ParseHex(run.Color);
                    sb.Append(@"\cf").Append(runColor.HasValue ? ColorIndex(runColor.Value) : colorIndex);

                    var hl = ParseHex(run.Highlight);
                    if (hl.HasValue) sb.Append(@"\highlight").Append(ColorIndex(hl.Value));
                    else sb.Append(@"\highlight0");

                    sb.Append(' ');
                    AppendRtfText(sb, run.Text);
                }
                sb.Append(@"\b0\i0\ulnone\highlight0");
            }

            if (i < els.Count - 1) sb.Append(@"\par");
        }

        var head = new StringBuilder();
        head.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern\fprq1\fcharset0 ").Append(family).Append(";}}");
        head.Append(@"{\colortbl ;");
        foreach (var c in palette)
            head.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';');
        head.Append('}');
        head.Append(@"\viewkind4\uc1");

        return head.Append(sb).Append('}').ToString();
    }

    private static Color? ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return null;
        try
        {
            return Color.FromArgb(
                Convert.ToInt32(h.Substring(0, 2), 16),
                Convert.ToInt32(h.Substring(2, 2), 16),
                Convert.ToInt32(h.Substring(4, 2), 16));
        }
        catch { return null; }
    }

    private static string ToHex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

    private static void AppendRtfText(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\n' || c == '\r' || c == '\t') sb.Append(' ');
            else if (c < 128) sb.Append(c);
            else
            {
                int code = c > 32767 ? c - 65536 : c;
                sb.Append(@"\u").Append(code).Append('?');
            }
        }
    }

    // ------------------------------------------------------------------ TASTIERA

    private const int WM_PASTE = 0x0302;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PASTE && !_internal)
        {
            if (PasteSmart()) return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Incolla dagli appunti passando dalla riclassificazione (voce di menu e Ctrl+V).</summary>
    public void PasteFromClipboard()
    {
        if (!PasteSmart()) Paste();
    }

    /// <summary>
    /// Incollare da Word o dal browser porterebbe dentro rientri e caratteri altrui, e qui
    /// il rientro E' il tipo di elemento: il testo estraneo viene quindi riclassificato.
    /// Il materiale copiato da DraftLite (o da un altro editor di sceneggiature) passa intatto.
    /// </summary>
    private bool PasteSmart()
    {
        try
        {
            if (Clipboard.ContainsData(DataFormats.Rtf))
            {
                var rtf = Clipboard.GetData(DataFormats.Rtf) as string;
                if (rtf != null && (rtf.Contains("\\li3168") || rtf.Contains("\\li2304") || rtf.Contains("\\li1440")))
                    return false;   // viene da noi: incolla normale
            }

            if (!Clipboard.ContainsText()) return false;
            var text = (Clipboard.GetText() ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (text.Length == 0) return true;

            bool saved = _internal;
            _internal = true;
            SuspendDrawing();
            try
            {
                if (!text.Contains('\n'))
                {
                    var st0 = ElementStyle.Get(CurrentType);
                    SelectedText = st0.UpperCase ? text.ToUpperInvariant() : text;
                    return true;
                }

                int start = SelectionStart;
                SelectedText = text;
                int end = SelectionStart;

                var t = CachedText;
                int from = ParagraphStartAt(t, start);
                int to = ParagraphEndAt(t, Math.Min(end, t.Length));

                var blocks = new List<(int Start, string Text)>();
                int p = from;
                while (p <= to)
                {
                    int e = ParagraphEndAt(t, p);
                    blocks.Add((p, t.Substring(p, e - p)));
                    if (e >= t.Length) break;
                    p = e + 1;
                }

                var types = IO.TextImporter.ClassifyPlainLines(blocks.Select(b => b.Text).ToList());
                for (int i = 0; i < blocks.Count; i++)
                    ApplyTypeToParagraph(blocks[i].Start, blocks[i].Text, types[i], true);

                Select(Math.Min(end, TextLength), 0);
                return true;
            }
            finally
            {
                ResumeDrawing();
                _internal = saved;
                _textCacheValid = false;
                _indexValid = false;
                RaiseTypeChanged(true);
                _indexTimer.Stop();
                _indexTimer.Start();
            }
        }
        catch { return false; }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_chooser.Visible)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    _chooser.SelectedIndex = Math.Min(_chooser.SelectedIndex + 1, _chooser.Items.Count - 1);
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Up:
                    _chooser.SelectedIndex = Math.Max(_chooser.SelectedIndex - 1, 0);
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Escape:
                    HideChooser();
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Enter:
                case Keys.Tab:
                    AcceptChooser();
                    e.Handled = e.SuppressKeyPress = true;
                    return;
            }
        }

        if (_suggest.Visible)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    _suggest.SelectedIndex = Math.Min(_suggest.SelectedIndex + 1, _suggest.Items.Count - 1);
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Up:
                    _suggest.SelectedIndex = Math.Max(_suggest.SelectedIndex - 1, 0);
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Escape:
                    HideSuggestions();
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                case Keys.Enter:
                case Keys.Tab:
                    AcceptSuggestion();
                    e.Handled = e.SuppressKeyPress = true;
                    return;
            }
        }

        // grassetto, corsivo e sottolineato restano all'utente...
        if (e.Control && !e.Alt && !e.Shift &&
            (e.KeyCode == Keys.B || e.KeyCode == Keys.I || e.KeyCode == Keys.U))
        {
            ToggleStyle(e.KeyCode == Keys.B ? FontStyle.Bold
                      : e.KeyCode == Keys.I ? FontStyle.Italic
                      : FontStyle.Underline);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        // ...l'allineamento no: quello dice di che tipo e' l'elemento
        if (e.Control && !e.Alt &&
            (e.KeyCode == Keys.E || e.KeyCode == Keys.L || e.KeyCode == Keys.R || e.KeyCode == Keys.J))
        {
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Alt && !e.Shift)
        {
            ElementType? t = e.KeyCode switch
            {
                Keys.D1 or Keys.NumPad1 => ElementType.SceneHeading,
                Keys.D2 or Keys.NumPad2 => ElementType.Action,
                Keys.D3 or Keys.NumPad3 => ElementType.Character,
                Keys.D4 or Keys.NumPad4 => ElementType.Parenthetical,
                Keys.D5 or Keys.NumPad5 => ElementType.Dialogue,
                Keys.D6 or Keys.NumPad6 => ElementType.Transition,
                _ => null
            };
            if (t.HasValue)
            {
                ApplyType(t.Value);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.D)
            {
                ToggleDual();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
        }

        if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt)
        {
            e.Handled = e.SuppressKeyPress = true;
            HandleEnter(e.Shift);
            return;
        }

        if (e.KeyCode == Keys.Tab)
        {
            e.Handled = e.SuppressKeyPress = true;
            HandleTab(e.Shift);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        _fromTyping = true;
        if (_chooser.Visible && !char.IsControl(e.KeyChar)) HideChooser();
        if (!char.IsControl(e.KeyChar))
        {
            var st = ElementStyle.Get(CurrentType);
            if (st.UpperCase) e.KeyChar = char.ToUpper(e.KeyChar, CultureInfo.CurrentCulture);
        }
        base.OnKeyPress(e);
    }

    private void HandleEnter(bool shift)
    {
        // con del testo selezionato, Invio lo sostituisce
        if (SelectionLength > 0)
        {
            bool s0 = _internal;
            _internal = true;
            try { SelectedText = string.Empty; } finally { _internal = s0; }
        }

        var type = CurrentType;
        var text = CurrentParagraphText;
        bool atEnd = SelectionStart >= ParagraphEndAt(CachedText, SelectionStart);

        // Invio su un elemento vuoto che non e' azione: torna ad azione senza creare righe
        if (string.IsNullOrWhiteSpace(text) && type != ElementType.Action && !shift)
        {
            ApplyType(ElementType.Action);
            return;
        }

        // dalla parentetica si esce sempre da fondo riga
        if (type == ElementType.Parenthetical)
        {
            Select(ParagraphEndAt(CachedText, SelectionStart), 0);
            atEnd = true;
        }

        // spezzando un elemento a meta', le due parti restano dello stesso tipo
        var next = (shift || !atEnd) ? type : ElementStyle.NextAfterEnter(type);

        bool saved = _internal;
        _internal = true;
        try
        {
            SelectedText = "\n";
            ApplyTypeToCurrentParagraph(next, false);
        }
        finally { _internal = saved; }

        HideSuggestions();
        RaiseTypeChanged(true);
        ScrollToCaret();

        if (AskElementOnEnter) ShowElementChooser(next);

        _indexTimer.Stop();
        _indexTimer.Start();
    }

    private void HandleTab(bool shift)
    {
        var type = CurrentType;
        var text = CurrentParagraphText;

        // da personaggio (con nome scritto) il Tab apre la parentetica, come Final Draft
        if (!shift && type == ElementType.Character && !string.IsNullOrWhiteSpace(text))
        {
            var t = CachedText;
            int end = ParagraphEndAt(t, SelectionStart);
            Select(end, 0);
            bool saved = _internal;
            _internal = true;
            try
            {
                SelectedText = "\n";
                ApplyTypeToCurrentParagraph(ElementType.Parenthetical, false);
                SelectedText = "()";
                Select(SelectionStart - 1, 0);
            }
            finally { _internal = saved; }
            RaiseTypeChanged(true);
            return;
        }

        int idx = Array.IndexOf(ElementStyle.TabCycle, type);
        if (idx < 0) idx = 0;
        int n = ElementStyle.TabCycle.Length;
        idx = ((idx + (shift ? -1 : 1)) % n + n) % n;
        ApplyType(ElementStyle.TabCycle[idx]);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _textCacheValid = false;
        _indexValid = false;
        base.OnTextChanged(e);
        if (_internal) return;

        AutoPromoteSceneHeading();
        UpdateSuggestions();
        RaiseTypeChanged(false);
        CenterCaret();
        _fromTyping = false;

        _indexTimer.Stop();
        _indexTimer.Start();
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_internal) return;
        HideChooser();
        RaiseTypeChanged(false);
        CenterCaret();
        CaretMoved?.Invoke(this, EventArgs.Empty);
        if (_suggest.Visible) UpdateSuggestions();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        HideSuggestions();
        HideChooser();
        base.OnMouseDown(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        HideSuggestions();
        HideChooser();
        base.OnLostFocus(e);
    }

    /// <summary>Il cursore si e' spostato: serve a chi disegna a margine.</summary>
    public event EventHandler CaretMoved;

    private void RaiseTypeChanged(bool force)
    {
        var t = CurrentType;
        if (force || t != _lastType)
        {
            _lastType = t;
            CurrentTypeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Se scrivi INT./EST. all'inizio di un'azione, diventa intestazione di scena da sola.</summary>
    private void AutoPromoteSceneHeading()
    {
        if (!_fromTyping) return;          // mai su Annulla, Ripristina o Incolla
        if (CurrentType != ElementType.Action) return;
        var text = CurrentParagraphText;
        if (text.Length < 4 || text.Length > 12) return;
        if (!Screenplay.LooksLikeSceneHeading(text)) return;
        if (!text.EndsWith(" ") && !text.EndsWith(".") && !text.EndsWith("/")) return;
        ApplyType(ElementType.SceneHeading);
    }

    // ------------------------------------------------------------------ SUGGERIMENTI

    private void UpdateSuggestions()
    {
        if (_chooser.Visible) { HideSuggestions(); return; }
        var type = CurrentType;
        var text = CurrentParagraphText;
        var caretAtEnd = SelectionStart >= ParagraphEndAt(CachedText, SelectionStart);

        _suggestValues.Clear();

        if (caretAtEnd && type == ElementType.Character && text.Trim().Length >= 1)
        {
            var p = text.Trim();
            foreach (var c in _characters)
                if (c.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
                    !c.Equals(p, StringComparison.OrdinalIgnoreCase))
                    _suggestValues.Add(c);
        }
        else if (caretAtEnd && type == ElementType.SceneHeading)
        {
            var p = text.TrimStart();
            int dash = p.LastIndexOf('-');

            if (dash >= 0 && dash >= p.Length - 12)
            {
                var head = p.Substring(0, dash).TrimEnd();
                var frag = p.Substring(dash + 1).TrimStart();
                foreach (var tod in TimesOfDay)
                    if (tod.StartsWith(frag, StringComparison.OrdinalIgnoreCase) &&
                        !tod.Equals(frag, StringComparison.OrdinalIgnoreCase))
                        _suggestValues.Add(head + " - " + tod);
            }
            else if (p.Length == 0)
            {
                _suggestValues.AddRange(ScenePrefixes.Select(x => x.TrimEnd()));
            }
            else
            {
                foreach (var s in _sceneHeadings)
                    if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
                        !s.Equals(p, StringComparison.OrdinalIgnoreCase))
                        _suggestValues.Add(s);
                foreach (var pre in ScenePrefixes)
                    if (pre.TrimEnd().StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
                        !pre.TrimEnd().Equals(p, StringComparison.OrdinalIgnoreCase))
                        _suggestValues.Add(pre.TrimEnd());
            }
        }

        if (_suggestValues.Count == 0) { HideSuggestions(); return; }

        var distinct = _suggestValues.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        _suggest.BeginUpdate();
        _suggest.Items.Clear();
        foreach (var v in distinct) _suggest.Items.Add(v);
        _suggest.EndUpdate();
        _suggest.SelectedIndex = 0;
        _suggest.Height = Math.Min(8, distinct.Count) * _suggest.ItemHeight + 4;

        var host = FindForm();
        if (host == null) return;
        var caretPos = GetPositionFromCharIndex(SelectionStart);
        var screen = PointToScreen(new Point(caretPos.X, caretPos.Y + (int)(Font.Height * ZoomFactor) + 2));
        var local = host.PointToClient(screen);
        if (local.Y + _suggest.Height > host.ClientSize.Height)
            local.Y = Math.Max(0, local.Y - _suggest.Height - (int)(Font.Height * ZoomFactor) - 4);
        if (local.X + _suggest.Width > host.ClientSize.Width)
            local.X = Math.Max(0, host.ClientSize.Width - _suggest.Width);
        _suggest.Location = local;
        _suggest.Visible = true;
        _suggest.BringToFront();
    }

    private void AcceptSuggestion()
    {
        if (!_suggest.Visible || _suggest.SelectedItem == null) { HideSuggestions(); return; }
        var value = _suggest.SelectedItem.ToString();
        HideSuggestions();

        var t = CachedText;
        int caret = Math.Min(SelectionStart, t.Length);
        int s = ParagraphStartAt(t, caret);
        int e = ParagraphEndAt(t, caret);

        bool saved = _internal;
        _internal = true;
        try
        {
            Select(s, e - s);
            SelectedText = value;
            Select(s + value.Length, 0);
        }
        finally { _internal = saved; }

        RaiseTypeChanged(true);
    }

    public void HideSuggestions()
    {
        if (_suggest.Visible) _suggest.Visible = false;
    }

    // ------------------------------------------------------------------ SCELTA ELEMENTO

    /// <summary>
    /// Il menu che compare andando a capo: propone l'elemento che verrebbe da solo,
    /// gli altri sono a un tasto di distanza. Si ignora semplicemente continuando a scrivere.
    /// </summary>
    public void ShowElementChooser(ElementType proposed)
    {
        var host = FindForm();
        if (host == null || !IsHandleCreated) return;

        HideSuggestions();

        var types = ElementStyle.All.ToList();
        _chooser.BeginUpdate();
        _chooser.Items.Clear();
        for (int i = 0; i < types.Count; i++)
            _chooser.Items.Add("  " + types[i].Name + "   (Ctrl+" + (i + 1) + ")");
        _chooser.EndUpdate();

        int index = types.FindIndex(x => x.Type == proposed);
        _chooser.SelectedIndex = index < 0 ? 0 : index;
        _chooser.Height = types.Count * _chooser.ItemHeight + 6;

        var caretPos = GetPositionFromCharIndex(SelectionStart);
        var screen = PointToScreen(new Point(caretPos.X, caretPos.Y + (int)(Font.Height * ZoomFactor) + 2));
        var local = host.PointToClient(screen);

        if (local.Y + _chooser.Height > host.ClientSize.Height)
            local.Y = Math.Max(0, local.Y - _chooser.Height - (int)(Font.Height * ZoomFactor) - 4);
        if (local.X + _chooser.Width > host.ClientSize.Width)
            local.X = Math.Max(0, host.ClientSize.Width - _chooser.Width);

        _chooser.Location = local;
        _chooser.Visible = true;
        _chooser.BringToFront();
    }

    public void HideChooser()
    {
        if (_chooser.Visible) _chooser.Visible = false;
    }

    private void AcceptChooser()
    {
        if (!_chooser.Visible) return;
        int i = _chooser.SelectedIndex;
        HideChooser();

        var types = ElementStyle.All.ToList();
        if (i < 0 || i >= types.Count) return;
        ApplyType(types[i].Type);
    }

    /// <summary>Colori del testo per tipo di elemento (li passa il tema).</summary>
    public void SetElementColors(IDictionary<ElementType, Color> colors)
    {
        if (colors == null) return;

        _chooser.BackColor = BackColor;
        _chooser.ForeColor = ForeColor;
        _suggest.BackColor = BackColor;
        _suggest.ForeColor = ForeColor;

        if (!IsHandleCreated || TextLength == 0)
        {
            foreach (var kv in colors) _elementColors[kv.Key] = kv.Value;
            return;
        }

        // gia' questi colori? allora non si tocca niente: rileggere e riscrivere tutto
        // marcherebbe il copione come modificato e butterebbe via l'annulla per nulla
        bool same = true;
        foreach (var kv in colors)
        {
            if (_elementColors.TryGetValue(kv.Key, out var old) && old.ToArgb() == kv.Value.ToArgb()) continue;
            same = false;
            break;
        }
        if (same) return;

        // si rilegge il contenuto con i colori vecchi e lo si riscrive con i nuovi:
        // cosi' quello che l'utente ha colorato a mano non viene travolto dal tema
        int caret = SelectionStart;
        var elements = GetElements();
        foreach (var kv in colors) _elementColors[kv.Key] = kv.Value;
        SetElements(elements, false);
        GoToCharIndex(Math.Min(caret, TextLength), false);
    }

    // ------------------------------------------------------------------ INDICE

    /// <summary>
    /// Scansione leggera della struttura: solo le righe tutte in maiuscolo possono essere
    /// scene, personaggi o transizioni, quindi il tipo reale si chiede a Windows solo per quelle.
    /// </summary>
    private List<(int CharIndex, ElementType Type, string Text)> ScanUpperLines()
    {
        var result = new List<(int, ElementType, string)>();
        if (!IsHandleCreated) return result;

        var lines = CachedText.Split('\n');
        int selStart = SelectionStart, selLen = SelectionLength;
        var pending = selLen == 0 ? SelectionFont : null;
        var scroll = GetScrollPos();
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            int pos = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed.Length <= 120 && trimmed == trimmed.ToUpperInvariant())
                {
                    Select(pos, 0);
                    result.Add((pos, TypeFromParaFormat(GetParaFormat()), trimmed));
                }
                pos += line.Length + 1;
            }
        }
        finally
        {
            Select(selStart, selLen);
            SetScrollPos(scroll);
            RestoreCaretFormat(selLen, pending);
            pending?.Dispose();
            ResumeDrawing();
            _internal = saved;
        }
        return result;
    }

    /// <summary>Elenco delle intestazioni di scena con la posizione nel testo.</summary>
    public List<(int CharIndex, int Number, string Text)> GetSceneIndex()
    {
        if (!_indexValid) RebuildIndex();
        return _sceneIndex;
    }

    private void RebuildIndex()
    {
        SyncMeta();
        var chars = new List<string>();
        var scenes = new List<string>();
        var index = new List<(int, int, string)>();
        var seenC = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenS = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;

        foreach (var r in ScanUpperLines())
        {
            if (r.Type == ElementType.SceneHeading)
            {
                index.Add((r.CharIndex, ++n, r.Text));
                if (seenS.Add(r.Text)) scenes.Add(r.Text);
            }
            else if (r.Type == ElementType.Character)
            {
                var name = Screenplay.NormalizeCharacterName(r.Text);
                if (name.Length > 0 && seenC.Add(name)) chars.Add(name);
            }
        }

        _characters = chars;
        _sceneHeadings = scenes;
        _sceneIndex = index;
        _indexValid = true;
        RefreshNoteHighlights();
        DocumentIndexed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dopo una scansione la selezione torna al suo posto, ma il formato "in attesa"
    /// del cursore no: senza questo, una scena appena iniziata perde il grassetto.
    /// </summary>
    /// <summary>
    /// Dopo una scansione la selezione torna al suo posto, ma il formato "in attesa"
    /// del cursore no. Si rimette quello che c'era, cosi' un Ctrl+I dato e non ancora
    /// usato non se lo mangia il primo timer che passa.
    /// </summary>
    private void RestoreCaretFormat(int selLen, Font pending = null)
    {
        if (selLen != 0 || !IsHandleCreated) return;

        if (pending != null)
        {
            SelectionFont = pending;
            return;
        }

        var type = TypeFromParaFormat(GetParaFormat());
        var st = ElementStyle.Get(type);
        SelectionFont = st.Bold ? _fontBold : _fontNormal;
        if (_elementColors.TryGetValue(type, out var color)) SelectionColor = color;
    }

    public IReadOnlyList<string> KnownCharacters => _characters;

    public void GoToCharIndex(int index) => GoToCharIndex(index, true);

    public void GoToCharIndex(int index, bool takeFocus)
    {
        if (index < 0) index = 0;
        if (index > TextLength) index = TextLength;
        Select(index, 0);
        ScrollToCaret();
        if (takeFocus) Focus();
    }

    // ------------------------------------------------------------------ DATI ANCORATI

    /// <summary>
    /// Riallinea note, sinossi, colori e numeri di scena ai paragrafi dopo una modifica:
    /// le parti uguali in testa e in coda restano dove sono, in mezzo si segue la posizione.
    /// Cosi' una nota resta attaccata alla sua riga anche se sopra ne aggiungi o togli dieci.
    /// </summary>
    private void SyncMeta()
    {
        var lines = CachedText.Split('\n');

        if (_metaKeys.Count == lines.Length)
        {
            bool same = true;
            for (int i = 0; i < lines.Length; i++)
                if (!string.Equals(_metaKeys[i], lines[i], StringComparison.Ordinal)) { same = false; break; }
            if (same) return;
        }

        var oldKeys = _metaKeys;
        var oldMeta = _meta;
        while (oldMeta.Count < oldKeys.Count) oldMeta.Add(null);

        int head = 0;
        while (head < oldKeys.Count && head < lines.Length &&
               string.Equals(oldKeys[head], lines[head], StringComparison.Ordinal)) head++;

        int tail = 0;
        while (tail < oldKeys.Count - head && tail < lines.Length - head &&
               string.Equals(oldKeys[oldKeys.Count - 1 - tail], lines[lines.Length - 1 - tail], StringComparison.Ordinal))
            tail++;

        var newMeta = new List<ElementMeta>(lines.Length);
        for (int i = 0; i < head; i++) newMeta.Add(oldMeta[i]);

        int oldMid = oldKeys.Count - head - tail;
        int newMid = lines.Length - head - tail;

        // quello che sparisce dal centro va in panchina: se la riga ricompare
        // (annulla, taglia e incolla, spostamento) si riprende la sua nota
        for (int k = 0; k < oldMid; k++)
        {
            var m = oldMeta[head + k];
            var key = oldKeys[head + k];
            if (m != null && !m.IsEmpty && !string.IsNullOrWhiteSpace(key))
                _orphanMeta[key] = m;
        }
        if (_orphanMeta.Count > 400) _orphanMeta.Clear();

        for (int k = 0; k < newMid; k++)
        {
            ElementMeta m = k < oldMid ? oldMeta[head + k] : null;
            var key = lines[head + k];
            if (m == null && !string.IsNullOrWhiteSpace(key) && _orphanMeta.TryGetValue(key, out var recovered))
                m = recovered;
            if (m != null && key != null && _orphanMeta.ContainsKey(key)) _orphanMeta.Remove(key);
            newMeta.Add(m);
        }

        for (int k = 0; k < tail; k++)
            newMeta.Add(oldMeta[oldKeys.Count - tail + k]);

        _meta = newMeta;
        _metaKeys = new List<string>(lines);
    }

    public int CurrentParagraphIndex
    {
        get
        {
            var t = CachedText;
            int caret = Math.Min(SelectionStart, t.Length);
            int idx = 0;
            for (int i = 0; i < caret; i++) if (t[i] == '\n') idx++;
            return idx;
        }
    }

    private int ParagraphStartOfIndex(string t, int paragraphIndex)
    {
        int idx = 0, pos = 0;
        while (idx < paragraphIndex && pos < t.Length)
        {
            if (t[pos] == '\n') idx++;
            pos++;
        }
        return pos;
    }

    private ElementMeta MetaAt(int index, bool create)
    {
        SyncMeta();
        while (_meta.Count <= index) _meta.Add(null);
        if (_meta[index] == null && create) _meta[index] = new ElementMeta();
        return _meta[index];
    }

    /// <summary>Nota ancorata al paragrafo su cui sta il cursore.</summary>
    public string NoteAtCaret
    {
        get
        {
            SyncMeta();
            int i = CurrentParagraphIndex;
            return i < _meta.Count ? _meta[i]?.Note : null;
        }
    }

    public void SetNoteAtCaret(string note)
    {
        int i = CurrentParagraphIndex;
        SetNoteAt(i, note);
    }

    public void SetNoteAt(int paragraphIndex, string note)
    {
        var m = MetaAt(paragraphIndex, true);
        m.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (m.IsEmpty) _meta[paragraphIndex] = null;
        HighlightParagraph(paragraphIndex, !string.IsNullOrWhiteSpace(note));
        DocumentIndexed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tutte le note del copione, con la posizione a cui saltare.</summary>
    public List<(int CharIndex, int Paragraph, string Note, string Text)> GetNotes()
    {
        SyncMeta();
        var result = new List<(int, int, string, string)>();
        var t = CachedText;
        var lines = t.Split('\n');
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i < _meta.Count && _meta[i] != null && !string.IsNullOrWhiteSpace(_meta[i].Note))
                result.Add((pos, i, _meta[i].Note, lines[i].Trim()));
            pos += lines[i].Length + 1;
        }
        return result;
    }

    private void HighlightParagraph(int paragraphIndex, bool on)
    {
        if (!IsHandleCreated) return;
        var t = CachedText;
        int start = ParagraphStartOfIndex(t, paragraphIndex);
        int end = ParagraphEndAt(t, start);
        if (end <= start) return;

        int selStart = SelectionStart, selLen = SelectionLength;
        var pending = selLen == 0 ? SelectionFont : null;
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            Select(start, end - start);
            SelectionBackColor = on ? _noteBack : BackColor;
        }
        finally
        {
            Select(selStart, selLen);
            RestoreCaretFormat(selLen, pending);
            pending?.Dispose();
            ResumeDrawing();
            _internal = saved;
        }
    }

    /// <summary>Ricolora tutte le righe che hanno una nota (dopo un caricamento o un cambio tema).</summary>
    public void RefreshNoteHighlights()
    {
        if (!IsHandleCreated) return;
        SyncMeta();

        bool anyNote = _meta.Any(m => m != null && !string.IsNullOrWhiteSpace(m.Note));
        if (!anyNote && !_hadNotes) return;
        _hadNotes = anyNote;

        var t = CachedText;
        var lines = t.Split('\n');
        int selStart = SelectionStart, selLen = SelectionLength;
        var pending = selLen == 0 ? SelectionFont : null;
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            // si tocca una riga per volta: cancellare lo sfondo di tutto il documento
            // spazzerebbe via anche le evidenziazioni messe a mano dall'utente
            int pos = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int len = lines[i].Length;
                if (len > 0)
                {
                    bool wantsNote = i < _meta.Count && _meta[i] != null &&
                                     !string.IsNullOrWhiteSpace(_meta[i].Note);

                    Select(pos, len);
                    if (wantsNote)
                    {
                        SelectionBackColor = _noteBack;
                    }
                    else
                    {
                        // si pulisce solo se era giallo nota: un colore scelto a mano resta
                        var back = SelectionBackColor;
                        if (!back.IsEmpty && back.ToArgb() == _noteBack.ToArgb())
                            SelectionBackColor = BackColor;
                    }
                }
                pos += len + 1;
            }
        }
        finally
        {
            Select(selStart, selLen);
            RestoreCaretFormat(selLen, pending);
            pending?.Dispose();
            ResumeDrawing();
            _internal = saved;
        }
    }

    // ------------------------------------------------------------------ DIALOGO SIMULTANEO

    public bool IsDualAtCaret
    {
        get
        {
            if (CurrentType != ElementType.Character) return false;
            return CurrentParagraphText.TrimEnd().EndsWith("^");
        }
    }

    /// <summary>
    /// Marca la battuta come simultanea a quella precedente (in PDF finiscono affiancate).
    /// Il marcatore e' il "^" finale, come in Fountain.
    /// </summary>
    public void ToggleDual()
    {
        if (CurrentType != ElementType.Character) return;

        var t = CachedText;
        int caret = Math.Min(SelectionStart, t.Length);
        int start = ParagraphStartAt(t, caret);
        int end = ParagraphEndAt(t, caret);
        var text = t.Substring(start, end - start);
        var trimmed = text.TrimEnd();

        string updated = trimmed.EndsWith("^")
            ? trimmed.TrimEnd('^').TrimEnd()
            : trimmed + " ^";

        bool saved = _internal;
        _internal = true;
        try
        {
            Select(start, text.Length);
            SelectedText = updated;
            Select(start + updated.Length, 0);
            ApplyTypeToCurrentParagraph(ElementType.Character, false);
        }
        finally { _internal = saved; }

        RaiseTypeChanged(true);
    }

    /// <summary>Aggiunge (V.O.), (F.C.)... al nome del personaggio sotto il cursore.</summary>
    public void AppendCharacterExtension(string ext)
    {
        if (CurrentType != ElementType.Character || string.IsNullOrWhiteSpace(ext)) return;

        var t = CachedText;
        int caret = Math.Min(SelectionStart, t.Length);
        int start = ParagraphStartAt(t, caret);
        int end = ParagraphEndAt(t, caret);
        var text = t.Substring(start, end - start).TrimEnd();
        if (text.IndexOf(ext, StringComparison.OrdinalIgnoreCase) >= 0) return;

        bool dual = text.EndsWith("^");
        if (dual) text = text.TrimEnd('^').TrimEnd();
        var updated = text + " " + ext.ToUpperInvariant() + (dual ? " ^" : string.Empty);

        bool saved = _internal;
        _internal = true;
        try
        {
            Select(start, end - start);
            SelectedText = updated;
            Select(start + updated.Length, 0);
            ApplyTypeToCurrentParagraph(ElementType.Character, false);
        }
        finally { _internal = saved; }

        RaiseTypeChanged(true);
    }

    // ------------------------------------------------------------------ COMANDI DELLA BARRA

    /// <summary>
    /// Sostituisce la selezione lasciando l'operazione nella cronologia dell'annulla.
    /// Assegnare SelectedText, invece, azzera l'annulla: per i comandi della barra
    /// (elimina, simboli, maiuscolo/minuscolo) non va bene.
    /// </summary>
    private void ReplaceSelection(string text)
    {
        if (!IsHandleCreated) { SelectedText = text ?? string.Empty; return; }
        NativeMethods.SendMessage(Handle, NativeMethods.EM_REPLACESEL,
            (IntPtr)1, text ?? string.Empty);
        _textCacheValid = false;
    }

    /// <summary>Inizio e fine del paragrafo sotto il cursore.</summary>
    public (int Start, int End) ParagraphBounds()
    {
        var t = CachedText;
        int caret = Math.Min(SelectionStart, t.Length);
        return (ParagraphStartAt(t, caret), ParagraphEndAt(t, caret));
    }

    /// <summary>Seleziona tutto il paragrafo sotto il cursore.</summary>
    public void SelectParagraph()
    {
        var (s, e) = ParagraphBounds();
        Select(s, e - s);
        Focus();
    }

    /// <summary>
    /// Seleziona la scena sotto il cursore: dall'intestazione di scena fino a quella dopo.
    /// Se il cursore sta prima della prima scena, prende tutto quello che c'e' prima.
    /// </summary>
    public void SelectScene()
    {
        var index = GetSceneIndex();
        int caret = SelectionStart;

        int start = 0, end = TextLength;
        for (int i = 0; i < index.Count; i++)
        {
            if (index[i].CharIndex <= caret)
            {
                start = index[i].CharIndex;
                end = (i + 1 < index.Count) ? index[i + 1].CharIndex - 1 : TextLength;
            }
            else
            {
                if (i == 0) end = index[0].CharIndex - 1;
                break;
            }
        }

        if (end < start) end = start;
        Select(start, end - start);
        Focus();
    }

    /// <summary>
    /// Toglie dal paragrafo corrente grassetto, corsivo, sottolineato, colori ed
    /// evidenziazioni messi a mano: torna come lo vuole il suo tipo di elemento.
    /// </summary>
    public void RevertParagraph()
    {
        if (!IsHandleCreated) return;

        var (start, end) = ParagraphBounds();
        if (end <= start) return;

        var type = CurrentType;
        var st = ElementStyle.Get(type);
        int caret = SelectionStart;

        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            Select(start, end - start);

            var cf = CHARFORMAT2.Create();
            cf.dwMask = NativeMethods.CFM_BOLD | NativeMethods.CFM_ITALIC | NativeMethods.CFM_UNDERLINE;
            cf.dwEffects = st.Bold ? NativeMethods.CFM_BOLD : 0u;
            NativeMethods.SendMessage(Handle, NativeMethods.EM_SETCHARFORMAT,
                (IntPtr)NativeMethods.SCF_SELECTION, ref cf);

            SelectionColor = _elementColors.TryGetValue(type, out var c) ? c : ForeColor;
            SelectionBackColor = HasNoteAtCaret ? _noteBack : BackColor;
        }
        finally
        {
            Select(Math.Min(caret, TextLength), 0);
            ResumeDrawing();
            _internal = saved;
            _textCacheValid = false;
        }

        OnTextChanged(EventArgs.Empty);
    }

    private bool HasNoteAtCaret => !string.IsNullOrWhiteSpace(NoteAtCaret);

    /// <summary>
    /// Maiuscolo/minuscolo a rotazione sulla selezione (o sul paragrafo, se non c'e'
    /// selezione): TUTTO MAIUSCOLO, tutto minuscolo, Iniziali Maiuscole.
    /// </summary>
    public void CycleCase()
    {
        if (!IsHandleCreated) return;

        var t = CachedText;
        int start = SelectionStart, len = SelectionLength;
        if (len == 0)
        {
            var (s, e) = ParagraphBounds();
            start = s; len = e - s;
            if (len == 0) return;
        }

        start = Math.Max(0, Math.Min(start, t.Length));
        len = Math.Max(0, Math.Min(len, t.Length - start));
        if (len == 0) return;

        var text = t.Substring(start, len);
        if (string.IsNullOrWhiteSpace(text)) return;

        // il giro: TUTTO MAIUSCOLO -> tutto minuscolo -> Iniziali Maiuscole -> ...
        Func<string, string> convert;
        if (text == text.ToUpperInvariant() && text != text.ToLowerInvariant())
            convert = s => s.ToLowerInvariant();
        else if (text == text.ToLowerInvariant())
            convert = TitleCase;
        else
            convert = s => s.ToUpperInvariant();

        // si riscrive un paragrafo per volta: sostituendo un blocco che contiene
        // degli "a capo" si perderebbe il tipo di ogni paragrafo dopo il primo
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            int pos = start;
            int end = start + len;
            while (pos < end)
            {
                int stop = t.IndexOf('\n', pos);
                if (stop < 0 || stop > end) stop = end;

                int pieceLen = stop - pos;
                if (pieceLen > 0)
                {
                    var piece = t.Substring(pos, pieceLen);
                    var changed = convert(piece);
                    // il cambio di caso non cambia la lunghezza: se per qualche carattere
                    // strano la cambiasse, si lascia stare quel pezzo e gli indici restano buoni
                    if (changed.Length == pieceLen && !string.Equals(piece, changed, StringComparison.Ordinal))
                    {
                        Select(pos, pieceLen);
                        ReplaceSelection(changed);
                    }
                }
                pos = stop + 1;
            }

            Select(start, len);
        }
        finally
        {
            ResumeDrawing();
            _internal = saved;
            _textCacheValid = false;
        }

        OnTextChanged(EventArgs.Empty);
    }

    private static string TitleCase(string s)
    {
        var chars = s.ToCharArray();
        bool newWord = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = newWord ? char.ToUpper(chars[i], CultureInfo.CurrentCulture) : chars[i];
                newWord = false;
            }
            else if (chars[i] == ' ' || chars[i] == '\t' || chars[i] == '-' ||
                     chars[i] == '.' || chars[i] == '(' || chars[i] == '"')
                newWord = true;
        }
        return new string(chars);
    }

    /// <summary>Scrive del testo dove sta il cursore (simboli, trattini, virgolette).</summary>
    public void InsertText(string s)
    {
        if (string.IsNullOrEmpty(s) || !IsHandleCreated) return;
        ReplaceSelection(s);
        Focus();
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Cancella la selezione, lasciando l'operazione annullabile.</summary>
    public void DeleteSelection()
    {
        if (!IsHandleCreated || SelectionLength == 0) return;
        ReplaceSelection(string.Empty);
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Apre un elemento nuovo del tipo indicato subito sotto il paragrafo corrente
    /// e ci porta dentro il cursore.
    /// </summary>
    public void InsertElement(ElementType type, string text)
    {
        if (!IsHandleCreated) return;

        var (_, end) = ParagraphBounds();
        bool saved = _internal;
        _internal = true;
        try
        {
            Select(end, 0);
            ReplaceSelection("\n" + (text ?? string.Empty));
            Select(end + 1, 0);
        }
        finally { _internal = saved; _textCacheValid = false; }

        ApplyType(type);
        Select(Math.Min(end + 1 + (text ?? string.Empty).Length, TextLength), 0);
        Focus();
        OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Grassetto, corsivo e sottolineato attivi sotto il cursore: servono ai pulsanti.</summary>
    public (bool Bold, bool Italic, bool Underline) CurrentStyles()
    {
        if (!IsHandleCreated) return (false, false, false);
        try
        {
            var cur = CHARFORMAT2.Create();
            NativeMethods.SendMessage(Handle, NativeMethods.EM_GETCHARFORMAT,
                (IntPtr)NativeMethods.SCF_SELECTION, ref cur);

            bool On(uint mask) => (cur.dwMask & mask) != 0 && (cur.dwEffects & mask) != 0;

            bool baseBold = ElementStyle.Get(CurrentType).Bold;
            return (On(NativeMethods.CFM_BOLD) && !baseBold,
                    On(NativeMethods.CFM_ITALIC),
                    On(NativeMethods.CFM_UNDERLINE));
        }
        catch { return (false, false, false); }
    }

    // ------------------------------------------------------------------ ASPETTO

    /// <summary>Tiene la riga che stai scrivendo a meta' schermo, come una macchina da scrivere.</summary>
    public bool TypewriterMode
    {
        get => _typewriter;
        set { _typewriter = value; if (value) CenterCaret(); }
    }

    private void CenterCaret()
    {
        if (!_typewriter || !IsHandleCreated || ClientSize.Height < 80) return;
        var pt = GetPositionFromCharIndex(SelectionStart);
        int lineHeight = Math.Max(1, (int)Math.Round(Font.Height * ZoomFactor));
        int target = ClientSize.Height / 2;
        int delta = (pt.Y - target) / lineHeight;
        if (delta != 0)
            NativeMethods.SendMessage(Handle, NativeMethods.EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
    }

    /// <summary>Cambia il carattere dell'editor. Il PDF resta in Courier: quello e' lo standard.</summary>
    public void SetEditorFont(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) family = "Courier New";
        Font newNormal, newBold;
        try
        {
            newNormal = new Font(family, 12f, FontStyle.Regular, GraphicsUnit.Point);
            newBold = new Font(family, 12f, FontStyle.Bold, GraphicsUnit.Point);
        }
        catch { return; }

        var oldNormal = _fontNormal;
        var oldBold = _fontBold;
        _fontNormal = newNormal;
        _fontBold = newBold;

        int selStart = SelectionStart, selLen = SelectionLength;
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            Font = _fontNormal;
            ApplyFontFaceToAll(_fontNormal.Name, 12f);   // solo la famiglia: gli stili restano
            Select(selStart, selLen);
        }
        finally
        {
            ResumeDrawing();
            _internal = saved;
        }

        oldNormal?.Dispose();
        oldBold?.Dispose();

        ApplyPageWidth();
        RefreshNoteHighlights();
    }

    /// <summary>Colori di carta, inchiostro ed evidenziazione delle note.</summary>
    public void SetColors(Color back, Color fore, Color noteBack)
    {
        _noteBack = noteBack;
        BackColor = back;
        ForeColor = fore;

        int selStart = SelectionStart, selLen = SelectionLength;
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            SelectAll();
            SelectionColor = fore;
            SelectionBackColor = back;
            Select(selStart, selLen);
        }
        finally
        {
            ResumeDrawing();
            _internal = saved;
        }

        RefreshNoteHighlights();
    }

    // ------------------------------------------------------------------ UTILITA'

    private POINT GetScrollPos()
    {
        var p = new POINT();
        if (IsHandleCreated)
            NativeMethods.SendMessage(Handle, NativeMethods.EM_GETSCROLLPOS, IntPtr.Zero, ref p);
        return p;
    }

    private void SetScrollPos(POINT p)
    {
        if (IsHandleCreated)
            NativeMethods.SendMessage(Handle, NativeMethods.EM_SETSCROLLPOS, IntPtr.Zero, ref p);
    }

    /// <summary>
    /// Sospende reindicizzazione e suggerimenti durante una modifica massiva
    /// (tipicamente "sostituisci tutto"): senza, ogni sostituzione rilegge il documento.
    /// </summary>
    public void BeginBulkEdit()
    {
        _bulkSaved = _internal;
        _internal = true;
        SuspendDrawing();
    }

    public void EndBulkEdit()
    {
        ResumeDrawing();
        _internal = _bulkSaved;
        _textCacheValid = false;
        _indexValid = false;
        RebuildIndex();
        RaiseTypeChanged(true);
    }

    private bool _bulkSaved;
    private int _suspend;

    public void SuspendDrawing()
    {
        if (_suspend++ == 0 && IsHandleCreated)
            NativeMethods.SendMessage(Handle, NativeMethods.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
    }

    public void ResumeDrawing()
    {
        if (_suspend > 0 && --_suspend == 0 && IsHandleCreated)
        {
            NativeMethods.SendMessage(Handle, NativeMethods.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _indexTimer?.Dispose();
            _fontNormal?.Dispose();
            _fontBold?.Dispose();
            _suggest?.Dispose();
            _chooser?.Dispose();
        }
        base.Dispose(disposing);
    }
}
