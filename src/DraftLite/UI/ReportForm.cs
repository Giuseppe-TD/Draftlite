using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DraftLite.IO;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>Statistiche del copione: quante pagine, quante scene, chi parla quanto.</summary>
public sealed class ReportForm : Form
{
    private readonly Screenplay _sp;
    private readonly PageSetup _setup;
    private readonly ScreenplayStats _stats;

    private readonly TextBox _summary = new TextBox
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Color.White,
        Font = new Font("Consolas", 10f)
    };
    private readonly ListView _chars = new ListView { View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ListView _scenes = new ListView { View = View.Details, FullRowSelect = true, GridLines = true };

    private string _plain = string.Empty;

    public ReportForm(Screenplay sp, PageSetup setup)
    {
        _sp = sp;
        _setup = setup;
        _stats = ScreenplayStats.Compute(sp, setup);

        Text = "Statistiche";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 540);
        Font = new Font("Segoe UI", 9f);
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var t1 = new TabPage("Riepilogo");
        _summary.Dock = DockStyle.Fill;
        t1.Controls.Add(_summary);

        var t2 = new TabPage("Personaggi");
        _chars.Dock = DockStyle.Fill;
        _chars.Columns.Add("Personaggio", 220);
        _chars.Columns.Add("Battute", 80, HorizontalAlignment.Right);
        _chars.Columns.Add("Parole", 80, HorizontalAlignment.Right);
        _chars.Columns.Add("% parlato", 90, HorizontalAlignment.Right);
        _chars.Columns.Add("Scene", 80, HorizontalAlignment.Right);
        _chars.Columns.Add("1a scena", 80, HorizontalAlignment.Right);
        t2.Controls.Add(_chars);

        var t3 = new TabPage("Scene");
        _scenes.Dock = DockStyle.Fill;
        _scenes.Columns.Add("#", 50, HorizontalAlignment.Right);
        _scenes.Columns.Add("Pag.", 50, HorizontalAlignment.Right);
        _scenes.Columns.Add("Intestazione", 330);
        _scenes.Columns.Add("Personaggi", 180);
        _scenes.Columns.Add("Sinossi", 200);
        t3.Controls.Add(_scenes);

        tabs.TabPages.Add(t1);
        tabs.TabPages.Add(t2);
        tabs.TabPages.Add(t3);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var pdf = new Button { Text = "Esporta PDF...", Width = 120, Left = 470, Top = 9 };
        var copy = new Button { Text = "Copia tutto", Width = 110, Left = 596, Top = 9 };
        var close = new Button { Text = "Chiudi", Width = 100, Left = 712, Top = 9, DialogResult = DialogResult.OK };

        pdf.Click += (s, e) => ExportPdf();
        copy.Click += (s, e) => { try { Clipboard.SetText(_plain); } catch { } };

        bottom.Controls.Add(pdf);
        bottom.Controls.Add(copy);
        bottom.Controls.Add(close);

        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = close;

        Fill();
    }

    private void Fill()
    {
        var st = _stats;
        var sb = new StringBuilder();
        sb.AppendLine("  RIEPILOGO");
        sb.AppendLine("  ---------");
        sb.AppendLine($"  Titolo................. {(string.IsNullOrWhiteSpace(st.Title) ? "(senza titolo)" : st.Title)}");
        sb.AppendLine($"  Autore................. {(string.IsNullOrWhiteSpace(st.Author) ? "-" : st.Author)}");
        sb.AppendLine();
        sb.AppendLine($"  Pagine................. {st.Pages}  ({st.Paper}, {st.LinesPerPage} righe)");
        sb.AppendLine($"  Durata stimata......... {(int)st.Duration.TotalHours:00}:{st.Duration.Minutes:00}  (1 pagina = 1 minuto)");
        sb.AppendLine($"  Scene.................. {st.SceneCount}   (interni {st.IntCount} / esterni {st.ExtCount})");
        sb.AppendLine($"  Giorno / notte......... {st.DayCount} / {st.NightCount}");
        sb.AppendLine($"  Personaggi parlanti.... {st.Characters.Count}");
        sb.AppendLine($"  Battute totali......... {st.TotalSpeeches}");
        sb.AppendLine($"  Parole di dialogo...... {st.DialogueWords}");
        sb.AppendLine($"  Parole di azione....... {st.ActionWords}");
        int totalWords = st.DialogueWords + st.ActionWords;
        if (totalWords > 0)
            sb.AppendLine($"  Dialogo / azione....... {100.0 * st.DialogueWords / totalWords:0}% / {100.0 * st.ActionWords / totalWords:0}%");
        if (st.SceneCount > 0)
            sb.AppendLine($"  Lunghezza media scena.. {(double)st.Pages / st.SceneCount:0.0} pagine");
        if (_sp.Revision != null && _sp.Revision.Active)
            sb.AppendLine($"  Bozza.................. {_sp.Revision.Color} {_sp.Revision.Date}");

        _summary.Text = sb.ToString().Replace("\n", "\r\n");

        foreach (var c in st.Characters)
        {
            var it = new ListViewItem(c.Name);
            it.SubItems.Add(c.Speeches.ToString());
            it.SubItems.Add(c.Words.ToString());
            it.SubItems.Add(c.SharePercent.ToString("0.0") + "%");
            it.SubItems.Add(c.SceneCount.ToString());
            it.SubItems.Add(c.FirstScene);
            _chars.Items.Add(it);
        }

        foreach (var sc in st.Scenes)
        {
            var it = new ListViewItem(sc.Number);
            it.SubItems.Add(sc.Page > 0 ? sc.Page.ToString() : "-");
            it.SubItems.Add(sc.Heading);
            it.SubItems.Add(string.Join(", ", sc.Characters.Take(4)) + (sc.Characters.Count > 4 ? "..." : ""));
            it.SubItems.Add(sc.Synopsis ?? string.Empty);
            _scenes.Items.Add(it);
        }

        var plain = new StringBuilder();
        plain.AppendLine(_summary.Text);
        plain.AppendLine();
        plain.AppendLine("PERSONAGGIO\tBATTUTE\tPAROLE\t% PARLATO\tSCENE\t1a SCENA");
        foreach (ListViewItem it in _chars.Items)
            plain.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
        plain.AppendLine();
        plain.AppendLine("#\tPAG.\tINTESTAZIONE\tPERSONAGGI\tSINOSSI");
        foreach (ListViewItem it in _scenes.Items)
            plain.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
        _plain = plain.ToString();
    }

    private void ExportPdf()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = "statistiche.pdf"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.ExportReport(dlg.FileName, _sp, _setup);
            MessageBox.Show(this, "Report salvato.", "DraftLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
