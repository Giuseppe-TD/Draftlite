using System;
using System.Drawing;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.UI;

public sealed class TitlePageForm : Form
{
    private readonly TextBox _title = new TextBox();
    private readonly TextBox _credit = new TextBox();
    private readonly TextBox _author = new TextBox();
    private readonly TextBox _source = new TextBox();
    private readonly TextBox _date = new TextBox();
    private readonly TextBox _contact = new TextBox { Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _copyright = new TextBox();

    public TitlePage Result { get; private set; }

    public TitlePageForm(TitlePage current)
    {
        Text = "Frontespizio";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(470, 350);
        Font = new Font("Segoe UI", 9f);

        _title.Text = current.Title;
        _credit.Text = current.Credit;
        _author.Text = current.Author;
        _source.Text = current.Source;
        _date.Text = current.DraftDate;
        _contact.Text = current.Contact;
        _copyright.Text = current.Copyright;

        int y = 14;
        void Row(string label, Control c, int h = 23)
        {
            var l = new Label { Text = label, Left = 14, Top = y + 3, Width = 110, AutoSize = false };
            c.Left = 130; c.Top = y; c.Width = 320; c.Height = h;
            Controls.Add(l); Controls.Add(c);
            y += h + 10;
        }

        Row("Titolo", _title);
        Row("Dicitura", _credit);
        Row("Autore", _author);
        Row("Tratto da", _source);
        Row("Data bozza", _date);
        Row("Contatti", _contact, 70);
        Row("Copyright", _copyright);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Top = y + 6, Left = 260 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Top = y + 6, Left = 360 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(470, y + 44);

        ok.Click += (s, e) =>
        {
            Result = new TitlePage
            {
                Title = _title.Text.Trim(),
                Credit = _credit.Text.Trim(),
                Author = _author.Text.Trim(),
                Source = _source.Text.Trim(),
                DraftDate = _date.Text.Trim(),
                Contact = _contact.Text.Trim(),
                Copyright = _copyright.Text.Trim()
            };
        };
    }
}
