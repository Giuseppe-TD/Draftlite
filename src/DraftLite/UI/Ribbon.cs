using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>
/// Pulsante della barra multifunzione: disegnato a mano perche' WinForms non ha niente
/// del genere. Due misure: grande (icona sopra, testo sotto su due righe) e piccolo
/// (icona a sinistra, testo a destra). Puo' avere un menu a tendina.
/// Il testo si misura e si disegna con TextRenderer: cosi' le due cose coincidono
/// anche sugli schermi ad alta densita', dove GDI+ misurerebbe stretto.
/// </summary>
public sealed class RibbonButton : Control
{
    private const TextFormatFlags CenterFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    private const TextFormatFlags LeftFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    public string IconKey { get; set; }
    public bool Big { get; set; }
    public bool HasArrow { get; set; }
    public ToolStripDropDown DropDown { get; set; }

    /// <summary>Pulsante a interruttore (macchina da scrivere, pannello laterale, temi...).</summary>
    public bool IsToggle { get; set; }

    /// <summary>
    /// Finestra stretta: il pulsante piccolo tiene solo l'icona e il testo resta
    /// nel suggerimento. Serve a non far sparire i gruppi di destra.
    /// </summary>
    public bool CompactMode { get; set; }

    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); } }
    }

    public Color Ink = Color.Black;
    public Color Accent = Color.SteelBlue;
    public Color Faint = Color.Gray;

    private bool _hot;
    private bool _down;
    private string[] _lines = Array.Empty<string>();

    public RibbonButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = false;
        _ownFont = new Font("Segoe UI", 8.25f);
        Font = _ownFont;
    }

    private readonly Font _ownFont;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownFont?.Dispose();
            DropDown?.Dispose();
        }
        base.Dispose(disposing);
    }

    private float Sc => DeviceDpi / 96f;

    protected override void OnTextChanged(EventArgs e)
    {
        _lines = Array.Empty<string>();
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    private int _closedAt;

    protected override void OnClick(EventArgs e)
    {
        // WinForms manda il clic anche col tasto destro e con quello centrale:
        // un pulsante della barra deve rispondere solo al sinistro
        if (e is MouseEventArgs me && me.Button != MouseButtons.Left) return;

        if (DropDown != null)
        {
            _hot = false;
            _down = false;

            // il clic che chiude la tendina arriva lo stesso fin qui: senza questo
            // controllo la richiuderebbe e la riaprirebbe subito, con uno sfarfallio
            if (DropDown.Visible || Environment.TickCount - _closedAt < 250) { Invalidate(); return; }

            DropDown.Show(this, new Point(0, Height));
            return;
        }
        base.OnClick(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DropDown != null) DropDown.Closed += (s, a) => _closedAt = Environment.TickCount;
    }

    /// <summary>Larghezza naturale: quanto basta per icona e testo.</summary>
    public int MeasureWidth()
    {
        float s = Sc;
        if (Big)
        {
            SplitLines();
            int w = 0;
            foreach (var l in _lines)
                w = Math.Max(w, TextRenderer.MeasureText(l, Font, Size.Empty, TextFormatFlags.NoPadding).Width);
            return Math.Max((int)(48 * s), w + (int)(14 * s));
        }

        int tw = string.IsNullOrEmpty(Text) || CompactMode
            ? 0
            : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(6 * s);

        return (int)(6 * s) + (int)(16 * s) + tw + (HasArrow ? (int)(12 * s) : 0) + (int)(8 * s);
    }

    /// <summary>Il testo del pulsante grande va su due righe: si spezza dove viene meglio.</summary>
    private void SplitLines()
    {
        if (_lines.Length > 0) return;
        var text = Text ?? string.Empty;
        var words = text.Split(' ');

        int whole = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        if (words.Length < 2 || whole <= (int)(64 * Sc))
        {
            _lines = new[] { text };
            return;
        }

        int best = 1;
        int bestDelta = int.MaxValue;
        for (int i = 1; i < words.Length; i++)
        {
            int a = TextRenderer.MeasureText(string.Join(" ", words.Take(i)), Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int b = TextRenderer.MeasureText(string.Join(" ", words.Skip(i)), Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int delta = Math.Abs(a - b);
            if (delta < bestDelta) { bestDelta = delta; best = i; }
        }
        _lines = new[] { string.Join(" ", words.Take(best)), string.Join(" ", words.Skip(best)) };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Sc;

        g.Clear(Parent != null ? Parent.BackColor : BackColor);

        if (Enabled && (_hot || _down || Checked))
        {
            int alpha = _down ? 96 : (Checked && _hot) ? 104 : Checked ? 64 : 44;
            using var fill = new SolidBrush(Color.FromArgb(alpha, Accent));
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(4 * s));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(Checked ? 160 : 90, Accent));
            g.DrawPath(pen, path);
        }

        var ink = Enabled ? Ink : Color.FromArgb(115, Ink);
        var accent = Enabled ? Accent : Color.FromArgb(115, Accent);

        if (Big)
        {
            SplitLines();
            int size = (int)Math.Round(26 * s);
            var icon = RibbonIcons.Get(IconKey, ink, accent, size);
            g.DrawImage(icon, (Width - size) / 2, (int)Math.Round(6 * s), size, size);

            int lineH = Font.Height;
            int y = (int)Math.Round(34 * s);
            foreach (var line in _lines)
            {
                TextRenderer.DrawText(g, line, Font, new Rectangle(1, y, Width - 2, lineH), ink, CenterFlags);
                y += lineH;
            }

            if (HasArrow) Chevron(g, ink, Width / 2f, Height - 6f * s, s);
        }
        else
        {
            int size = (int)Math.Round(16 * s);
            var icon = RibbonIcons.Get(IconKey, ink, accent, size);
            g.DrawImage(icon, (int)Math.Round(5 * s), (Height - size) / 2, size, size);

            if (!string.IsNullOrEmpty(Text) && !CompactMode)
            {
                int left = (int)Math.Round(25 * s);
                int right = HasArrow ? (int)Math.Round(12 * s) : (int)Math.Round(3 * s);
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(left, 0, Math.Max(1, Width - left - right), Height), ink, LeftFlags);
            }

            if (HasArrow) Chevron(g, ink, Width - 7f * s, Height / 2f + 1, s);
        }
    }

    private static void Chevron(Graphics g, Color c, float cx, float cy, float s)
    {
        using var pen = new Pen(c, 1.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(cx - 3.2f * s, cy - 1.6f * s),
            new PointF(cx, cy + 1.6f * s),
            new PointF(cx + 3.2f * s, cy - 1.6f * s)
        });
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Riga di pulsantini affiancati (grassetto, corsivo, sottolineato...): dentro il gruppo
/// conta come un elemento solo, alto quanto un pulsante piccolo.
/// </summary>
public sealed class RibbonRow : Panel
{
    private readonly List<Control> _items = new List<Control>();

