using System;
using System.Drawing;
using System.Windows.Forms;
using DraftLite.Editor;

namespace DraftLite.UI;

/// <summary>Trova e sostituisci, senza fronzoli.</summary>
public sealed class FindForm : Form
{
    private readonly ScreenplayEditor _editor;
    private readonly TextBox _find = new TextBox();
    private readonly TextBox _replace = new TextBox();
    private readonly CheckBox _matchCase = new CheckBox { Text = "Maiuscole/minuscole" };

    public FindForm(ScreenplayEditor editor)
    {
        _editor = editor;
        Text = "Trova e sostituisci";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 150);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;

        Controls.Add(new Label { Text = "Trova", Left = 12, Top = 15, Width = 80 });
        _find.SetBounds(100, 12, 220, 23);
        Controls.Add(_find);

        Controls.Add(new Label { Text = "Sostituisci con", Left = 12, Top = 47, Width = 90 });
        _replace.SetBounds(100, 44, 220, 23);
        Controls.Add(_replace);

        _matchCase.SetBounds(100, 74, 180, 22);
        Controls.Add(_matchCase);

        var next = new Button { Text = "Trova", Left = 330, Top = 11, Width = 88 };
        var rep = new Button { Text = "Sostituisci", Left = 330, Top = 43, Width = 88 };
        var all = new Button { Text = "Sostituisci tutto", Left = 240, Top = 104, Width = 110 };
        var close = new Button { Text = "Chiudi", Left = 356, Top = 104, Width = 62 };
        close.Click += (s, e) => Close();

        next.Click += (s, e) => FindNext();
        rep.Click += (s, e) => ReplaceOne();
        all.Click += (s, e) => ReplaceAll();

        Controls.Add(next); Controls.Add(rep); Controls.Add(all); Controls.Add(close);
        AcceptButton = next;
        CancelButton = close;
    }

    private RichTextBoxFinds Options =>
        _matchCase.Checked ? RichTextBoxFinds.MatchCase : RichTextBoxFinds.None;

    private bool FindNext()
    {
        if (_find.Text.Length == 0) return false;
        int start = _editor.SelectionStart + _editor.SelectionLength;
        int idx = _editor.Find(_find.Text, start, Options);
        if (idx < 0) idx = _editor.Find(_find.Text, 0, Options);   // riparti dall'inizio
        if (idx < 0)
        {
            MessageBox.Show(this, "Nessuna corrispondenza.", "Trova",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        _editor.Select(idx, _find.Text.Length);
        _editor.ScrollToCaret();
        return true;
    }

    private void ReplaceOne()
    {
        if (_editor.SelectionLength > 0 &&
            string.Equals(_editor.SelectedText, _find.Text,
                _matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            _editor.SelectedText = _replace.Text;
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        if (_find.Text.Length == 0) return;
        int count = 0;
        _editor.Select(0, 0);
        _editor.BeginBulkEdit();
        try
        {
            int idx;
            int from = 0;
            while ((idx = _editor.Find(_find.Text, from, Options)) >= 0)
            {
                _editor.Select(idx, _find.Text.Length);
                _editor.SelectedText = _replace.Text;
                from = idx + Math.Max(1, _replace.Text.Length);
                count++;
                if (count > 20000) break;
            }
        }
        finally { _editor.EndBulkEdit(); }

        MessageBox.Show(this, count + " sostituzioni.", "Sostituisci tutto",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
