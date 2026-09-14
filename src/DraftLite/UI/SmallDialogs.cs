using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DraftLite.IO;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>Nota ancorata all'elemento sotto il cursore.</summary>
public sealed class NoteForm : Form
{
    private readonly TextBox _text = new TextBox
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true
    };

    public string Note { get; private set; }
    public bool Deleted { get; private set; }

    public NoteForm(string current, string context)
    {
        Text = "Nota";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(430, 230);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(context) ? "(riga vuota)" : context,
            Left = 14, Top = 12, Width = 400, Height = 18,
            ForeColor = Color.FromArgb(110, 110, 110),
            AutoEllipsis = true
        });

        _text.SetBounds(14, 36, 400, 140);
        _text.Text = current ?? string.Empty;
        Controls.Add(_text);

        var del = new Button { Text = "Elimina", Width = 90, Left = 14, Top = 186 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Left = 224, Top = 186 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Left = 324, Top = 186 };

        del.Click += (s, e) => { Deleted = true; Note = null; DialogResult = DialogResult.OK; Close(); };
        ok.Click += (s, e) => Note = _text.Text.Trim();

        Controls.Add(del);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Scelta del personaggio per i sides.</summary>
public sealed class SidesForm : Form
{
    private readonly ComboBox _character = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

    public string Character => _character.SelectedItem as string;

    public SidesForm(IEnumerable<string> characters)
    {
        Text = "Sides per personaggio";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(380, 140);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label
        {
            Text = "Esporta un PDF con le sole scene in cui compare:",
            Left = 14, Top = 16, Width = 350
        });

        _character.SetBounds(14, 44, 350, 23);
        foreach (var c in characters) _character.Items.Add(c);
        if (_character.Items.Count > 0) _character.SelectedIndex = 0;
        Controls.Add(_character);

