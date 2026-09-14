using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>
/// Colori dell'interfaccia e del testo. Ogni tipo di elemento ha il suo colore:
/// scorrendo la pagina si capisce al volo dove sono le scene e chi parla,
/// senza doverlo leggere. Il PDF resta nero su bianco, come deve essere.
/// </summary>
public sealed class Theme
{
    public string Name = "Carta";
    public Color Paper = Color.FromArgb(251, 250, 246);
    public Color Ink = Color.FromArgb(35, 32, 27);
    public Color Desk = Color.FromArgb(108, 104, 97);
    public Color Panel = Color.FromArgb(239, 237, 231);
    public Color PanelText = Color.FromArgb(46, 42, 36);
    public Color NoteBack = Color.FromArgb(251, 239, 192);
    public Color Rule = Color.FromArgb(178, 172, 160);      // interruzioni di pagina
    public Color Shadow = Color.FromArgb(60, 0, 0, 0);

    public Color SceneColor = Color.FromArgb(28, 61, 90);
    public Color ActionColor = Color.FromArgb(35, 32, 27);
    public Color CharacterColor = Color.FromArgb(107, 58, 31);
    public Color ParentheticalColor = Color.FromArgb(106, 102, 94);
    public Color DialogueColor = Color.FromArgb(35, 32, 27);
    public Color TransitionColor = Color.FromArgb(74, 68, 89);

    public Dictionary<ElementType, Color> ElementColors => new Dictionary<ElementType, Color>
    {
        [ElementType.SceneHeading] = SceneColor,
        [ElementType.Action] = ActionColor,
        [ElementType.Character] = CharacterColor,
        [ElementType.Parenthetical] = ParentheticalColor,
        [ElementType.Dialogue] = DialogueColor,
        [ElementType.Transition] = TransitionColor
    };

    /// <summary>Carta avorio: il bianco pieno per ore stanca.</summary>
    public static readonly Theme Paper1 = new Theme();

    public static readonly Theme Light = new Theme
    {
        Name = "Chiaro",
        Paper = Color.White,
        Ink = Color.FromArgb(26, 26, 26),
        Desk = Color.FromArgb(124, 124, 130),
        Panel = Color.FromArgb(244, 244, 246),
        PanelText = Color.FromArgb(32, 32, 32),
        NoteBack = Color.FromArgb(255, 246, 200),
        Rule = Color.FromArgb(190, 190, 196),
        SceneColor = Color.FromArgb(22, 58, 96),
        ActionColor = Color.FromArgb(26, 26, 26),
        CharacterColor = Color.FromArgb(120, 60, 24),
        ParentheticalColor = Color.FromArgb(110, 110, 110),
        DialogueColor = Color.FromArgb(26, 26, 26),
        TransitionColor = Color.FromArgb(72, 66, 96)
    };

    public static readonly Theme Dark = new Theme
    {
        Name = "Scuro",
        Paper = Color.FromArgb(30, 31, 34),
        Ink = Color.FromArgb(226, 226, 226),
        Desk = Color.FromArgb(18, 19, 22),
        Panel = Color.FromArgb(38, 40, 44),
        PanelText = Color.FromArgb(222, 222, 222),
        NoteBack = Color.FromArgb(74, 67, 32),
        Rule = Color.FromArgb(72, 74, 80),
        Shadow = Color.FromArgb(90, 0, 0, 0),
        SceneColor = Color.FromArgb(126, 178, 232),
        ActionColor = Color.FromArgb(226, 226, 226),
        CharacterColor = Color.FromArgb(226, 170, 120),
        ParentheticalColor = Color.FromArgb(154, 154, 154),
        DialogueColor = Color.FromArgb(226, 226, 226),
        TransitionColor = Color.FromArgb(185, 174, 224)
    };

    public static readonly Theme Sepia = new Theme
    {
        Name = "Seppia",
        Paper = Color.FromArgb(246, 238, 220),
        Ink = Color.FromArgb(59, 50, 40),
        Desk = Color.FromArgb(128, 116, 96),
        Panel = Color.FromArgb(236, 227, 207),
        PanelText = Color.FromArgb(52, 44, 34),
        NoteBack = Color.FromArgb(234, 223, 168),
        Rule = Color.FromArgb(176, 160, 132),
        SceneColor = Color.FromArgb(78, 54, 26),
        ActionColor = Color.FromArgb(59, 50, 40),
        CharacterColor = Color.FromArgb(122, 52, 30),
        ParentheticalColor = Color.FromArgb(120, 108, 90),
        DialogueColor = Color.FromArgb(59, 50, 40),
        TransitionColor = Color.FromArgb(90, 72, 96)
    };

    public static IEnumerable<Theme> All
    {
        get { yield return Paper1; yield return Light; yield return Sepia; yield return Dark; }
    }

    public static Theme ByName(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Paper1;

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
