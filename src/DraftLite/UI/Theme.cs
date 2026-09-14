using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>Colori dell'interfaccia. Tre temi, niente fronzoli.</summary>
public sealed class Theme
{
    public string Name = "Chiaro";
    public Color Paper = Color.White;          // foglio
    public Color Ink = Color.FromArgb(26, 26, 26);
    public Color Desk = Color.FromArgb(120, 120, 124);  // sfondo attorno al foglio
    public Color Panel = Color.FromArgb(243, 243, 245);
    public Color PanelText = Color.FromArgb(32, 32, 32);
    public Color NoteBack = Color.FromArgb(255, 246, 200);

    public static readonly Theme Light = new Theme();

    public static readonly Theme Dark = new Theme
    {
        Name = "Scuro",
        Paper = Color.FromArgb(30, 31, 34),
        Ink = Color.FromArgb(228, 228, 228),
        Desk = Color.FromArgb(18, 19, 22),
        Panel = Color.FromArgb(38, 40, 44),
        PanelText = Color.FromArgb(222, 222, 222),
        NoteBack = Color.FromArgb(74, 67, 32)
    };

    public static readonly Theme Sepia = new Theme
    {
        Name = "Seppia",
        Paper = Color.FromArgb(246, 240, 226),
        Ink = Color.FromArgb(59, 50, 40),
        Desk = Color.FromArgb(138, 127, 107),
        Panel = Color.FromArgb(236, 228, 210),
        PanelText = Color.FromArgb(52, 44, 34),
        NoteBack = Color.FromArgb(234, 223, 168)
    };

    public static IEnumerable<Theme> All { get { yield return Light; yield return Dark; yield return Sepia; } }

    public static Theme ByName(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Light;

    /// <summary>Applica il tema a menu, pannelli e liste (WinForms non ha un tema scuro suo).</summary>
    public void ApplyTo(Control root)
    {
        foreach (Control c in root.Controls) ApplyRecursive(c);
    }

    private void ApplyRecursive(Control c)
    {
        switch (c)
        {
            case MenuStrip ms:
                ms.BackColor = Panel; ms.ForeColor = PanelText;
                foreach (ToolStripItem it in ms.Items) ApplyItem(it);
                break;
            // StatusStrip deriva da ToolStrip: va valutato prima
            case StatusStrip ss:
                ss.BackColor = Panel; ss.ForeColor = PanelText;
                foreach (ToolStripItem it in ss.Items) ApplyItem(it);
                break;
            case ToolStrip ts:
                ts.BackColor = Panel; ts.ForeColor = PanelText;
                foreach (ToolStripItem it in ts.Items) ApplyItem(it);
                break;
            case ListBox lb:
                lb.BackColor = Panel; lb.ForeColor = PanelText;
                break;
            case TabControl tc:
                foreach (TabPage tp in tc.TabPages) { tp.BackColor = Panel; tp.ForeColor = PanelText; }
                break;
            case Label lab:
                lab.BackColor = Panel; lab.ForeColor = PanelText;
                break;
            case Panel pnl when pnl.Tag as string == "desk":
                pnl.BackColor = Desk;
                break;
            case Panel pnl when pnl.Tag as string == "page":
                pnl.BackColor = Paper;
                break;
            case SplitContainer sc:
                sc.BackColor = Panel;
                break;
        }

        foreach (Control child in c.Controls) ApplyRecursive(child);
    }

    private void ApplyItem(ToolStripItem item)
    {
        item.BackColor = Panel;
        item.ForeColor = PanelText;
        if (item is ToolStripMenuItem mi)
            foreach (ToolStripItem sub in mi.DropDownItems) ApplyItem(sub);
    }
}
