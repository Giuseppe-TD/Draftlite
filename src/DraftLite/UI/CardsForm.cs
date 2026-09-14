using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>
/// La bacheca delle scene: una scheda per scena, con sinossi e colore.
/// Si trascinano per cambiare l'ordine del copione, doppio clic per saltare alla scena.
/// </summary>
public sealed class CardsForm : Form
{
    public sealed class CardData
    {
        public int SceneIndex;       // posizione originale della scena nel copione
        public string Number = string.Empty;
        public string Heading = string.Empty;
        public string Synopsis = string.Empty;
        public string Color;
        public int Pages;
    }

    private static readonly (string Name, string Hex)[] Palette =
    {
        ("Nessuno", null),
        ("Giallo", "#FFE9A8"),
        ("Verde", "#CDE9C4"),
        ("Azzurro", "#C6DEF6"),
        ("Rosa", "#F5CEDC"),
        ("Arancio", "#F8D3AE"),
        ("Grigio", "#DDDDDD")
    };

    private readonly FlowLayoutPanel _flow = new FlowLayoutPanel
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(14),
        AllowDrop = true,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true
    };

    private readonly List<CardData> _cards;
    private Panel _dragged;
    private Panel _pressed;
    private Point _pressPoint;

    /// <summary>Schede nell'ordine finale, con sinossi e colori aggiornati.</summary>
    public List<CardData> Result { get; private set; }

    /// <summary>Scena su cui saltare alla chiusura (doppio clic), -1 se nessuna.</summary>
    public int GoToScene { get; private set; } = -1;

    public CardsForm(List<CardData> cards, Theme theme)
    {
        _cards = cards;
        Text = "Schede scena";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 650);
        Font = new Font("Segoe UI", 9f);
        MinimizeBox = false;
        BackColor = theme.Panel;
        ForeColor = theme.PanelText;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = theme.Panel };
        var hint = new Label
        {
            Text = "  Trascina una scheda per spostare la scena - doppio clic per aprirla nel copione",
            Dock = DockStyle.Left,
            Width = 560,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = theme.PanelText
        };
        var ok = new Button { Text = "Applica", DialogResult = DialogResult.OK, Width = 100, Left = 770, Top = 9 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Left = 878, Top = 9 };
        bottom.Controls.Add(hint);
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        _flow.BackColor = theme.Desk;
        Controls.Add(_flow);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;

        _flow.DragOver += Flow_DragOver;
        _flow.DragDrop += Flow_DragDrop;

        foreach (var c in _cards) _flow.Controls.Add(BuildCard(c, theme));

        FormClosing += (s, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            Result = _flow.Controls.OfType<Panel>()
                         .Select(p => (CardData)p.Tag)
                         .ToList();
        };
    }

    private Panel BuildCard(CardData data, Theme theme)
    {
        var card = new Panel
        {
            Width = 230,
            Height = 180,
            Margin = new Padding(8),
            BackColor = theme.Paper,
            BorderStyle = BorderStyle.FixedSingle,
            Tag = data
        };

        var strip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 6,
            BackColor = ParseColor(data.Color) ?? Color.FromArgb(200, 200, 205)
        };

        var head = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Text = data.Number + ".  " + data.Heading,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = theme.Ink,
            BackColor = theme.Paper,
            Padding = new Padding(6, 4, 4, 0)
        };

        var synopsis = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            Text = data.Synopsis ?? string.Empty,
            ScrollBars = ScrollBars.Vertical,
            BackColor = theme.Paper,
            ForeColor = theme.Ink
        };
        synopsis.TextChanged += (s, e) => data.Synopsis = synopsis.Text.Trim();

        var foot = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Text = "  colore...",
            ForeColor = Color.FromArgb(130, 130, 130),
            BackColor = theme.Paper,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System, BackColor = theme.Panel, ForeColor = theme.PanelText };
        card.Disposed += (s, e) => menu.Dispose();
        foreach (var (name, hex) in Palette)
        {
            var item = new ToolStripMenuItem(name);
            var captured = hex;
            item.Click += (s, e) =>
            {
                data.Color = captured;
                strip.BackColor = ParseColor(captured) ?? Color.FromArgb(200, 200, 205);
            };
            menu.Items.Add(item);
        }
        foot.Click += (s, e) => menu.Show(foot, new Point(6, foot.Height));

        card.Controls.Add(synopsis);
        card.Controls.Add(foot);
        card.Controls.Add(head);
        card.Controls.Add(strip);

        void Hook(Control c)
        {
            if (!(c is TextBox))
            {
                c.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left) { _pressed = card; _pressPoint = c.PointToScreen(e.Location); }
                };
                c.MouseUp += (s, e) => { _pressed = null; };
                c.MouseMove += (s, e) =>
                {
                    if (_pressed != card || e.Button != MouseButtons.Left) return;
                    var now = c.PointToScreen(e.Location);
                    if (Math.Abs(now.X - _pressPoint.X) < SystemInformation.DragSize.Width &&
                        Math.Abs(now.Y - _pressPoint.Y) < SystemInformation.DragSize.Height) return;
                    _pressed = null;
                    _dragged = card;
                    card.DoDragDrop(card, DragDropEffects.Move);
                    _dragged = null;
                };
                c.DoubleClick += (s, e) =>
                {
                    _pressed = null;
                    GoToScene = data.SceneIndex;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            foreach (Control child in c.Controls) Hook(child);
        }
        Hook(card);

        return card;
    }

    private void Flow_DragOver(object sender, DragEventArgs e)
    {
        e.Effect = _dragged != null && e.Data.GetDataPresent(typeof(Panel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void Flow_DragDrop(object sender, DragEventArgs e)
    {
        if (_dragged == null || !e.Data.GetDataPresent(typeof(Panel))) return;
        var pt = _flow.PointToClient(new Point(e.X, e.Y));
        var target = _flow.GetChildAtPoint(pt);
        while (target != null && target.Parent != _flow) target = target.Parent;

        int index = target != null ? _flow.Controls.GetChildIndex(target) : _flow.Controls.Count - 1;
        _flow.Controls.SetChildIndex(_dragged, index);
        _dragged = null;
        _flow.Invalidate(true);
    }

    public static Color? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) return null;
            return Color.FromArgb(
                Convert.ToInt32(h.Substring(0, 2), 16),
                Convert.ToInt32(h.Substring(2, 2), 16),
                Convert.ToInt32(h.Substring(4, 2), 16));
        }
        catch { return null; }
    }
}