        var ok = new Button { Text = "Esporta...", DialogResult = DialogResult.OK, Width = 100, Left = 164, Top = 90 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Left = 268, Top = 90 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Stato della bozza: colore, data, asterischi.</summary>
public sealed class RevisionForm : Form
{
    private readonly ComboBox _color = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _date = new TextBox();
    private readonly CheckBox _active = new CheckBox { Text = "Segna con l'asterisco le righe cambiate da qui in poi" };
    private readonly Label _info = new Label { ForeColor = Color.FromArgb(110, 110, 110) };

    public bool FreezeRequested { get; private set; }
    public string ColorName => _color.SelectedItem as string ?? "Bianca";
    public string RevisionDate => _date.Text.Trim();
    public bool Active => _active.Checked;

    public RevisionForm(Revision current, int changedLines)
    {
        Text = "Revisione";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 250);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label { Text = "Colore bozza", Left = 14, Top = 17, Width = 100 });
        _color.SetBounds(120, 14, 180, 23);
        foreach (var c in Revision.Colors) _color.Items.Add(c);
        _color.SelectedItem = Revision.Colors.Contains(current.Color) ? current.Color : Revision.Colors[0];
        Controls.Add(_color);

        Controls.Add(new Label { Text = "Data", Left = 14, Top = 49, Width = 100 });
        _date.SetBounds(120, 46, 180, 23);
        _date.Text = string.IsNullOrWhiteSpace(current.Date) ? DateTime.Now.ToString("dd/MM/yyyy") : current.Date;
        Controls.Add(_date);

        _active.SetBounds(16, 80, 410, 22);
        _active.Checked = current.Active;
        Controls.Add(_active);

        _info.SetBounds(16, 108, 410, 40);
        _info.Text = current.Active
            ? "Righe marcate rispetto all'ultimo blocco: " + changedLines + "."
            : "Nessuna bozza congelata: al primo blocco tutto il copione parte pulito.";
        Controls.Add(_info);

        var freeze = new Button { Text = "Congela bozza e passa al colore successivo", Width = 300, Left = 16, Top = 156 };
        freeze.Click += (s, e) =>
        {
            FreezeRequested = true;
            _color.SelectedItem = Revision.NextColor(_color.SelectedItem as string);
            _date.Text = DateTime.Now.ToString("dd/MM/yyyy");
            _active.Checked = true;
            _info.Text = "Alla conferma il copione attuale diventa il riferimento: da li' si contano le modifiche.";
        };
        Controls.Add(freeze);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Left = 232, Top = 200 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 90, Left = 332, Top = 200 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Opzioni dell'export PDF: quello che finisce sulla pagina.</summary>
public sealed class PdfOptionsForm : Form
{
    private readonly ComboBox _paper = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _titlePage = new CheckBox { Text = "Frontespizio" };
    private readonly CheckBox _pageNumbers = new CheckBox { Text = "Numeri di pagina" };
    private readonly CheckBox _sceneNumbers = new CheckBox { Text = "Numeri di scena ai margini" };
    private readonly CheckBox _revisionMarks = new CheckBox { Text = "Asterischi di revisione e colore bozza" };
    private readonly TextBox _watermark = new TextBox();
    private readonly TextBox _copyFor = new TextBox();

    public PageSetup Setup { get; private set; }

    public PdfOptionsForm(PageSetup current)
    {
        Text = "Esporta PDF";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 300);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label { Text = "Formato carta", Left = 14, Top = 17, Width = 100 });
        _paper.SetBounds(120, 14, 140, 23);
        _paper.Items.AddRange(new object[] { "A4", "Letter" });
        _paper.SelectedItem = current.PaperName == "Letter" ? "Letter" : "A4";
        Controls.Add(_paper);

        _titlePage.SetBounds(16, 48, 400, 22); _titlePage.Checked = current.IncludeTitlePage;
        _pageNumbers.SetBounds(16, 72, 400, 22); _pageNumbers.Checked = current.PageNumbers;
        _sceneNumbers.SetBounds(16, 96, 400, 22); _sceneNumbers.Checked = current.SceneNumbers;
        _revisionMarks.SetBounds(16, 120, 400, 22); _revisionMarks.Checked = current.RevisionMarks;
        Controls.Add(_titlePage); Controls.Add(_pageNumbers);
        Controls.Add(_sceneNumbers); Controls.Add(_revisionMarks);

        Controls.Add(new Label { Text = "Filigrana", Left = 14, Top = 157, Width = 100 });
        _watermark.SetBounds(120, 154, 300, 23);
        _watermark.Text = current.Watermark ?? string.Empty;
        _watermark.PlaceholderText = "BOZZA - NON DISTRIBUIRE";
        Controls.Add(_watermark);

        Controls.Add(new Label { Text = "Copia per", Left = 14, Top = 189, Width = 100 });
        _copyFor.SetBounds(120, 186, 300, 23);
        _copyFor.Text = current.CopyFor ?? string.Empty;
        _copyFor.PlaceholderText = "nome del destinatario";
        Controls.Add(_copyFor);

        Controls.Add(new Label
        {
            Text = "La filigrana appare in diagonale su ogni pagina, il destinatario in fondo.",
            Left = 16, Top = 216, Width = 410,
            ForeColor = Color.FromArgb(110, 110, 110)
        });

        var ok = new Button { Text = "Esporta...", DialogResult = DialogResult.OK, Width = 100, Left = 228, Top = 250 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Left = 332, Top = 250 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (s, e) =>
        {
            var ps = (_paper.SelectedItem as string) == "Letter" ? PageSetup.Letter() : PageSetup.A4();
            ps.IncludeTitlePage = _titlePage.Checked;
            ps.PageNumbers = _pageNumbers.Checked;
            ps.SceneNumbers = _sceneNumbers.Checked;
            ps.RevisionMarks = _revisionMarks.Checked;
            ps.Watermark = string.IsNullOrWhiteSpace(_watermark.Text) ? null : _watermark.Text.Trim();
            ps.CopyFor = string.IsNullOrWhiteSpace(_copyFor.Text) ? null : _copyFor.Text.Trim();
            Setup = ps;
        };
    }
}