    public RibbonRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public void Add(Control c)
    {
        _items.Add(c);
        Controls.Add(c);
    }

    /// <summary>Mette in fila i pulsanti all'altezza voluta e restituisce la larghezza totale.</summary>
    public int Arrange(int height)
    {
        Height = height;
        int x = 0;
        int gap = Math.Max(1, (int)Math.Round(DeviceDpi / 96f));
        foreach (var c in _items)
        {
            int w = c is RibbonButton b ? b.MeasureWidth() : c.Width;
            c.SetBounds(x, 0, w, height);
            x += w + gap;
        }
        Width = Math.Max(1, x - gap);
        return Width;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
    }
}

/// <summary>Separatore verticale dentro un gruppo.</summary>
public sealed class RibbonSeparator : Control
{
    public Color Line = Color.Gray;

    public RibbonSeparator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Width = 7;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
        using var pen = new Pen(Line);
        e.Graphics.DrawLine(pen, Width / 2, 4, Width / 2, Height - 6);
    }
}

/// <summary>
/// Gruppo di comandi: i pulsanti dentro, il nome del gruppo scritto in piccolo sotto
/// e una riga di separazione a destra. I pulsanti piccoli si impilano a tre a tre;
/// quelli grandi prendono tutta l'altezza.
/// </summary>
public sealed class RibbonGroup : Panel
{
    /// <summary>Altezza di riferimento (a 96 dpi) dell'area dei pulsanti.</summary>
    public const int ItemArea = 69;
    /// <summary>Altezza di riferimento della striscia col nome del gruppo.</summary>
    public const int CaptionArea = 15;
    /// <summary>Altezza di riferimento di un pulsante piccolo.</summary>
    public const int SmallHeight = 22;

    public string Caption { get; set; } = string.Empty;
    public Color Line = Color.Gray;
    public Color CaptionColor = Color.Gray;

    /// <summary>Gruppo ristretto: i pulsanti piccoli mostrano la sola icona.</summary>
    public bool Compact { get; set; }

