using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>
/// Carattere, ingrandimento, tema e colore di ogni tipo di elemento.
/// Il PDF resta Courier 12 nero: qui si decide solo come si sta davanti allo schermo.
/// </summary>
public sealed class AppearanceForm : Form
{
    private readonly ComboBox _font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _onlyMono = new CheckBox { Text = "Solo caratteri a larghezza fissa", Checked = true };
    private readonly CheckBox _typewriter = new CheckBox { Text = "Macchina da scrivere (riga corrente a meta' schermo)" };
    private readonly CheckBox _askElement = new CheckBox { Text = "Chiedi l'elemento quando vado a capo" };
    private readonly CheckBox _pageBreaks = new CheckBox { Text = "Mostra le interruzioni di pagina nel margine" };
    private readonly Panel _preview = new Panel { BorderStyle = BorderStyle.FixedSingle };

    private readonly Dictionary<ElementType, Color> _colors = new Dictionary<ElementType, Color>();
    private readonly Dictionary<ElementType, Button> _colorButtons = new Dictionary<ElementType, Button>();
    private readonly HashSet<ElementType> _custom = new HashSet<ElementType>();

    public string SelectedFont => _font.SelectedItem as string ?? "Courier New";
    public string SelectedTheme => _theme.SelectedItem as string ?? "Carta";
    public bool Typewriter => _typewriter.Checked;
    public bool AskElement => _askElement.Checked;
    public bool PageBreaks => _pageBreaks.Checked;

    public float SelectedZoom
    {
        get
        {
            var t = (_zoom.SelectedItem as string ?? "100%").TrimEnd('%');
            return float.TryParse(t, out var v) ? Math.Max(0.6f, Math.Min(2.5f, v / 100f)) : 1f;
        }
    }

    /// <summary>Solo i colori cambiati a mano: gli altri restano quelli del tema.</summary>
    public Dictionary<string, string> CustomColors =>
        _custom.ToDictionary(t => t.ToString(), t => "#" + _colors[t].R.ToString("X2") +
                                                      _colors[t].G.ToString("X2") +
                                                      _colors[t].B.ToString("X2"));

    public AppearanceForm(string currentFont, string currentTheme, bool typewriter,
                          bool askElement, bool pageBreaks, float zoom,
                          IDictionary<string, string> customColors)
    {
        Text = "Aspetto";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(540, 560);
        Font = new Font("Segoe UI", 9f);

        // --- carattere
        Controls.Add(new Label { Text = "Carattere", Left = 14, Top = 17, Width = 90 });
        _font.SetBounds(110, 14, 290, 23);
        Controls.Add(_font);

        Controls.Add(new Label { Text = "Ingrandimento", Left = 410, Top = 17, Width = 85 });
        _zoom.SetBounds(410, 38, 110, 23);
        foreach (var z in new[] { "75%", "90%", "100%", "110%", "125%", "150%", "175%", "200%" }) _zoom.Items.Add(z);
        _zoom.SelectedItem = Math.Round(zoom * 100) + "%";
        if (_zoom.SelectedIndex < 0) _zoom.SelectedItem = "100%";
        Controls.Add(_zoom);

        _onlyMono.SetBounds(110, 42, 290, 22);
        Controls.Add(_onlyMono);

        // --- tema
        Controls.Add(new Label { Text = "Tema", Left = 14, Top = 78, Width = 90 });
        _theme.SetBounds(110, 75, 180, 23);
        foreach (var t in Theme.All) _theme.Items.Add(t.Name);
        _theme.SelectedItem = Theme.ByName(currentTheme).Name;
        Controls.Add(_theme);

        // --- colori degli elementi
        Controls.Add(new Label
        {
            Text = "Colore degli elementi",
            Left = 14, Top = 112, Width = 200,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        });

        int col = 0, rowY = 136;
        foreach (var st in ElementStyle.All)
        {
            var type = st.Type;
            int x = 14 + col * 262;

            var lab = new Label { Text = st.Name, Left = x, Top = rowY + 4, Width = 110 };
            var btn = new Button
            {
                Left = x + 116,
                Top = rowY,
                Width = 120,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                Text = string.Empty
            };
            btn.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = _colors[type] };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _colors[type] = dlg.Color;
                _custom.Add(type);
                RefreshSwatches();
                _preview.Invalidate();
            };

            Controls.Add(lab);
            Controls.Add(btn);
            _colorButtons[type] = btn;

