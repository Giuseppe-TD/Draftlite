using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>Carattere, tema e modo di scrivere. Il PDF resta Courier: quello e' lo standard.</summary>
public sealed class AppearanceForm : Form
{
    private readonly ComboBox _font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _onlyMono = new CheckBox { Text = "Solo caratteri a larghezza fissa", Checked = true };
    private readonly CheckBox _typewriter = new CheckBox { Text = "Macchina da scrivere (riga corrente a meta' schermo)" };
    private readonly CheckBox _askElement = new CheckBox { Text = "Chiedi l'elemento quando vado a capo" };
    private readonly CheckBox _pageBreaks = new CheckBox { Text = "Mostra le interruzioni di pagina nel margine" };
    private readonly Panel _preview = new Panel { BorderStyle = BorderStyle.FixedSingle };

    public string SelectedFont => _font.SelectedItem as string ?? "Courier New";
    public string SelectedTheme => _theme.SelectedItem as string ?? "Carta";
    public bool Typewriter => _typewriter.Checked;
    public bool AskElement => _askElement.Checked;
    public bool PageBreaks => _pageBreaks.Checked;

    public AppearanceForm(string currentFont, string currentTheme, bool typewriter,
                          bool askElement, bool pageBreaks)
    {
        Text = "Aspetto";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(470, 430);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label { Text = "Carattere", Left = 14, Top = 17, Width = 90 });
        _font.SetBounds(110, 14, 330, 23);
        Controls.Add(_font);

        _onlyMono.SetBounds(110, 42, 330, 22);
        Controls.Add(_onlyMono);

        Controls.Add(new Label { Text = "Tema", Left = 14, Top = 78, Width = 90 });
        _theme.SetBounds(110, 75, 180, 23);
        foreach (var t in Theme.All) _theme.Items.Add(t.Name);
        _theme.SelectedItem = Theme.ByName(currentTheme).Name;
        Controls.Add(_theme);

        _preview.SetBounds(14, 110, 440, 150);
        Controls.Add(_preview);
        _preview.Paint += Preview_Paint;

        _typewriter.SetBounds(16, 274, 430, 22);
        _typewriter.Checked = typewriter;
        _askElement.SetBounds(16, 300, 430, 22);
        _askElement.Checked = askElement;
        _pageBreaks.SetBounds(16, 326, 430, 22);
        _pageBreaks.Checked = pageBreaks;
        Controls.Add(_typewriter);
        Controls.Add(_askElement);
        Controls.Add(_pageBreaks);

        Controls.Add(new Label
        {
            Text = "I colori valgono solo a schermo: il PDF esce sempre in Courier 12 nero,\r\n" +
                   "che e' il formato standard di consegna.",
            Left = 16,
            Top = 352,
            Width = 440,
            Height = 34,
            ForeColor = Color.FromArgb(110, 110, 110)
        });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Left = 262, Top = 392 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Left = 362, Top = 392 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        _onlyMono.CheckedChanged += (s, e) => FillFonts(SelectedFont);
        _font.SelectedIndexChanged += (s, e) => _preview.Invalidate();
        _theme.SelectedIndexChanged += (s, e) => _preview.Invalidate();

        FillFonts(currentFont);
    }

    /// <summary>Anteprima con i colori veri: si vede subito come verra' la pagina.</summary>
    private void Preview_Paint(object sender, PaintEventArgs e)
    {
        var t = Theme.ByName(SelectedTheme);
        var g = e.Graphics;
        g.Clear(t.Paper);

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

            Line("INT. BAR DI PERIFERIA - GIORNO", 16, 12, t.SceneColor, fb);
            Line("Luce grigia dalle vetrine. Il barista pulisce", 16, 34, t.ActionColor, f);
            Line("lo stesso bicchiere da dieci minuti.", 16, 48, t.ActionColor, f);
            Line("MARTA", 150, 72, t.CharacterColor, f);
            Line("(senza alzare lo sguardo)", 110, 86, t.ParentheticalColor, f);
            Line("Un altro.", 90, 100, t.DialogueColor, f);
            Line("DISSOLVENZA A:", 300, 122, t.TransitionColor, f);

            using var rule = new Pen(t.Rule, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(rule, 30, 116, _preview.Width - 12, 116);
            using var small = new Font("Segoe UI", 7f, FontStyle.Bold);
            using var rb = new SolidBrush(t.Rule);
            g.DrawString("2", small, rb, 8, 110);
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
