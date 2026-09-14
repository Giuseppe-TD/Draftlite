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
    private sealed class CharStat
    {
        public string Name;
        public int Speeches;
        public int Words;
        public int FirstScene;
        public readonly HashSet<int> Scenes = new HashSet<int>();
    }

    private readonly Screenplay _sp;
    private readonly PageSetup _setup;
    private readonly TextBox _summary = new TextBox
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Color.White
    };
    private readonly ListView _chars = new ListView { View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ListView _scenes = new ListView { View = View.Details, FullRowSelect = true, GridLines = true };

    public ReportForm(Screenplay sp, PageSetup setup)
    {
        _sp = sp;
        _setup = setup;

        Text = "Statistiche";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 520);
        Font = new Font("Segoe UI", 9f);
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var t1 = new TabPage("Riepilogo");
        _summary.Dock = DockStyle.Fill;
        _summary.Font = new Font("Consolas", 10f);
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
        _scenes.Columns.Add("#", 40, HorizontalAlignment.Right);
        _scenes.Columns.Add("Intestazione", 400);
        _scenes.Columns.Add("Pag.", 60, HorizontalAlignment.Right);
        _scenes.Columns.Add("Righe", 70, HorizontalAlignment.Right);
        _scenes.Columns.Add("Personaggi", 150);
        t3.Controls.Add(_scenes);

        tabs.TabPages.Add(t1);
        tabs.TabPages.Add(t2);
        tabs.TabPages.Add(t3);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var copy = new Button { Text = "Copia tutto", Width = 110, Left = 520, Top = 8 };
        var close = new Button { Text = "Chiudi", Width = 100, Left = 640, Top = 8, DialogResult = DialogResult.OK };
        copy.Click += (s, e) => { Clipboard.SetText(BuildPlainText()); };
        bottom.Controls.Add(copy);
        bottom.Controls.Add(close);

        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = close;

        Build();
    }

    private string _plain = string.Empty;

    private void Build()
    {
        var els = _sp.Compacted();

        // pagina di ogni scena
        var setup = _setup.Clone();
        setup.SceneNumbers = true;
        var pages = Paginator.Paginate(_sp, setup);
        var scenePage = new Dictionary<string, int>();
        for (int p = 0; p < pages.Count; p++)
            foreach (var l in pages[p].Lines)
                if (!string.IsNullOrEmpty(l.SceneNumber) && !scenePage.ContainsKey(l.SceneNumber))
                    scenePage[l.SceneNumber] = p + 1;

        var stats = new Dictionary<string, CharStat>(StringComparer.OrdinalIgnoreCase);
        var sceneRows = new List<(int No, string Head, int Lines, HashSet<string> Chars)>();

        int sceneNo = 0;
        int intCount = 0, extCount = 0, dayCount = 0, nightCount = 0;
        int totalWords = 0, totalSpeeches = 0, actionWords = 0;
        string current = null;
        HashSet<string> sceneChars = null;
        int sceneLines = 0;

        void CloseScene()
        {
            if (sceneNo > 0)
            {
                var row = sceneRows[sceneRows.Count - 1];
                sceneRows[sceneRows.Count - 1] = (row.No, row.Head, sceneLines, row.Chars);
            }
        }

        foreach (var e in els)
        {
            switch (e.Type)
            {
                case ElementType.SceneHeading:
                    CloseScene();
                    sceneNo++;
                    sceneLines = 0;
                    sceneChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    sceneRows.Add((sceneNo, e.Text, 0, sceneChars));
                    var up = e.Text.ToUpperInvariant();
                    if (up.StartsWith("INT")) intCount++;
                    if (up.StartsWith("EST") || up.StartsWith("EXT")) extCount++;
                    if (up.Contains("NOTTE") || up.Contains("NIGHT")) nightCount++;
                    else if (up.Contains("GIORNO") || up.Contains("DAY") || up.Contains("MATTIN") ||
                             up.Contains("POMERIGG")) dayCount++;
                    break;

                case ElementType.Character:
                    current = Screenplay.NormalizeCharacterName(e.Text);
                    if (current.Length > 0)
                    {
                        if (!stats.TryGetValue(current, out var st))
                        {
                            st = new CharStat { Name = current, FirstScene = Math.Max(1, sceneNo) };
                            stats[current] = st;
                        }
                        st.Speeches++;
                        totalSpeeches++;
                        if (sceneNo > 0) st.Scenes.Add(sceneNo);
                        sceneChars?.Add(current);
                    }
                    break;

                case ElementType.Dialogue:
                    int w = CountWords(e.Text);
                    totalWords += w;
                    if (current != null && stats.TryGetValue(current, out var cs)) cs.Words += w;
                    break;

                case ElementType.Action:
                    actionWords += CountWords(e.Text);
                    break;
            }

            sceneLines += Paginator.Wrap(e.Text, ElementStyle.Get(e.Type).Cols).Count +
                          ElementStyle.Get(e.Type).SpaceBeforeLines;
        }
        CloseScene();

        int pageCount = pages.Count;
        var dur = TimeSpan.FromMinutes(pageCount);

        var sb = new StringBuilder();
        sb.AppendLine("  RIEPILOGO");
        sb.AppendLine("  ---------");
        sb.AppendLine($"  Titolo................. {(string.IsNullOrWhiteSpace(_sp.TitlePage.Title) ? "(senza titolo)" : _sp.TitlePage.Title)}");
        sb.AppendLine($"  Autore................. {(string.IsNullOrWhiteSpace(_sp.TitlePage.Author) ? "-" : _sp.TitlePage.Author)}");
        sb.AppendLine();
        sb.AppendLine($"  Pagine................. {pageCount}  ({setup.PaperName}, {setup.LinesPerPage} righe)");
        sb.AppendLine($"  Durata stimata......... {(int)dur.TotalHours:00}:{dur.Minutes:00}  (1 pagina = 1 minuto)");
        sb.AppendLine($"  Scene.................. {sceneNo}   (interni {intCount} / esterni {extCount})");
        sb.AppendLine($"  Giorno / notte......... {dayCount} / {nightCount}");
        sb.AppendLine($"  Personaggi parlanti.... {stats.Count}");
        sb.AppendLine($"  Battute totali......... {totalSpeeches}");
        sb.AppendLine($"  Parole di dialogo...... {totalWords}");
        sb.AppendLine($"  Parole di azione....... {actionWords}");
        if (totalWords + actionWords > 0)
            sb.AppendLine($"  Rapporto dialogo/azione {100.0 * totalWords / (totalWords + actionWords):0}% / {100.0 * actionWords / (totalWords + actionWords):0}%");
        if (sceneNo > 0)
            sb.AppendLine($"  Lunghezza media scena.. {(double)pageCount / sceneNo:0.0} pagine");
        _summary.Text = sb.ToString().Replace("\n", "\r\n");

        foreach (var st in stats.Values.OrderByDescending(x => x.Words).ThenByDescending(x => x.Speeches))
        {
            var it = new ListViewItem(st.Name);
            it.SubItems.Add(st.Speeches.ToString());
            it.SubItems.Add(st.Words.ToString());
            it.SubItems.Add(totalWords > 0 ? (100.0 * st.Words / totalWords).ToString("0.0") + "%" : "-");
            it.SubItems.Add(st.Scenes.Count.ToString());
            it.SubItems.Add(st.FirstScene.ToString());
            _chars.Items.Add(it);
        }

        foreach (var s in sceneRows)
        {
            var it = new ListViewItem(s.No.ToString());
            it.SubItems.Add(s.Head);
            it.SubItems.Add(scenePage.TryGetValue(s.No.ToString(), out var pg) ? pg.ToString() : "-");
            it.SubItems.Add(s.Lines.ToString());
            it.SubItems.Add(string.Join(", ", s.Chars.Take(4)) + (s.Chars.Count > 4 ? "..." : ""));
            _scenes.Items.Add(it);
        }

        var plain = new StringBuilder();
        plain.AppendLine(_summary.Text);
        plain.AppendLine();
        plain.AppendLine("PERSONAGGIO\tBATTUTE\tPAROLE\t% PARLATO\tSCENE\t1a SCENA");
        foreach (ListViewItem it in _chars.Items)
            plain.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
        plain.AppendLine();
        plain.AppendLine("#\tINTESTAZIONE\tPAG.\tRIGHE\tPERSONAGGI");
        foreach (ListViewItem it in _scenes.Items)
            plain.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
        _plain = plain.ToString();
    }

    private string BuildPlainText() => _plain;

    private static int CountWords(string s)
        => string.IsNullOrWhiteSpace(s) ? 0 : s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