            col++;
            if (col == 2) { col = 0; rowY += 30; }
        }
        if (col == 1) rowY += 30;

        var reset = new Button { Text = "Rimetti i colori del tema", Left = 14, Top = rowY + 4, Width = 190, Height = 26 };
        reset.Click += (s, e) =>
        {
            _custom.Clear();
            LoadThemeColors();
            RefreshSwatches();
            _preview.Invalidate();
        };
        Controls.Add(reset);

        // --- anteprima
        _preview.SetBounds(14, rowY + 40, 512, 150);
        Controls.Add(_preview);
        _preview.Paint += Preview_Paint;

        int optY = rowY + 200;
        _typewriter.SetBounds(16, optY, 500, 22);
        _typewriter.Checked = typewriter;
        _askElement.SetBounds(16, optY + 26, 500, 22);
        _askElement.Checked = askElement;
        _pageBreaks.SetBounds(16, optY + 52, 500, 22);
        _pageBreaks.Checked = pageBreaks;
        Controls.Add(_typewriter);
        Controls.Add(_askElement);
        Controls.Add(_pageBreaks);

        Controls.Add(new Label
        {
            Text = "I colori valgono a schermo. Nel PDF il copione esce nero su bianco, come si consegna:\r\n" +
                   "se ti servono anche li', c'e' la spunta nella finestra di esportazione.",
            Left = 16, Top = optY + 80, Width = 510, Height = 34,
            ForeColor = Color.FromArgb(110, 110, 110)
        });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Left = 332, Top = optY + 122 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Left = 432, Top = optY + 122 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(540, optY + 162);

        _onlyMono.CheckedChanged += (s, e) => FillFonts(SelectedFont);
        _font.SelectedIndexChanged += (s, e) => _preview.Invalidate();
        _theme.SelectedIndexChanged += (s, e) =>
        {
            LoadThemeColors();      // il tema ridipinge tutto tranne quello che hai scelto tu
            RefreshSwatches();
            _preview.Invalidate();
        };

        FillFonts(currentFont);
        LoadThemeColors();
        if (customColors != null)
        {
            foreach (var kv in customColors)
            {
                if (!Enum.TryParse<ElementType>(kv.Key, out var type)) continue;
                var c = CardsForm.ParseColor(kv.Value);
                if (!c.HasValue) continue;
                _colors[type] = c.Value;
                _custom.Add(type);
            }
        }
        RefreshSwatches();
    }

    private void LoadThemeColors()
    {
        var themeColors = Theme.ByName(SelectedTheme).ElementColors;
        foreach (var kv in themeColors)
            if (!_custom.Contains(kv.Key)) _colors[kv.Key] = kv.Value;
        foreach (var kv in themeColors)
            if (!_colors.ContainsKey(kv.Key)) _colors[kv.Key] = kv.Value;
    }

    private void RefreshSwatches()
    {
        foreach (var kv in _colorButtons)
        {
            var c = _colors.TryGetValue(kv.Key, out var col) ? col : Color.Black;
            kv.Value.BackColor = c;
            kv.Value.ForeColor = Brightness(c) > 140 ? Color.Black : Color.White;
            kv.Value.Text = _custom.Contains(kv.Key) ? "personalizzato" : string.Empty;
            kv.Value.FlatAppearance.BorderColor = Color.FromArgb(120, 120, 120);
        }
    }

    private static int Brightness(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;

    /// <summary>Anteprima con i colori veri: si vede subito come verra' la pagina.</summary>
    private void Preview_Paint(object sender, PaintEventArgs e)
    {
        var t = Theme.ByName(SelectedTheme);
        var g = e.Graphics;
        g.Clear(t.Paper);

        Color Col(ElementType type) => _colors.TryGetValue(type, out var c) ? c : t.Ink;

        Font f, fb;
        try
        {
            f = new Font(SelectedFont, 9.5f);
            fb = new Font(SelectedFont, 9.5f, FontStyle.Bold);
        }
        catch { f = new Font("Courier New", 9.5f); fb = new Font("Courier New", 9.5f, FontStyle.Bold); }

        using (f)
        using (fb)
        {
            void Line(string text, int x, int y, Color color, Font font)
            {
                using var b = new SolidBrush(color);
                g.DrawString(text, font, b, x, y);
            }

            Line("INT. BAR DI PERIFERIA - GIORNO", 40, 12, Col(ElementType.SceneHeading), fb);
            Line("Luce grigia dalle vetrine. Il barista pulisce", 40, 34, Col(ElementType.Action), f);
            Line("lo stesso bicchiere da dieci minuti.", 40, 48, Col(ElementType.Action), f);
            Line("MARTA", 190, 72, Col(ElementType.Character), f);
            Line("(senza alzare lo sguardo)", 140, 86, Col(ElementType.Parenthetical), f);
            Line("Un altro.", 110, 100, Col(ElementType.Dialogue), f);
            Line("DISSOLVENZA A:", 340, 122, Col(ElementType.Transition), f);

            using var rule = new Pen(t.Rule, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(rule, 6, 116, 34, 116);
            using var small = new Font("Segoe UI", 7f, FontStyle.Bold);
            using var rb = new SolidBrush(t.Rule);
            g.DrawString("2", small, rb, 8, 106);
        }
    }

    private void FillFonts(string select)
    {
        var names = _onlyMono.Checked ? MonospacedFonts() : FontFamily.Families.Select(x => x.Name).ToList();
        names = names.Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        _font.BeginUpdate();
        _font.Items.Clear();
        foreach (var n in names) _font.Items.Add(n);
        _font.EndUpdate();

        if (!string.IsNullOrWhiteSpace(select) && _font.Items.Contains(select)) _font.SelectedItem = select;
        else if (_font.Items.Contains("Courier New")) _font.SelectedItem = "Courier New";
        else if (_font.Items.Count > 0) _font.SelectedIndex = 0;
    }

    /// <summary>Un carattere va bene se "i" e "W" occupano lo stesso spazio.</summary>
    private static List<string> MonospacedFonts()
    {
        var result = new List<string>();
        foreach (var family in FontFamily.Families)
        {
            if (!family.IsStyleAvailable(FontStyle.Regular)) continue;
            try
            {
                using var f = new Font(family, 12f);
                var narrow = TextRenderer.MeasureText("iiiiiiiiii", f);
                var wide = TextRenderer.MeasureText("WWWWWWWWWW", f);
                if (Math.Abs(narrow.Width - wide.Width) <= 2) result.Add(family.Name);
            }
            catch { /* famiglia non utilizzabile */ }
        }
        if (!result.Contains("Courier New")) result.Add("Courier New");
        return result;
    }
}
