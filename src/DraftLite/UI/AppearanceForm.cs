using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>Carattere, tema e modo di scrivere. Il PDF resta Courier: quello e' lo standard.</summary>
public sealed class AppearanceForm : Form
{
    private readonly ComboBox _font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _typewriter = new CheckBox { Text = "Macchina da scrivere (riga corrente a meta' schermo)" };
    private readonly CheckBox _onlyMono = new CheckBox { Text = "Solo caratteri a larghezza fissa", Checked = true };
    private readonly Label _preview = new Label
    {
        Text = "INT. BAR DI PERIFERIA - GIORNO\r\n\r\nMarta fissa la tazzina vuota.",
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = ContentAlignment.TopLeft
    };

    public string SelectedFont => _font.SelectedItem as string ?? "Courier New";
    public string SelectedTheme => _theme.SelectedItem as string ?? "Chiaro";
    public bool Typewriter => _typewriter.Checked;

    public AppearanceForm(string currentFont, string currentTheme, bool typewriter)
    {
        Text = "Aspetto";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 330);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label { Text = "Carattere", Left = 14, Top = 17, Width = 90 });
        _font.SetBounds(110, 14, 300, 23);
        Controls.Add(_font);

        _onlyMono.SetBounds(110, 42, 300, 22);
        Controls.Add(_onlyMono);

        Controls.Add(new Label { Text = "Tema", Left = 14, Top = 78, Width = 90 });
        _theme.SetBounds(110, 75, 180, 23);
        foreach (var t in Theme.All) _theme.Items.Add(t.Name);
        _theme.SelectedItem = Theme.ByName(currentTheme).Name;
        Controls.Add(_theme);

        _typewriter.SetBounds(110, 106, 310, 22);
        _typewriter.Checked = typewriter;
        Controls.Add(_typewriter);

        _preview.SetBounds(14, 140, 410, 110);
        Controls.Add(_preview);

        Controls.Add(new Label
        {
            Text = "Il PDF viene sempre esportato in Courier 12: e' il carattere\r\n" +
                   "standard delle sceneggiature e regola il conteggio pagine.",
            Left = 14,
            Top = 256,
            Width = 410,
            Height = 34,
            ForeColor = Color.FromArgb(110, 110, 110)
        });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Left = 232, Top = 294 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Left = 332, Top = 294 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        _onlyMono.CheckedChanged += (s, e) => FillFonts(SelectedFont);
        _font.SelectedIndexChanged += (s, e) => UpdatePreview();
        _theme.SelectedIndexChanged += (s, e) => UpdatePreview();

        FillFonts(currentFont);
        UpdatePreview();
    }

    private void FillFonts(string select)
    {
        var names = _onlyMono.Checked ? MonospacedFonts() : FontFamily.Families.Select(f => f.Name).ToList();
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

    private void UpdatePreview()
    {
        try
        {
            _preview.Font = new Font(SelectedFont, 11f);
            var t = Theme.ByName(SelectedTheme);
            _preview.BackColor = t.Paper;
            _preview.ForeColor = t.Ink;
        }
        catch { /* anteprima non disponibile per questo carattere */ }
    }
}