    private readonly List<Control> _items = new List<Control>();
    private int _arrangedFor = -1;

    private readonly Font _ownFont;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ownFont?.Dispose();
        base.Dispose(disposing);
    }

    public RibbonGroup(string caption)
    {
        Caption = caption;
        _ownFont = new Font("Segoe UI", 7.5f);
        Font = _ownFont;
        Dock = DockStyle.Left;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    private float Sc => DeviceDpi / 96f;

    public void Add(Control c)
    {
        _items.Add(c);
        Controls.Add(c);
    }

    /// <summary>Quando il gruppo riceve la sua altezza vera, i pulsanti si ridispongono.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Height > 0 && Height != _arrangedFor) Arrange();
    }

    /// <summary>Dispone i pulsanti in colonne e calcola la larghezza del gruppo.</summary>
    public void Arrange()
    {
        _arrangedFor = Height;

        float s = Sc;
        int captionPx = (int)Math.Round(CaptionArea * s);
        int smallH = (int)Math.Round(SmallHeight * s);
        int pad = (int)Math.Round(4 * s);
        int itemArea = Height > 0
            ? Math.Max((int)Math.Round(44 * s), Height - captionPx - pad * 2)
            : (int)Math.Round(ItemArea * s);

        int x = (int)Math.Round(5 * s);
        int top = pad;
        var column = new List<Control>();

        foreach (var it in _items) SetCompact(it, Compact);

        void FlushColumn()
        {
            if (column.Count == 0) return;

            int w = 0;
            foreach (var c in column)
            {
                int cw = c switch
                {
                    RibbonButton b => b.MeasureWidth(),
                    RibbonRow r => r.Arrange(smallH),
                    _ => c.Width
                };
                w = Math.Max(w, cw);
            }

            int gap = column.Count >= 3 ? (int)Math.Round(1 * s) : (int)Math.Round(3 * s);
            int totalH = column.Count * smallH + gap * (column.Count - 1);
            int y = top + Math.Max(0, (itemArea - totalH) / 2);

            foreach (var c in column)
            {
                // le righe di pulsantini restano della loro larghezza: non si stirano
                int cw = c is RibbonRow row ? row.Width : w;
                c.SetBounds(x, y, cw, smallH);
                y += smallH + gap;
            }

            x += w + (int)Math.Round(2 * s);
            column.Clear();
        }

        foreach (var item in _items)
        {
            if (item is RibbonButton b && b.Big)
            {
                FlushColumn();
                int bw = b.MeasureWidth();
                b.SetBounds(x, top, bw, itemArea);
                x += bw + (int)Math.Round(2 * s);
                continue;
            }

            if (item is RibbonSeparator sep)
            {
                FlushColumn();
                int sw = (int)Math.Round(7 * s);
                sep.SetBounds(x, top + 2, sw, itemArea - 4);
                x += sw;
                continue;
            }

            column.Add(item);
            if (column.Count == 3) FlushColumn();
        }
        FlushColumn();

        int captionWidth = TextRenderer.MeasureText(Caption, Font).Width + (int)Math.Round(16 * s);
        Width = Math.Max(x + (int)Math.Round(4 * s), captionWidth);
    }

    private static void SetCompact(Control c, bool compact)
    {
        switch (c)
        {
            case RibbonButton b when !b.Big:
                b.CompactMode = compact;
                break;
            case RibbonRow row:
                foreach (Control child in row.Controls) SetCompact(child, compact);
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        float s = Sc;
        int captionPx = (int)Math.Round(CaptionArea * s);

        TextRenderer.DrawText(g, Caption, Font,
            new Rectangle(0, Height - captionPx - 1, Width, captionPx), CaptionColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        using var pen = new Pen(Line);
        g.DrawLine(pen, Width - 1, (int)(5 * s), Width - 1, Height - (int)(8 * s));
    }
}

/// <summary>Una linguetta della barra: contiene i suoi gruppi, allineati da sinistra.</summary>
public sealed class RibbonTab : Panel
{
    public string Title { get; }
    internal Rectangle HeaderRect { get; set; }

    private readonly List<RibbonGroup> _groups = new List<RibbonGroup>();
    public IReadOnlyList<RibbonGroup> Groups => _groups;

    public RibbonTab(string title)
    {
        Title = title;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public RibbonGroup AddGroup(string caption)
    {
        var grp = new RibbonGroup(caption);
        _groups.Add(grp);
        return grp;
    }

    /// <summary>
    /// I gruppi vanno aggiunti al contrario: con Dock.Left l'ultimo aggiunto finisce
    /// piu' a sinistra, quindi si parte dal fondo per vederli nell'ordine giusto.
    /// </summary>
    public void Commit()
    {
        Controls.Clear();
        for (int i = _groups.Count - 1; i >= 0; i--) Controls.Add(_groups[i]);
    }

    /// <summary>
    /// Dispone i gruppi e, se non ci stanno tutti, stringe quelli di destra
    /// lasciandogli la sola icona: meglio un pulsante piccolo che un pulsante tagliato.
    /// </summary>
    public void ArrangeAll()
    {
        foreach (var g in _groups)
        {
            g.Compact = false;
            g.Arrange();
        }

        int avail = ClientSize.Width;
        if (avail <= 0) return;

        int total = _groups.Sum(g => g.Width);
        for (int i = _groups.Count - 1; i >= 0 && total > avail; i--)
        {
            int before = _groups[i].Width;
            _groups[i].Compact = true;
            _groups[i].Arrange();
            total -= before - _groups[i].Width;
        }

        _arrangedFor = avail;
    }

    private int _arrangedFor = -1;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Visible && ClientSize.Width > 0 && ClientSize.Width != _arrangedFor) ArrangeAll();
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(BackColor);
}

/// <summary>
/// La barra multifunzione: una riga di linguette in alto e, sotto, i gruppi di comandi
/// della linguetta scelta. Tutto disegnato a mano, cosi' segue il tema (anche quello scuro)
/// e non dipende da librerie esterne.
/// </summary>
public sealed class Ribbon : Panel
{
    /// <summary>Altezze di riferimento a 96 dpi: a video vengono scalate con lo schermo.</summary>
    public const int HeaderHeight = 26;
    public const int BodyHeight = RibbonGroup.ItemArea + RibbonGroup.CaptionArea + 8;

    private readonly List<RibbonTab> _tabs = new List<RibbonTab>();
    private readonly Panel _body = new Panel { Dock = DockStyle.Fill };
    private readonly Dictionary<string, List<RibbonButton>> _byKey =
        new Dictionary<string, List<RibbonButton>>(StringComparer.OrdinalIgnoreCase);
    private readonly Font _tabFont = new Font("Segoe UI", 9f);

    private int _selected;
    private int _hotTab = -1;
    private Theme _theme = Theme.Paper1;

    /// <summary>La linguetta aperta.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Il primo pulsante registrato con quella chiave.</summary>
    public RibbonButton this[string key] =>
        _byKey.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;

    public Ribbon()
    {
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Controls.Add(_body);
        ApplyMetrics();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tabFont?.Dispose();
        base.Dispose(disposing);
    }

    private float Sc => DeviceDpi / 96f;
    private int HeaderPx => (int)Math.Round(HeaderHeight * Sc);

    private void ApplyMetrics()
    {
        // un pixel di bordo in fondo: senza, la riga di chiusura finisce sotto
        // al pannello del corpo e non si vede
        int header = HeaderPx;
        Padding = new Padding(0, header, 0, 1);
        Height = header + (int)Math.Round(BodyHeight * Sc) + 1;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
        MeasureHeaders();
        foreach (var t in _tabs) t.ArrangeAll();
    }

    public RibbonTab AddTab(string title)
    {
        var tab = new RibbonTab(title) { Visible = false };
        _tabs.Add(tab);
        _body.Controls.Add(tab);
        return tab;
    }

    /// <summary>Chiude la costruzione: aggiunge i gruppi e mostra la prima linguetta.</summary>
    public void Commit()
    {
        foreach (var t in _tabs) t.Commit();
        Select(0);
    }

    /// <summary>
    /// Lo stesso comando compare in piu' linguette: si registrano tutti i pulsanti
    /// con quella chiave, cosi' una spunta si accende dappertutto.
    /// </summary>
    public void Register(string key, RibbonButton b)
    {
        if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = new List<RibbonButton>();
        list.Add(b);
    }

    public void SetChecked(string key, bool value)
    {
        if (!_byKey.TryGetValue(key, out var list)) return;
        foreach (var b in list) b.Checked = value;
    }

    public void SetEnabled(string key, bool value)
    {
        if (!_byKey.TryGetValue(key, out var list)) return;
        foreach (var b in list) b.Enabled = value;
    }

    /// <summary>
    /// Rimisura linguette e gruppi: si chiama quando la finestra e' finalmente
    /// a video, perche' prima le larghezze vere non si conoscono.
    /// </summary>
    public void Relayout()
    {
        MeasureHeaders();
        foreach (var t in _tabs) if (t.Visible) t.ArrangeAll();
        Invalidate();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _selected = index;
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Visible = i == index;
        _tabs[index].ArrangeAll();
        Invalidate();
    }

    // ------------------------------------------------------------------ tema

    public void ApplyTheme(Theme t)
    {
        _theme = t;
        BackColor = t.RibbonBack;
        _body.BackColor = t.RibbonBack;

        foreach (var tab in _tabs)
        {
            tab.BackColor = t.RibbonBack;
            foreach (var grp in tab.Groups)
            {
                grp.BackColor = t.RibbonBack;
                grp.Line = t.RibbonLine;
                grp.CaptionColor = t.Faint;
                foreach (Control c in grp.Controls) ApplyToItem(c, t);
                grp.Invalidate();
            }
        }
        Invalidate();
    }

    private static void ApplyToItem(Control c, Theme t)
    {
        switch (c)
        {
            case RibbonRow row:
                row.BackColor = t.RibbonBack;
                foreach (Control child in row.Controls) ApplyToItem(child, t);
                row.Invalidate();
                break;
            case RibbonButton b:
                b.BackColor = t.RibbonBack;
                b.Ink = t.PanelText;
                b.Accent = t.Accent;
                b.Faint = t.Faint;
                b.Invalidate();
                break;
            case RibbonSeparator s:
                s.BackColor = t.RibbonBack;
                s.Line = t.RibbonLine;
                s.Invalidate();
                break;
            case ComboBox cb:
                cb.BackColor = t.Panel;
                cb.ForeColor = t.PanelText;
                cb.FlatStyle = FlatStyle.Flat;
                break;
            case Label lab:
                lab.BackColor = t.RibbonBack;
                lab.ForeColor = t.Faint;
                break;
        }
    }

    // ------------------------------------------------------------ linguette

    private void MeasureHeaders()
    {
        float s = Sc;
        int x = (int)Math.Round(8 * s);
        int header = HeaderPx;
        foreach (var t in _tabs)
        {
            int w = TextRenderer.MeasureText(t.Title, _tabFont).Width + (int)Math.Round(20 * s);
            t.HeaderRect = new Rectangle(x, 0, w, header);
            x += w;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
        MeasureHeaders();
        foreach (var t in _tabs) t.ArrangeAll();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hot = -1;
        if (e.Y < HeaderPx)
            for (int i = 0; i < _tabs.Count; i++)
                if (_tabs[i].HeaderRect.Contains(e.Location)) { hot = i; break; }

        if (hot != _hotTab) { _hotTab = hot; Invalidate(new Rectangle(0, 0, Width, HeaderPx)); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hotTab != -1) { _hotTab = -1; Invalidate(new Rectangle(0, 0, Width, HeaderPx)); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Y < HeaderPx)
            for (int i = 0; i < _tabs.Count; i++)
                if (_tabs[i].HeaderRect.Contains(e.Location)) { Select(i); break; }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int header = HeaderPx;

        using (var head = new SolidBrush(_theme.RibbonHead))
            g.FillRectangle(head, 0, 0, Width, header);
        using (var body = new SolidBrush(_theme.RibbonBack))
            g.FillRectangle(body, 0, header, Width, Height - header);

        using var line = new Pen(_theme.RibbonLine);
        g.DrawLine(line, 0, header - 1, Width, header - 1);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);

        if (_tabs.Count > 0 && _tabs[0].HeaderRect.Width == 0) MeasureHeaders();

        for (int i = 0; i < _tabs.Count; i++)
        {
            var r = _tabs[i].HeaderRect;
            bool sel = i == _selected;

            if (sel)
            {
                using var fill = new SolidBrush(_theme.RibbonBack);
                g.FillRectangle(fill, r.X, 1, r.Width, header - 1);
                g.DrawLine(line, r.X, 1, r.X, header - 2);
                g.DrawLine(line, r.Right - 1, 1, r.Right - 1, header - 2);
                using var accent = new Pen(_theme.Accent, 2f);
                g.DrawLine(accent, r.X + 1, 1, r.Right - 2, 1);
            }
            else if (i == _hotTab)
            {
                using var hot = new SolidBrush(Color.FromArgb(38, _theme.Accent));
                g.FillRectangle(hot, r.X, 2, r.Width, header - 3);
            }

            TextRenderer.DrawText(g, _tabs[i].Title, _tabFont, r,
                sel ? _theme.PanelText : _theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
