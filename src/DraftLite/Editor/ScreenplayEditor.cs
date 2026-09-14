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
    private readonly Font _fontNormal;
    private readonly Font _fontBold;
    private readonly ListBox _suggest;
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

        int selStart = SelectionStart, selLen = SelectionLength;
        var scroll = GetScrollPos();
        bool saved = _internal;
        _internal = true;
        SuspendDrawing();
        try
        {
            int pos = 0;
            foreach (var line in lines)
            {
                Select(pos, 0);
                result.Add(new ScreenElement(TypeFromParaFormat(GetParaFormat()), line));
                pos += line.Length + 1;
            }
        }
        finally
        {
            Select(selStart, selLen);
            SetScrollPos(scroll);
            RestoreCaretFormat(selLen);
            ResumeDrawing();
            _internal = saved;
        }
        return result;
    }

    public void SetElements(IList<ScreenElement> els) => SetElements(els, true);

    public void SetElements(IList<ScreenElement> els, bool clearUndo)
    {
        bool savedFlag = _internal;
        _internal = true;
        var zoom = ZoomFactor;
        try
        {
            if (els == null || els.Count == 0)
                els = new List<ScreenElement> { new ScreenElement(ElementType.SceneHeading, string.Empty) };

            Rtf = BuildRtf(els);
            Select(0, 0);
            if (clearUndo) ClearUndo();
            if (Math.Abs(ZoomFactor - zoom) > 0.001f) ZoomFactor = zoom;   // l'RTF resetta lo zoom
            ApplyPageWidth();
        }
        finally { _internal = savedFlag; _textCacheValid = false; _indexValid = false; }

        RebuildIndex();
        RaiseTypeChanged(true);
    }

    private static string BuildRtf(IList<ScreenElement> els)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern\fprq1\fcharset0 Courier New;}}");
        sb.Append(@"\viewkind4\uc1");

        for (int i = 0; i < els.Count; i++)
        {
            var e = els[i];
            var st = ElementStyle.Get(e.Type);
            var text = e.Text ?? string.Empty;
            if (st.UpperCase) text = text.ToUpperInvariant();

            sb.Append(@"\pard\f0\fs24");
            sb.Append(@"\li").Append(st.LeftTwips);
            sb.Append(@"\ri").Append(st.RightTwips);
            sb.Append(@"\sb").Append(st.SpaceBeforeTwips);
            sb.Append(@"\sa0");
            sb.Append(st.RightAlign ? @"\qr" : @"\ql");
            sb.Append(st.Bold ? @"\b" : @"\b0");
            sb.Append(' ');
            AppendRtfText(sb, text);
            if (i < els.Count - 1) sb.Append(@"\par");
        }

        sb.Append('}');
        return sb.ToString();
    }

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

    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
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

        // blocca le scorciatoie di formattazione del RichTextBox: la formattazione
        // qui la decide il tipo di elemento, non l'utente
        if (e.Control && !e.Alt &&
            (e.KeyCode == Keys.B || e.KeyCode == Keys.I || e.KeyCode == Keys.U ||
             e.KeyCode == Keys.E || e.KeyCode == Keys.L || e.KeyCode == Keys.R || e.KeyCode == Keys.J))
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
        _fromTyping = false;

        _indexTimer.Stop();
        _indexTimer.Start();
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_internal) return;
        RaiseTypeChanged(false);
        if (_suggest.Visible) UpdateSuggestions();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        HideSuggestions();
        base.OnMouseDown(e);
    }

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
                if (trimmed.Length > 0 && trimmed.Length <= 70 && trimmed == trimmed.ToUpperInvariant())
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
            RestoreCaretFormat(selLen);
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
        DocumentIndexed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dopo una scansione la selezione torna al suo posto, ma il formato "in attesa"
    /// del cursore no: senza questo, una scena appena iniziata perde il grassetto.
    /// </summary>
    private void RestoreCaretFormat(int selLen)
    {
        if (selLen != 0 || !IsHandleCreated) return;
        var st = ElementStyle.Get(TypeFromParaFormat(GetParaFormat()));
        SelectionFont = st.Bold ? _fontBold : _fontNormal;
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
        }
        base.Dispose(disposing);
    }
}
