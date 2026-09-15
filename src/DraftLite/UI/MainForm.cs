using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DraftLite.Editor;
using DraftLite.IO;
using DraftLite.Model;

namespace DraftLite.UI;

public sealed class MainForm : Form
{
    private enum DocFormat { DraftLite, Fountain, Fdx }

    private readonly ScreenplayEditor _editor = new ScreenplayEditor();
    private readonly ListBox _sceneList = new ListBox
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        AllowDrop = true
    };
    private readonly ListBox _noteList = new ListBox
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false
    };
    private readonly TabControl _sideTabs = new TabControl { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill };
    private readonly Panel _pageHost = new Panel { Dock = DockStyle.Fill, Tag = "desk", AutoScroll = true };
    private readonly Panel _page = new Panel { Padding = new Padding(34, 18, 14, 10), Tag = "page" };
    private readonly RulerStrip _ruler = new RulerStrip();
    private readonly StatusStrip _status = new StatusStrip();
    private readonly ToolStripStatusLabel _lblType = new ToolStripStatusLabel { AutoSize = false, Width = 150, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblScene = new ToolStripStatusLabel { AutoSize = false, Width = 300, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblPages = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly System.Windows.Forms.Timer _statsTimer = new System.Windows.Forms.Timer { Interval = 4000 };
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 120000 };

    private AppSettings _settings = AppSettings.Load();
    private Theme _theme = Theme.Light;
    private TitlePage _titlePage = new TitlePage();
    private Revision _revision = new Revision();
    private List<(int CharIndex, int Number, string Text)> _scenes = new List<(int, int, string)>();
    private List<(int CharIndex, int Paragraph, string Note, string Text)> _notes =
        new List<(int, int, string, string)>();
    private string _path;
    private DocFormat _format = DocFormat.DraftLite;
    private bool _dirty;
    private bool _loading;
    private bool _statsDirty = true;
    private FindForm _findForm;
    private ToolStripMenuItem _typewriterItem;
    private ToolStripMenuItem _askElementItem;
    private ToolStripMenuItem _navItem;
    private ToolStripMenuItem _breaksItem;
    private ToolStripMenuItem _rulerItem;
    private List<(int CharIndex, int Page)> _pageBreaks = new List<(int, int)>();
    private Font _gutterFont = new Font("Segoe UI", 7f, FontStyle.Bold);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gutterFont?.Dispose();
            _statsTimer?.Dispose();
            _autoSaveTimer?.Dispose();
            _tips?.Dispose();
        }
        base.Dispose(disposing);
    }

    public MainForm(string openPath)
    {
        Text = "DraftLite";
        ClientSize = new Size(1180, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildUi();
        WireEvents();

        _editor.ZoomFactor = Math.Max(0.5f, Math.Min(3f, _settings.Zoom));
        _split.Panel1Collapsed = !_settings.ShowNavigator;

        NewDocument();
        ApplyAppearance();

        if (!string.IsNullOrEmpty(openPath) && File.Exists(openPath))
            OpenFile(openPath);
        else
            OfferAutoSaveRecovery();

        _autoSaveTimer.Start();
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var menu = BuildMenu();
        var ribbon = BuildRibbon();

        _page.Controls.Add(_editor);
        _editor.Dock = DockStyle.Fill;
        _pageHost.Controls.Add(_page);
        _pageHost.Controls.Add(_ruler);
        _ruler.Visible = _settings.ShowRuler;
        _ruler.BringToFront();

        var tabScenes = new TabPage("Scene");
        tabScenes.Controls.Add(_sceneList);
        var tabNotes = new TabPage("Note");
        tabNotes.Controls.Add(_noteList);
        _sideTabs.TabPages.Add(tabScenes);
        _sideTabs.TabPages.Add(tabNotes);

        _split.Panel1.Controls.Add(_sideTabs);
        _split.Panel2.Controls.Add(_pageHost);
        _split.FixedPanel = FixedPanel.Panel1;

        _status.Items.AddRange(new ToolStripItem[] { _lblType, _lblScene, _lblPages });

        Controls.Add(_split);
        Controls.Add(ribbon);
        Controls.Add(menu);
        Controls.Add(_status);
        MainMenuStrip = menu;
    }

    /// <summary>Colori pronti per il testo e per l'evidenziatore.</summary>
    private static readonly (string Name, string Hex)[] TextPalette =
    {
        ("Automatico (colore dell'elemento)", null),
        ("Nero", "#1A1A1A"),
        ("Rosso", "#C0392B"),
        ("Blu", "#2E5FA3"),
        ("Verde", "#2E7D4F"),
        ("Arancio", "#D98026"),
        ("Viola", "#7A4FA3"),
        ("Grigio", "#7A7A7A")
    };

    private static readonly (string Name, string Hex)[] HighlightPalette =
    {
        ("Nessuno", null),
        ("Giallo", "#FFF176"),
        ("Verde", "#C5E1A5"),
        ("Azzurro", "#A7D8F0"),
        ("Rosa", "#F8BBD0"),
        ("Arancio", "#FFCC80"),
        ("Grigio", "#DDDDDD")
    };

    private static Bitmap Swatch(string hex)
    {
        var bmp = new Bitmap(14, 14);
        using var g = Graphics.FromImage(bmp);
        var c = CardsForm.ParseColor(hex);
        g.Clear(c ?? Color.White);
        using var pen = new Pen(Color.FromArgb(120, 120, 120));
        g.DrawRectangle(pen, 0, 0, 13, 13);
        if (c == null)
        {
            using var red = new Pen(Color.FromArgb(180, 60, 60), 1.5f);
            g.DrawLine(red, 2, 12, 12, 2);
        }
        return bmp;
    }

    /// <summary>Riempie un menu con la tavolozza, piu' la voce per sceglierne uno qualunque.</summary>
    private void FillColorMenu(ToolStripDropDownItem parent, bool highlight)
    {
        parent.DropDownItems.Clear();
        foreach (var (name, hex) in highlight ? HighlightPalette : TextPalette)
        {
            var item = new ToolStripMenuItem(name) { Image = Swatch(hex), ImageScaling = ToolStripItemImageScaling.None };
            var captured = hex;
            item.Click += (s, e) =>
            {
                var color = CardsForm.ParseColor(captured);
                if (highlight) _editor.ApplyHighlight(color);
                else _editor.ApplyTextColor(color);
                _editor.Focus();
                SetDirty(true);
            };
            parent.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("Altro colore...");
        custom.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (highlight) _editor.ApplyHighlight(dlg.Color);
            else _editor.ApplyTextColor(dlg.Color);
            _editor.Focus();
            SetDirty(true);
        };
        parent.DropDownItems.Add(custom);
    }

    private static ToolStripMenuItem Mi(string text, Keys shortcut, EventHandler handler)
    {
        var it = new ToolStripMenuItem(text);
        if (shortcut != Keys.None) { it.ShortcutKeys = shortcut; it.ShowShortcutKeys = true; }
        if (handler != null) it.Click += handler;
        return it;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { RenderMode = ToolStripRenderMode.System };

        // ---------------- File
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Mi("Nuovo", Keys.Control | Keys.N, (s, e) => { if (ConfirmDiscard()) NewDocument(); }));
        file.DropDownItems.Add(Mi("Apri...", Keys.Control | Keys.O, (s, e) => OpenDialog()));
        file.DropDownItems.Add(Mi("Importa da Word, RTF o testo...", Keys.None, (s, e) => ImportDialog()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Mi("Salva", Keys.Control | Keys.S, (s, e) => Save()));
        file.DropDownItems.Add(Mi("Salva con nome...", Keys.Control | Keys.Shift | Keys.S, (s, e) => SaveAs()));
        file.DropDownItems.Add(new ToolStripSeparator());

        var export = new ToolStripMenuItem("Esporta");
        export.DropDownItems.Add(Mi("PDF...", Keys.Control | Keys.P, (s, e) => ExportPdf()));
        export.DropDownItems.Add(Mi("Sides per personaggio...", Keys.None, (s, e) => ExportSides()));
        export.DropDownItems.Add(Mi("Report statistiche (PDF)...", Keys.None, (s, e) => ExportReport()));
        export.DropDownItems.Add(new ToolStripSeparator());
        export.DropDownItems.Add(Mi("Fountain (.fountain)...", Keys.None, (s, e) => ExportAs(DocFormat.Fountain)));
        export.DropDownItems.Add(Mi("Final Draft (.fdx)...", Keys.None, (s, e) => ExportAs(DocFormat.Fdx)));
        file.DropDownItems.Add(export);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Mi("Esci", Keys.None, (s, e) => Close()));
        menu.Items.Add(file);

        // ---------------- Modifica
        var edit = new ToolStripMenuItem("&Modifica");
        edit.DropDownItems.Add(Mi("Annulla", Keys.Control | Keys.Z, (s, e) => _editor.Undo()));
        edit.DropDownItems.Add(Mi("Ripristina", Keys.Control | Keys.Y, (s, e) => _editor.Redo()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Mi("Taglia", Keys.Control | Keys.X, (s, e) => _editor.Cut()));
        edit.DropDownItems.Add(Mi("Copia", Keys.Control | Keys.C, (s, e) => _editor.Copy()));
        edit.DropDownItems.Add(Mi("Incolla", Keys.Control | Keys.V, (s, e) => _editor.PasteFromClipboard()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Mi("Seleziona tutto", Keys.Control | Keys.A, (s, e) => { _editor.SelectAll(); _editor.Focus(); }));
        edit.DropDownItems.Add(Mi("Seleziona la scena", Keys.Control | Keys.Shift | Keys.A, (s, e) => _editor.SelectScene()));
        edit.DropDownItems.Add(Mi("Elimina la selezione", Keys.None, (s, e) => DeleteSelection()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Mi("Trova e sostituisci...", Keys.Control | Keys.F, (s, e) => ShowFind()));
        edit.DropDownItems.Add(Mi("Vai a...", Keys.Control | Keys.G, (s, e) => GoTo()));
        edit.DropDownItems.Add(Mi("Rinomina personaggio...", Keys.None, (s, e) => RenameCharacter(null)));
        menu.Items.Add(edit);

        // ---------------- Formato
        var format = new ToolStripMenuItem("&Formato");
        int n = 1;
        foreach (var st in ElementStyle.All)
        {
            var type = st.Type;
            format.DropDownItems.Add(Mi(st.Name, Keys.Control | (Keys)((int)Keys.D1 + n - 1),
                (s, e) => { _editor.ApplyType(type); _editor.Focus(); }));
            n++;
        }
        format.DropDownItems.Add(new ToolStripSeparator());
        format.DropDownItems.Add(Mi("Grassetto", Keys.Control | Keys.B,
            (s, e) => { _editor.ToggleStyle(FontStyle.Bold); _editor.Focus(); }));
        format.DropDownItems.Add(Mi("Corsivo", Keys.Control | Keys.I,
            (s, e) => { _editor.ToggleStyle(FontStyle.Italic); _editor.Focus(); }));
        format.DropDownItems.Add(Mi("Sottolineato", Keys.Control | Keys.U,
            (s, e) => { _editor.ToggleStyle(FontStyle.Underline); _editor.Focus(); }));
        var textColor = new ToolStripMenuItem("Colore del testo");
        FillColorMenu(textColor, false);
        format.DropDownItems.Add(textColor);

        var highlightMenu = new ToolStripMenuItem("Evidenziatore");
        FillColorMenu(highlightMenu, true);
        format.DropDownItems.Add(highlightMenu);

        format.DropDownItems.Add(Mi("Maiuscolo / minuscolo", Keys.Shift | Keys.F3,
            (s, e) => { _editor.CycleCase(); SetDirty(true); _editor.Focus(); }));
        format.DropDownItems.Add(Mi("Ripristina il paragrafo", Keys.None,
            (s, e) => { _editor.RevertParagraph(); SetDirty(true); _editor.Focus(); }));

        format.DropDownItems.Add(new ToolStripSeparator());
        format.DropDownItems.Add(Mi("Dialogo simultaneo", Keys.Control | Keys.D,
            (s, e) => { _editor.ToggleDual(); _editor.Focus(); }));

        var ext = new ToolStripMenuItem("Estensione personaggio");
        foreach (var x in ScreenplayEditor.CharacterExtensions)
        {
            var captured = x;
            ext.DropDownItems.Add(Mi(x, Keys.None, (s, e) => { _editor.AppendCharacterExtension(captured); _editor.Focus(); }));
        }
        format.DropDownItems.Add(ext);
        menu.Items.Add(format);

        // ---------------- Inserisci
        var insert = new ToolStripMenuItem("&Inserisci");
        insert.DropDownItems.Add(Mi("Nuova scena", Keys.Control | Keys.Shift | Keys.N,
            (s, e) => _editor.InsertElement(ElementType.SceneHeading, string.Empty)));
        insert.DropDownItems.Add(Mi("Nuovo personaggio", Keys.None,
            (s, e) => _editor.InsertElement(ElementType.Character, string.Empty)));
        insert.DropDownItems.Add(Mi("Interruzione di pagina", Keys.Control | Keys.Enter,
            (s, e) => InsertPageBreak()));
        insert.DropDownItems.Add(new ToolStripSeparator());
        insert.DropDownItems.Add(Mi("Nota sull'elemento...", Keys.Control | Keys.M, (s, e) => EditNote()));

        var symbols = new ToolStripMenuItem("Simboli");
        FillSymbolMenu(symbols);
        insert.DropDownItems.Add(symbols);
        menu.Items.Add(insert);

        // ---------------- Vista
        var view = new ToolStripMenuItem("&Vista");
        _navItem = Mi("Pannello laterale", Keys.F9, null);
        _navItem.CheckOnClick = true;
        _navItem.Checked = _settings.ShowNavigator;
        _navItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowNavigator = _navItem.Checked;
            _split.Panel1Collapsed = !_navItem.Checked;
            _ribbon?.SetChecked("navigator", _navItem.Checked);
        };
        view.DropDownItems.Add(_navItem);
        view.DropDownItems.Add(Mi("Schede scena...", Keys.F6, (s, e) => ShowCards()));
        view.DropDownItems.Add(new ToolStripSeparator());

        _rulerItem = Mi("Righello", Keys.None, null);
        _rulerItem.CheckOnClick = true;
        _rulerItem.Checked = _settings.ShowRuler;
        _rulerItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowRuler = _rulerItem.Checked;
            _ruler.Visible = _rulerItem.Checked;
            _ribbon?.SetChecked("ruler", _rulerItem.Checked);
            LayoutPage();
        };
        view.DropDownItems.Add(_rulerItem);

        _typewriterItem = Mi("Macchina da scrivere", Keys.F11, null);
        _typewriterItem.CheckOnClick = true;
        _typewriterItem.Checked = _settings.Typewriter;
        _typewriterItem.CheckedChanged += (s, e) =>
        {
            _settings.Typewriter = _typewriterItem.Checked;
            _editor.TypewriterMode = _typewriterItem.Checked;
            _ribbon?.SetChecked("typewriter", _typewriterItem.Checked);
        };
        view.DropDownItems.Add(_typewriterItem);

        _askElementItem = Mi("Chiedi l'elemento andando a capo", Keys.None, null);
        _askElementItem.CheckOnClick = true;
        _askElementItem.Checked = _settings.AskElementOnEnter;
        _askElementItem.CheckedChanged += (s, e) =>
        {
            _settings.AskElementOnEnter = _askElementItem.Checked;
            _editor.AskElementOnEnter = _askElementItem.Checked;
            _ribbon?.SetChecked("askelement", _askElementItem.Checked);
        };
        view.DropDownItems.Add(_askElementItem);

        _breaksItem = Mi("Mostra le interruzioni di pagina", Keys.None, null);
        _breaksItem.CheckOnClick = true;
        _breaksItem.Checked = _settings.ShowPageBreaks;
        _breaksItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowPageBreaks = _breaksItem.Checked;
            _ribbon?.SetChecked("breaks", _breaksItem.Checked);
            _page.Invalidate();
        };
        view.DropDownItems.Add(_breaksItem);

        var themes = new ToolStripMenuItem("Tema");
        foreach (var t in Theme.All)
        {
            var captured = t.Name;
            themes.DropDownItems.Add(Mi(captured, Keys.None, (s, e) => SetTheme(captured)));
        }
        view.DropDownItems.Add(themes);
        view.DropDownItems.Add(Mi("Aspetto (carattere e tema)...", Keys.None, (s, e) => ShowAppearance()));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Mi("Ingrandisci", Keys.Control | Keys.Oemplus, (s, e) => Zoom(0.1f)));
        view.DropDownItems.Add(Mi("Riduci", Keys.Control | Keys.OemMinus, (s, e) => Zoom(-0.1f)));
        view.DropDownItems.Add(Mi("Dimensioni del foglio...", Keys.None, (s, e) => EditPageLayout()));
        view.DropDownItems.Add(Mi("Adatta alla finestra", Keys.Control | Keys.D9, (s, e) => FitPage()));
        view.DropDownItems.Add(Mi("Zoom 100%", Keys.Control | Keys.D0, (s, e) => SetZoom(1f)));
        menu.Items.Add(view);

        // ---------------- Strumenti
        var tools = new ToolStripMenuItem("&Strumenti");
        tools.DropDownItems.Add(Mi("Frontespizio...", Keys.F7, (s, e) => EditTitlePage()));
        tools.DropDownItems.Add(Mi("Statistiche...", Keys.F8, (s, e) => ShowReport()));
        tools.DropDownItems.Add(Mi("Elenco personaggi...", Keys.F4, (s, e) => ShowCastList()));

        var numbers = new ToolStripMenuItem("Numeri di scena");
        numbers.DropDownItems.Add(Mi("Blocca la numerazione", Keys.None, (s, e) => LockSceneNumbers(true)));
        numbers.DropDownItems.Add(Mi("Sblocca (torna a 1, 2, 3...)", Keys.None, (s, e) => LockSceneNumbers(false)));
        tools.DropDownItems.Add(numbers);

        tools.DropDownItems.Add(Mi("Revisione...", Keys.None, (s, e) => ShowRevision()));
        menu.Items.Add(tools);

        // ---------------- Aiuto
        var help = new ToolStripMenuItem("&?");
        help.DropDownItems.Add(Mi("Scorciatoie da tastiera", Keys.F1, (s, e) => ShowShortcuts()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Mi("Cerca aggiornamenti...", Keys.None,
            (s, e) => UpdateChecker.CheckInBackground(this, _settings, true)));

        var autoUpdate = Mi("Controlla all'avvio", Keys.None, null);
        autoUpdate.CheckOnClick = true;
        autoUpdate.Checked = _settings.CheckUpdates;
        autoUpdate.CheckedChanged += (s, e) => { _settings.CheckUpdates = autoUpdate.Checked; _settings.Save(); };
        help.DropDownItems.Add(autoUpdate);

        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Mi("Informazioni su DraftLite", Keys.None, (s, e) => ShowAbout()));
        menu.Items.Add(help);

        return menu;
    }

    private Ribbon _ribbon;
    private ComboBox _cboElement;
    private ComboBox _cboFont;
    private ComboBox _cboZoom;

    /// <summary>Simboli che servono spesso e che sulla tastiera non ci sono.</summary>
    private static readonly (string Name, string Char)[] Symbols =
    {
        ("Trattino lungo  —", "—"),
        ("Trattino medio  –", "–"),
        ("Puntini  …", "…"),
        ("Virgolette basse  « »", "«»"),
        ("Virgolette alte  “ ”", "“”"),
        ("Apostrofo curvo  ’", "’"),
        ("Grado  °", "°"),
        ("Euro  €", "€"),
        ("Paragrafo  §", "§"),
        ("Spazio unificatore", " ")
    };

    private void FillSymbolMenu(ToolStripDropDownItem parent) => FillSymbols(parent.DropDownItems);

    private void FillSymbolMenu(ContextMenuStrip menu) => FillSymbols(menu.Items);

    private void FillSymbols(ToolStripItemCollection items)
    {
        items.Clear();
        foreach (var (name, ch) in Symbols)
        {
            var captured = ch;
            var item = new ToolStripMenuItem(name);
            item.Click += (s, e) => { _editor.InsertText(captured); SetDirty(true); };
            items.Add(item);
        }
    }

    // ------------------------------------------------------------- costruzione barra

    private RibbonButton Big(string key, string text, string tip, EventHandler onClick)
    {
        var b = new RibbonButton { IconKey = key, Text = text, Big = true, Height = RibbonGroup.ItemArea };
        if (onClick != null) b.Click += onClick;
        if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(b, tip);
        _ribbon.Register(key, b);
        return b;
    }

    private RibbonButton Small(string key, string text, string tip, EventHandler onClick)
    {
        var b = new RibbonButton { IconKey = key, Text = text, Height = 22 };
        if (onClick != null) b.Click += onClick;
        if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(b, tip);
        _ribbon.Register(key, b);
        return b;
    }

    /// <summary>Pulsantino con la sola icona, per le righe compatte (grassetto, corsivo...).</summary>
    private RibbonButton IconBtn(string key, string tip, EventHandler onClick)
    {
        var b = new RibbonButton { IconKey = key, Text = string.Empty, Height = 22, Width = 26 };
        if (onClick != null) b.Click += onClick;
        if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(b, tip);
        _ribbon.Register(key, b);
        return b;
    }

    /// <summary>Pulsante con la tendina dei colori attaccata.</summary>
    private RibbonButton ColorButton(string key, string tip, bool highlight)
    {
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        FillColorMenu2(menu, highlight);
        var b = new RibbonButton
        {
            IconKey = key,
            Text = string.Empty,
            Height = 22,
            Width = 34,
            HasArrow = true,
            DropDown = menu
        };
        _tips.SetToolTip(b, tip);
        _ribbon.Register(key, b);
        return b;
    }

    private RibbonButton MenuButton(string key, string text, string tip, bool big, Action<ContextMenuStrip> fill)
    {
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        fill(menu);
        var b = new RibbonButton
        {
            IconKey = key,
            Text = text,
            Big = big,
            Height = big ? RibbonGroup.ItemArea : 22,
            HasArrow = true,
            DropDown = menu
        };
        if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(b, tip);
        _ribbon.Register(key, b);
        return b;
    }

    private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 9000, InitialDelay = 500, ReshowDelay = 120 };

    /// <summary>La tavolozza dentro un ContextMenuStrip (la versione per i menu sta in FillColorMenu).</summary>
    private void FillColorMenu2(ContextMenuStrip menu, bool highlight)
    {
        menu.Items.Clear();
        foreach (var (name, hex) in highlight ? HighlightPalette : TextPalette)
        {
            var item = new ToolStripMenuItem(name) { Image = Swatch(hex), ImageScaling = ToolStripItemImageScaling.None };
            var captured = hex;
            item.Click += (s, e) =>
            {
                var color = CardsForm.ParseColor(captured);
                if (highlight) _editor.ApplyHighlight(color);
                else _editor.ApplyTextColor(color);
                _editor.Focus();
                SetDirty(true);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("Altro colore...");
        custom.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (highlight) _editor.ApplyHighlight(dlg.Color);
            else _editor.ApplyTextColor(dlg.Color);
            _editor.Focus();
            SetDirty(true);
        };
        menu.Items.Add(custom);
    }

    /// <summary>
    /// La barra multifunzione: cinque linguette con i comandi raggruppati come nei
    /// programmi di scrittura veri. Tutto quello che c'e' qui sta anche nei menu,
    /// con le stesse scorciatoie: la barra e' solo la strada piu' corta.
    /// </summary>
    private Ribbon BuildRibbon()
    {
        _ribbon = new Ribbon();

        BuildTabFile(_ribbon.AddTab("File"));
        BuildTabHome(_ribbon.AddTab("Home"));
        BuildTabInsert(_ribbon.AddTab("Inserisci"));
        BuildTabFormat(_ribbon.AddTab("Formato"));
        BuildTabView(_ribbon.AddTab("Vista"));
        BuildTabEdit(_ribbon.AddTab("Modifica"));

        _ribbon.Commit();
        _ribbon.Select(1);          // si parte da Home
        return _ribbon;
    }

    private void BuildTabFile(RibbonTab tab)
    {
        var doc = tab.AddGroup("Documento");
        doc.Add(Big("new", "Nuovo", "Copione nuovo (Ctrl+N)",
            (s, e) => { if (ConfirmDiscard()) NewDocument(); }));
        doc.Add(Big("open", "Apri", "Apri un copione (Ctrl+O)", (s, e) => OpenDialog()));
        doc.Add(Big("save", "Salva", "Salva il copione (Ctrl+S)", (s, e) => Save()));
        doc.Add(Small("saveas", "Salva con nome...", "Salva una copia (Ctrl+Shift+S)", (s, e) => SaveAs()));
        doc.Add(Small("import", "Importa da Word o RTF...", "Porta dentro un testo gia' scritto", (s, e) => ImportDialog()));
        doc.Add(Small("print", "Anteprima PDF...", "Esporta e apri il PDF (Ctrl+P)", (s, e) => ExportPdf()));

        var exp = tab.AddGroup("Esporta");
        exp.Add(Big("pdf", "PDF", "Esporta il copione impaginato (Ctrl+P)", (s, e) => ExportPdf()));
        exp.Add(Big("sides", "Sides", "PDF con le sole scene di un personaggio", (s, e) => ExportSides()));
        exp.Add(Small("stats-pdf", "Report statistiche...", "Statistiche in PDF", (s, e) => ExportReport()));
        exp.Add(Small("export", "Fountain (.fountain)...", "Esporta in Fountain", (s, e) => ExportAs(DocFormat.Fountain)));
        exp.Add(Small("export2", "Final Draft (.fdx)...", "Esporta per Final Draft", (s, e) => ExportAs(DocFormat.Fdx)));

        var app = tab.AddGroup("Programma");
        app.Add(Small("update", "Cerca aggiornamenti", "Controlla se c'e' una versione nuova",
            (s, e) => UpdateChecker.CheckInBackground(this, _settings, true)));
        app.Add(Small("help", "Scorciatoie (F1)", "Tutti i tasti", (s, e) => ShowShortcuts()));
        app.Add(Small("settings", "Informazioni", "Versione e contatti", (s, e) => ShowAbout()));
    }

    private void BuildTabHome(RibbonTab tab)
    {
        // ---- appunti
        var clip = tab.AddGroup("Appunti");
        clip.Add(Big("paste", "Incolla", "Incolla riconoscendo gli elementi (Ctrl+V)",
            (s, e) => { _editor.PasteFromClipboard(); _editor.Focus(); }));
        clip.Add(Small("cut", "Taglia", "Ctrl+X", (s, e) => { _editor.Cut(); _editor.Focus(); }));
        clip.Add(Small("copy", "Copia", "Ctrl+C", (s, e) => { _editor.Copy(); _editor.Focus(); }));
        clip.Add(Small("delete", "Elimina", "Cancella la selezione", (s, e) => DeleteSelection()));

        // ---- carattere
        var font = tab.AddGroup("Carattere");
        var row1 = new RibbonRow();
        row1.Add(IconBtn("bold", "Grassetto (Ctrl+B)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Bold); _editor.Focus(); SetDirty(true); }));
        row1.Add(IconBtn("italic", "Corsivo (Ctrl+I)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Italic); _editor.Focus(); SetDirty(true); }));
        row1.Add(IconBtn("underline", "Sottolineato (Ctrl+U)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Underline); _editor.Focus(); SetDirty(true); }));
        row1.Add(IconBtn("case", "MAIUSCOLO / minuscolo / Iniziali (Shift+F3)",
            (s, e) => { _editor.CycleCase(); SetDirty(true); _editor.Focus(); }));
        row1.Add(ColorButton("textcolor", "Colore del testo", false));
        row1.Add(ColorButton("highlight", "Evidenziatore", true));
        font.Add(row1);

        _cboFont = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 170,
            Height = 22,
            Font = new Font("Segoe UI", 8.25f)
        };
        foreach (var f in AppearanceForm.MonospaceFamilies()) _cboFont.Items.Add(f);
        _cboFont.SelectedIndexChanged += (s, e) =>
        {
            if (_loading || _cboFont.SelectedItem == null) return;
            _settings.FontFamily = _cboFont.SelectedItem.ToString();
            _editor.SetEditorFont(_settings.FontFamily);
            _settings.Save();
            LayoutPage();
        };
        font.Add(_cboFont);

        var row2 = new RibbonRow();
        row2.Add(IconBtn("revert", "Ripristina il paragrafo: via stili e colori messi a mano",
            (s, e) => { _editor.RevertParagraph(); SetDirty(true); _editor.Focus(); }));
        row2.Add(IconBtn("appearance", "Aspetto: carattere, tema, colori degli elementi",
            (s, e) => ShowAppearance()));
        row2.Add(IconBtn("zoomout", "Riduci (Ctrl+-)", (s, e) => Zoom(-0.1f)));
        row2.Add(IconBtn("zoomin", "Ingrandisci (Ctrl++)", (s, e) => Zoom(0.1f)));
        font.Add(row2);

        // ---- elementi
        var els = tab.AddGroup("Elementi");
        _cboElement = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            Height = 22,
            Font = new Font("Segoe UI", 8.25f)
        };
        foreach (var st in ElementStyle.All) _cboElement.Items.Add(st.Name);
        _cboElement.SelectedIndexChanged += (s, e) =>
        {
            if (_loading || _cboElement.SelectedIndex < 0) return;
            var type = ElementStyle.All.ElementAt(_cboElement.SelectedIndex).Type;
            if (type != _editor.CurrentType) { _editor.ApplyType(type); _editor.Focus(); }
        };
        els.Add(_cboElement);
        els.Add(Small("dual", "Dialogo simultaneo", "Affianca questa battuta alla precedente (Ctrl+D)",
            (s, e) => { _editor.ToggleDual(); _editor.Focus(); SetDirty(true); }));
        els.Add(MenuButton("character", "Estensione personaggio", "(V.O.), (F.C.), (CONT'D)", false, m =>
        {
            foreach (var x in ScreenplayEditor.CharacterExtensions)
            {
                var captured = x;
                var it = new ToolStripMenuItem(x);
                it.Click += (s2, e2) => { _editor.AppendCharacterExtension(captured); _editor.Focus(); SetDirty(true); };
                m.Items.Add(it);
            }
        }));

        // ---- inserisci
        var ins = tab.AddGroup("Inserisci");
        ins.Add(Big("note", "Nota", "Appunto ancorato all'elemento (Ctrl+M)", (s, e) => EditNote()));
        ins.Add(Big("titlepage", "Frontespizio", "Titolo, autore, contatti (F7)", (s, e) => EditTitlePage()));
        ins.Add(Small("pagebreak", "Interruzione di pagina", "Forza il salto pagina (Ctrl+Invio)",
            (s, e) => InsertPageBreak()));
        ins.Add(Small("scene", "Nuova scena", "Apre un'intestazione di scena (Ctrl+Shift+N)",
            (s, e) => { _editor.InsertElement(ElementType.SceneHeading, string.Empty); SetDirty(true); }));
        ins.Add(MenuButton("symbol", "Simboli", "Trattini, virgolette, puntini", false, FillSymbolMenu));

        // ---- viste
        var views = tab.AddGroup("Viste");
        views.Add(Big("cards", "Schede scena", "Bacheca delle scene (F6)", (s, e) => ShowCards()));
        views.Add(Big("stats", "Statistiche", "Pagine, scene, battute (F8)", (s, e) => ShowReport()));
        views.Add(Toggle("navigator", "Pannello scene", "Mostra o nascondi il pannello laterale (F9)",
            (s, e) => { if (_navItem != null) _navItem.Checked = !_navItem.Checked; }));
        views.Add(Small("castlist", "Elenco personaggi", "Chi parla, quanto, da quando (F4)", (s, e) => ShowCastList()));
        views.Add(Toggle("typewriter", "Macchina da scrivere", "Tiene la riga al centro (F11)",
            (s, e) => { if (_typewriterItem != null) _typewriterItem.Checked = !_typewriterItem.Checked; }));
    }

    private void BuildTabInsert(RibbonTab tab)
    {
        var note = tab.AddGroup("Note");
        note.Add(Big("note", "Nota", "Appunto ancorato all'elemento (Ctrl+M)", (s, e) => EditNote()));
        note.Add(Small("bookmark", "Vai alle note", "Apre l'elenco delle note nel pannello laterale",
            (s, e) => ShowSidePanel(1)));
        note.Add(Small("delete", "Togli la nota", "Cancella la nota dell'elemento corrente",
            (s, e) => { _editor.SetNoteAtCaret(null); RefreshNotes(); SetDirty(true); _editor.Focus(); }));

        var el = tab.AddGroup("Elementi");
        el.Add(Big("scene", "Nuova scena", "Intestazione di scena nuova (Ctrl+Shift+N)",
            (s, e) => { _editor.InsertElement(ElementType.SceneHeading, string.Empty); SetDirty(true); }));
        el.Add(Big("character", "Personaggio", "Nuovo blocco di dialogo",
            (s, e) => { _editor.InsertElement(ElementType.Character, string.Empty); SetDirty(true); }));
        el.Add(MenuButton("elements", "Altro elemento", "Azione, parentetica, transizione...", true, m =>
        {
            foreach (var st in ElementStyle.All)
            {
                var type = st.Type;
                var it = new ToolStripMenuItem(st.Name);
                it.Click += (s2, e2) => { _editor.InsertElement(type, string.Empty); SetDirty(true); };
                m.Items.Add(it);
            }
        }));

        var page = tab.AddGroup("Pagina");
        page.Add(Big("pagebreak", "Interruzione di pagina", "Il resto va a pagina nuova (Ctrl+Invio)",
            (s, e) => InsertPageBreak()));
        page.Add(Small("numbers", "Blocca i numeri di scena", "Le nuove scene diventano 12A, 12B...",
            (s, e) => LockSceneNumbers(true)));
        page.Add(Small("numbers2", "Sblocca i numeri", "Torna a 1, 2, 3...", (s, e) => LockSceneNumbers(false)));
        page.Add(Small("revision", "Revisione...", "Colore bozza e asterischi", (s, e) => ShowRevision()));

        var sym = tab.AddGroup("Simboli");
        sym.Add(MenuButton("symbol", "Simbolo", "Trattini, virgolette, puntini", true, FillSymbolMenu));
        sym.Add(Big("titlepage", "Frontespizio", "Titolo, autore, contatti (F7)", (s, e) => EditTitlePage()));
    }

    private void BuildTabFormat(RibbonTab tab)
    {
        var els = tab.AddGroup("Elementi");
        els.Add(MenuButton("elements", "Tipo di elemento", "Cambia il tipo del paragrafo (Ctrl+1...Ctrl+6)", true, m =>
        {
            int n = 1;
            foreach (var st in ElementStyle.All)
            {
                var type = st.Type;
                var it = new ToolStripMenuItem(st.Name + "\tCtrl+" + n);
                it.Click += (s2, e2) => { _editor.ApplyType(type); _editor.Focus(); };
                m.Items.Add(it);
                n++;
            }
        }));
        els.Add(Big("dual", "Dialogo simultaneo", "Due colonne nel PDF (Ctrl+D)",
            (s, e) => { _editor.ToggleDual(); _editor.Focus(); SetDirty(true); }));
        els.Add(Small("settings", "Impostazioni elementi...", "Colori e carattere di ogni elemento",
            (s, e) => ShowAppearance()));
        els.Add(Small("castlist", "Elenco personaggi...", "Chi parla, quanto, da quando (F4)", (s, e) => ShowCastList()));
        els.Add(Small("rename", "Rinomina personaggio...", "In tutto il copione", (s, e) => RenameCharacter(null)));

        var text = tab.AddGroup("Testo");
        var r1 = new RibbonRow();
        r1.Add(IconBtn("bold", "Grassetto (Ctrl+B)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Bold); _editor.Focus(); SetDirty(true); }));
        r1.Add(IconBtn("italic", "Corsivo (Ctrl+I)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Italic); _editor.Focus(); SetDirty(true); }));
        r1.Add(IconBtn("underline", "Sottolineato (Ctrl+U)",
            (s, e) => { _editor.ToggleStyle(FontStyle.Underline); _editor.Focus(); SetDirty(true); }));
        r1.Add(IconBtn("case", "MAIUSCOLO / minuscolo / Iniziali (Shift+F3)",
            (s, e) => { _editor.CycleCase(); SetDirty(true); _editor.Focus(); }));
        text.Add(r1);

        var r2 = new RibbonRow();
        r2.Add(ColorButton("textcolor", "Colore del testo", false));
        r2.Add(ColorButton("highlight", "Evidenziatore", true));
        r2.Add(IconBtn("revert", "Ripristina il paragrafo",
            (s, e) => { _editor.RevertParagraph(); SetDirty(true); _editor.Focus(); }));
        text.Add(r2);
        text.Add(Small("font", "Carattere...", "Scegli il carattere dell'editor", (s, e) => ShowAppearance()));

        var par = tab.AddGroup("Paragrafo");
        par.Add(Small("align-left", "Rientri dell'elemento", "Li decide il tipo: e' lo standard della sceneggiatura",
            (s, e) => ShowElementInfo()));
        par.Add(Small("spacing", "Spaziatura", "Anche questa segue lo standard", (s, e) => ShowElementInfo()));
        par.Add(Small("revert", "Ripristina il paragrafo", "Via stili e colori messi a mano",
            (s, e) => { _editor.RevertParagraph(); SetDirty(true); _editor.Focus(); }));
        par.Add(Toggle("ruler", "Righello", "Mostra o nascondi il righello",
            (s, e) => { if (_rulerItem != null) _rulerItem.Checked = !_rulerItem.Checked; }));

        var rev = tab.AddGroup("Revisione");
        rev.Add(Big("revision", "Revisione", "Colore della bozza e asterischi ai margini", (s, e) => ShowRevision()));
        rev.Add(Small("numbers", "Blocca i numeri di scena", "12A, 12B per le scene nuove", (s, e) => LockSceneNumbers(true)));
        rev.Add(Small("numbers2", "Sblocca i numeri", "Torna a 1, 2, 3...", (s, e) => LockSceneNumbers(false)));
        rev.Add(Small("sides", "Sides personaggio...", "PDF con le sole scene di uno", (s, e) => ExportSides()));
    }

    private void BuildTabView(RibbonTab tab)
    {
        var views = tab.AddGroup("Viste");
        views.Add(Big("script", "Copione", "Torna alla pagina", (s, e) => _editor.Focus()));
        views.Add(Big("cards", "Schede scena", "Bacheca delle scene (F6)", (s, e) => ShowCards()));
        views.Add(Big("sceneview", "Elenco scene", "Il pannello con tutte le scene", (s, e) => ShowSidePanel(0)));

        var show = tab.AddGroup("Mostra");
        show.Add(Toggle("navigator", "Pannello laterale", "Scene e note (F9)",
            (s, e) => { if (_navItem != null) _navItem.Checked = !_navItem.Checked; }));
        show.Add(Toggle("ruler", "Righello", "I pollici sopra la pagina",
            (s, e) => { if (_rulerItem != null) _rulerItem.Checked = !_rulerItem.Checked; }));
        show.Add(Toggle("breaks", "Interruzioni di pagina", "La riga tratteggiata dove cade la pagina",
            (s, e) => { if (_breaksItem != null) _breaksItem.Checked = !_breaksItem.Checked; }));
        show.Add(Toggle("typewriter", "Macchina da scrivere", "Tiene la riga al centro (F11)",
            (s, e) => { if (_typewriterItem != null) _typewriterItem.Checked = !_typewriterItem.Checked; }));
        show.Add(Toggle("askelement", "Chiedi l'elemento a ogni a capo", "Il menu che compare premendo Invio",
            (s, e) => { if (_askElementItem != null) _askElementItem.Checked = !_askElementItem.Checked; }));
        show.Add(Small("note", "Note del copione", "Apre l'elenco delle note", (s, e) => ShowSidePanel(1)));

        var zoom = tab.AddGroup("Zoom");
        zoom.Add(Small("zoom", "Dimensioni foglio...", "Cambia larghezza e margini in centimetri", (s, e) => EditPageLayout()));
        zoom.Add(Small("zoom", "Adatta alla finestra", "Larghezza automatica (Ctrl+9). Trascina il bordo destro del foglio per regolarla.", (s, e) => FitPage()));
        zoom.Add(Big("zoom", "Zoom 100%", "Riporta la pagina alla misura giusta (Ctrl+0)", (s, e) => SetZoom(1f)));
        zoom.Add(Small("zoomin", "Ingrandisci", "Ctrl + +", (s, e) => Zoom(0.1f)));
        zoom.Add(Small("zoomout", "Riduci", "Ctrl + -", (s, e) => Zoom(-0.1f)));

        _cboZoom = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 80,
            Height = 22,
            Font = new Font("Segoe UI", 8.25f)
        };
        foreach (var z in new[] { "75%", "90%", "100%", "110%", "125%", "150%", "175%", "200%" })
            _cboZoom.Items.Add(z);
        _cboZoom.SelectedIndexChanged += (s, e) =>
        {
            if (_loading || _cboZoom.SelectedItem == null) return;
            var txt = _cboZoom.SelectedItem.ToString().TrimEnd('%');
            if (int.TryParse(txt, out var pct)) SetZoom(pct / 100f);
        };
        zoom.Add(_cboZoom);

        var look = tab.AddGroup("Aspetto");
        var themeRow1 = new RibbonRow();
        themeRow1.Add(Toggle("paper", "Carta", "Avorio riposante: il tema di partenza", (s, e) => SetTheme("Carta")));
        themeRow1.Add(Toggle("day", "Chiaro", "Bianco pieno", (s, e) => SetTheme("Chiaro")));
        look.Add(themeRow1);

        var themeRow2 = new RibbonRow();
        themeRow2.Add(Toggle("sepia", "Seppia", "Tonalita' calde", (s, e) => SetTheme("Seppia")));
        themeRow2.Add(Toggle("night", "Scuro", "Per scrivere di notte", (s, e) => SetTheme("Scuro")));
        look.Add(themeRow2);

        look.Add(Big("appearance", "Aspetto", "Carattere, tema, colori di ogni elemento", (s, e) => ShowAppearance()));
    }

    private void BuildTabEdit(RibbonTab tab)
    {
        var undo = tab.AddGroup("Annulla");
        undo.Add(Big("undo", "Annulla", "Ctrl+Z", (s, e) => { _editor.Undo(); _editor.Focus(); }));
        undo.Add(Big("redo", "Ripristina", "Ctrl+Y", (s, e) => { _editor.Redo(); _editor.Focus(); }));

        var clip = tab.AddGroup("Appunti");
        clip.Add(Small("cut", "Taglia", "Ctrl+X", (s, e) => { _editor.Cut(); _editor.Focus(); }));
        clip.Add(Small("copy", "Copia", "Ctrl+C", (s, e) => { _editor.Copy(); _editor.Focus(); }));
        clip.Add(Small("paste", "Incolla", "Ctrl+V", (s, e) => { _editor.PasteFromClipboard(); _editor.Focus(); }));

        var sel = tab.AddGroup("Seleziona");
        sel.Add(Small("selectall", "Seleziona tutto", "Ctrl+A", (s, e) => { _editor.SelectAll(); _editor.Focus(); }));
        sel.Add(Small("selectscene", "Seleziona la scena", "Dall'intestazione alla scena dopo (Ctrl+Shift+A)",
            (s, e) => _editor.SelectScene()));
        sel.Add(Small("delete", "Elimina", "Cancella la selezione", (s, e) => DeleteSelection()));

        var find = tab.AddGroup("Trova");
        find.Add(Big("find", "Trova e sostituisci", "Ctrl+F", (s, e) => ShowFind()));
        find.Add(Big("goto", "Vai a", "Salta a una scena o a una pagina (Ctrl+G)", (s, e) => GoTo()));
        find.Add(Small("rename", "Rinomina personaggio...", "In tutto il copione", (s, e) => RenameCharacter(null)));
        find.Add(Small("castlist", "Elenco personaggi...", "F4", (s, e) => ShowCastList()));

        var rev = tab.AddGroup("Revisione");
        rev.Add(Big("revision", "Revisione", "Colore bozza e asterischi", (s, e) => ShowRevision()));
        rev.Add(Small("track", "Righe cambiate", "Quante righe sono cambiate dall'ultima bozza",
            (s, e) => ShowRevisionSummary()));
        rev.Add(Small("numbers", "Numeri di scena", "Blocca la numerazione", (s, e) => LockSceneNumbers(true)));
        rev.Add(Small("stats", "Statistiche", "F8", (s, e) => ShowReport()));
    }

    /// <summary>Pulsante piccolo a interruttore (resta acceso quando la cosa e' attiva).</summary>
    private RibbonButton Toggle(string key, string text, string tip, EventHandler onClick)
    {
        var b = Small(key, text, tip, onClick);
        b.IsToggle = true;
        return b;
    }

    private void WireEvents()
    {
        _editor.TextChanged += (s, e) => { if (!_loading) { SetDirty(true); MarkStats(); } };
        _editor.CurrentTypeChanged += (s, e) => UpdateStatus();
        _editor.DocumentIndexed += (s, e) => { RefreshScenes(); RefreshNotes(); };

        _pageHost.Resize += (s, e) => LayoutPage();
        _pageHost.MouseDown += (s, e) => _editor.Focus();
        _pageHost.Paint += PageHost_Paint;
        _page.Paint += Page_Paint;
        _pageHost.Scroll += (s, e) => LayoutPage();
        _page.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left || e.X < _page.Width - 10) return;
            _resizingPage = true;
            _resizeStartX = Cursor.Position.X;
            _resizeStartZoom = _editor.ZoomFactor;
            _page.Capture = true;
        };
        _page.MouseMove += (s, e) =>
        {
            _page.Cursor = _resizingPage || e.X >= _page.Width - 10 ? Cursors.SizeWE : Cursors.Default;
            if (_resizingPage)
                SetZoom(_resizeStartZoom + (float)(2.0 * (Cursor.Position.X - _resizeStartX) /
                    (_editor.DocumentLayout.PaperWidth * _editor.DeviceDpi)));
        };
        _page.MouseUp += (s, e) => { _resizingPage = false; _page.Capture = false; };
        _page.MouseCaptureChanged += (s, e) => { if (!_page.Capture) _resizingPage = false; };
        _editor.DpiChangedAfterParent += (s, e) => LayoutPage();
        _editor.VScroll += (s, e) => _page.Invalidate();
        _editor.Resize += (s, e) => _page.Invalidate();
        _editor.CaretMoved += (s, e) => { if (_settings.ShowPageBreaks) _page.Invalidate(); };

        _sceneList.SelectedIndexChanged += (s, e) =>
        {
            if (_loading) return;
            int i = _sceneList.SelectedIndex;
            if (i >= 0 && i < _scenes.Count) _editor.GoToCharIndex(_scenes[i].CharIndex, false);
        };
        _sceneList.DoubleClick += (s, e) =>
        {
            int i = _sceneList.SelectedIndex;
            if (i >= 0 && i < _scenes.Count) _editor.GoToCharIndex(_scenes[i].CharIndex, true);
        };
        _sceneList.MouseDown += SceneList_MouseDown;
        _sceneList.MouseMove += SceneList_MouseMove;
        _sceneList.MouseUp += (s, e) => _dragIndex = -1;
        _sceneList.DragOver += (s, e) =>
            e.Effect = e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
        _sceneList.DragLeave += (s, e) => _dragIndex = -1;
        _sceneList.DragDrop += SceneList_DragDrop;

        _noteList.SelectedIndexChanged += (s, e) =>
        {
            if (_loading) return;
            int i = _noteList.SelectedIndex;
            if (i >= 0 && i < _notes.Count) _editor.GoToCharIndex(_notes[i].CharIndex, false);
        };
        _noteList.DoubleClick += (s, e) =>
        {
            int i = _noteList.SelectedIndex;
            if (i >= 0 && i < _notes.Count)
            {
                _editor.GoToCharIndex(_notes[i].CharIndex, true);
                EditNote();
            }
        };

        _statsTimer.Tick += (s, e) => { _statsTimer.Stop(); RefreshStats(); };
        _autoSaveTimer.Tick += (s, e) => AutoSave();

        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) =>
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && ConfirmDiscard()) OpenFile(files[0]);
        };

        FormClosing += (s, e) =>
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            _settings.Zoom = _editor.ZoomFactor;
            _settings.Save();
            TryDeleteAutoSave();
        };

        Shown += (s, e) =>
        {
            try { _split.SplitterDistance = 260; } catch { }
            _ribbon?.ApplyTheme(_theme);
            _ribbon?.Relayout();
            LayoutPage();
            _editor.ApplyPageWidth();
            SyncRibbonToggles();
            _editor.Focus();

            // il controllo aggiornamenti parte staccato dall'avvio: se la rete
            // non c'e' o GitHub non risponde, l'applicazione non se ne accorge
            var delay = new System.Windows.Forms.Timer { Interval = 3000 };
            delay.Tick += (s2, e2) =>
            {
                delay.Stop();
                delay.Dispose();
                UpdateChecker.CheckInBackground(this, _settings, false);
            };
            delay.Start();
        };
    }

    /// <summary>Ombra morbida attorno al foglio: si capisce dove finisce la pagina.</summary>
    private void PageHost_Paint(object sender, PaintEventArgs e)
    {
        var r = _page.Bounds;
        if (r.Width <= 0) return;

        for (int i = 6; i >= 1; i--)
        {
            using var pen = new Pen(Color.FromArgb(Math.Max(4, 26 - i * 3), 0, 0, 0), 1f);
            e.Graphics.DrawRectangle(pen, r.X - i, r.Y - i, r.Width + i * 2 - 1, r.Height + i * 2 - 1);
        }
    }

    /// <summary>Numero di pagina e linea di stacco nel margine sinistro del foglio.</summary>
    private void Page_Paint(object sender, PaintEventArgs e)
    {
        if (!_settings.ShowPageBreaks || _pageBreaks.Count == 0) return;

        int top = _editor.Top;
        int height = _editor.Height;

        using var dash = new Pen(_theme.Rule, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var brush = new SolidBrush(_theme.Rule);

        foreach (var (charIndex, page) in _pageBreaks)
        {
            if (page <= 1 || charIndex < 0 || charIndex > _editor.TextLength) continue;

            var pt = _editor.GetPositionFromCharIndex(charIndex);
            int y = top + pt.Y;
            if (y < top - 2 || y > top + height) continue;

            int marginRight = Math.Max(10, _page.Padding.Left - 3);
            e.Graphics.DrawLine(dash, 4, y - 5, marginRight, y - 5);
            e.Graphics.DrawString(page.ToString(), _gutterFont, brush, 5, y - 11);
        }
    }

    private bool _fitPage = true;
    private bool _layingOutPage;
    private bool _resizingPage;
    private int _resizeStartX;
    private float _resizeStartZoom;

    private void EditPageLayout()
    {
        using var dialog = new PageLayoutForm(_editor.DocumentLayout);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _editor.ChangeDocumentLayout(dialog.LayoutResult);
        SetDirty(true);
        MarkStats();
        FitPage();
    }

    private void FitPage()
    {
        _fitPage = true;
        _pageHost.AutoScrollPosition = Point.Empty;
        LayoutPage();
    }

    private void LayoutPage()
    {
        if (_layingOutPage || _pageHost.ClientSize.Width <= 0) return;
        _layingOutPage = true;
        try
        {
            var layout = _editor.DocumentLayout;
            int available = Math.Max(1, _pageHost.ClientSize.Width - 24);
            if (_fitPage)
            {
                float zoom = (float)Math.Max(0.1, Math.Min(3,
                    (available - SystemInformation.VerticalScrollBarWidth) /
                    (layout.PaperWidth * _editor.DeviceDpi)));
                if (Math.Abs(_editor.ZoomFactor - zoom) > 0.001f)
                {
                    _editor.ZoomFactor = zoom;
                    _editor.ApplyPageWidth();
                }
            }
            float scale = _editor.DeviceDpi * _editor.ZoomFactor;
            _page.Padding = new Padding((int)Math.Round(layout.LeftMargin * scale), 18,
                (int)Math.Round(layout.RightMargin * scale) + SystemInformation.VerticalScrollBarWidth, 10);
            int w = (int)Math.Round(layout.PaperWidth * scale) + SystemInformation.VerticalScrollBarWidth;
            int top = _settings.ShowRuler ? _ruler.Height : 0;
            _pageHost.AutoScrollMinSize = new Size(w + 24, 0);
            int x = Math.Max(12, (_pageHost.ClientSize.Width - w) / 2) + _pageHost.AutoScrollPosition.X;
            _page.Bounds = new Rectangle(x, top, w, Math.Max(0, _pageHost.ClientSize.Height - top));
            if (_settings.ShowRuler)
            {
                _ruler.Bounds = new Rectangle(0, 0, _pageHost.ClientSize.Width, _ruler.Height);
                _ruler.Update(x, w, x + _page.Padding.Left, scale,
                    _ruler.MarkerLeftInch, _ruler.MarkerRightInch);
            }
            if (_cboZoom != null)
            {
                bool saved = _loading;
                _loading = true;
                var value = ((int)Math.Round(_editor.ZoomFactor * 100)) + "%";
                if (!_cboZoom.Items.Contains(value)) _cboZoom.Items.Add(value);
                _cboZoom.SelectedItem = value;
                _loading = saved;
            }
            _pageHost.Invalidate();
        }
        finally { _layingOutPage = false; }
    }

    private void Zoom(float delta) => SetZoom(_editor.ZoomFactor + delta);

    private void SetZoom(float z)
    {
        _fitPage = false;
        _editor.ZoomFactor = Math.Max(0.1f, Math.Min(3f, z));
        _editor.ApplyPageWidth();
        LayoutPage();

        if (_cboZoom != null)
        {
            bool saved = _loading;
            _loading = true;
            try
            {
                var pct = (int)Math.Round(_editor.ZoomFactor * 100) + "%";
                _cboZoom.SelectedItem = _cboZoom.Items.Contains(pct) ? pct : null;
            }
            finally { _loading = saved; }
        }
    }

    private void ApplyAppearance()
    {
        _theme = Theme.ByName(_settings.ThemeName);
        _theme.ApplyTo(this);
        BackColor = _theme.Panel;
        _pageHost.BackColor = _theme.Desk;
        _page.BackColor = _theme.Paper;
        _sceneList.BackColor = _theme.Panel;
        _sceneList.ForeColor = _theme.PanelText;
        _noteList.BackColor = _theme.Panel;
        _noteList.ForeColor = _theme.PanelText;

        _editor.SetColors(_theme.Paper, _theme.Ink, _theme.NoteBack);

        var colors = _theme.ElementColors;
        foreach (var kv in _settings.ElementColors)
        {
            if (!Enum.TryParse<ElementType>(kv.Key, out var type)) continue;
            var c = CardsForm.ParseColor(kv.Value);
            if (c.HasValue) colors[type] = c.Value;
        }
        _editor.SetElementColors(colors);
        if (!string.Equals(_editor.Font.Name, _settings.FontFamily, StringComparison.OrdinalIgnoreCase))
            _editor.SetEditorFont(_settings.FontFamily);

        _ribbon?.ApplyTheme(_theme);
        _ruler.ApplyTheme(_theme);
        _ruler.Visible = _settings.ShowRuler;

        _editor.TypewriterMode = _settings.Typewriter;
        _editor.AskElementOnEnter = _settings.AskElementOnEnter;

        SyncRibbonToggles();
        LayoutPage();
        _pageHost.Invalidate();
        _page.Invalidate();
    }

    /// <summary>Allinea le spunte della barra a quello che dicono le impostazioni.</summary>
    private void SyncRibbonToggles()
    {
        if (_ribbon == null) return;

        _ribbon.SetChecked("navigator", _settings.ShowNavigator);
        _ribbon.SetChecked("ruler", _settings.ShowRuler);
        _ribbon.SetChecked("typewriter", _settings.Typewriter);
        _ribbon.SetChecked("breaks", _settings.ShowPageBreaks);
        _ribbon.SetChecked("askelement", _settings.AskElementOnEnter);

        _ribbon.SetChecked("paper", _theme.Name == "Carta");
        _ribbon.SetChecked("day", _theme.Name == "Chiaro");
        _ribbon.SetChecked("sepia", _theme.Name == "Seppia");
        _ribbon.SetChecked("night", _theme.Name == "Scuro");

        bool saved = _loading;
        _loading = true;
        try
        {
            if (_cboFont != null && _cboFont.Items.Contains(_settings.FontFamily))
                _cboFont.SelectedItem = _settings.FontFamily;

            if (_cboZoom != null)
            {
                var pct = (int)Math.Round(_editor.ZoomFactor * 100) + "%";
                _cboZoom.SelectedItem = _cboZoom.Items.Contains(pct) ? pct : null;
            }
        }
        finally { _loading = saved; }
    }

    // ------------------------------------------------------------------ DOCUMENTO

    private void NewDocument()
    {
        _loading = true;
        try
        {
            _editor.DocumentLayout = new DocumentLayout();
            _editor.ApplyPageWidth();
            _titlePage = new TitlePage();
            _revision = new Revision();
            _editor.SetElements(new List<ScreenElement>
            {
                new ScreenElement(ElementType.SceneHeading, string.Empty)
            });
            _path = null;
            _format = DocFormat.DraftLite;
        }
        finally { _loading = false; }

        FitPage();
        SetDirty(false);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        UpdateStatus();
        _editor.Focus();
    }

    private PageSetup CurrentPageSetup()
    {
        var setup = _settings.ToPageSetup();
        var layout = _editor.DocumentLayout;
        setup.WidthPt = layout.PaperWidth * 72;
        setup.HeightPt = layout.PaperHeight * 72;
        setup.PaperName = Math.Abs(layout.PaperWidth - 8.5) < 0.01 && Math.Abs(layout.PaperHeight - 11) < 0.01
            ? "Letter" : "Documento";
        setup.LeftMarginInch = layout.LeftMargin;
        setup.TopMarginInch = layout.TopMargin;
        setup.LinesPerPage = Math.Max(10, (int)Math.Floor((layout.PaperHeight - layout.TopMargin - layout.BottomMargin) * 6));
        return setup;
    }

    private Screenplay CurrentScreenplay() => new Screenplay
    {
        Layout = _editor.DocumentLayout.Clone(),
        TitlePage = _titlePage,
        Revision = _revision,
        Elements = _editor.GetElements()
    };

    private void LoadScreenplay(Screenplay sp, string path, DocFormat format)
    {
        _loading = true;
        try
        {
            _editor.DocumentLayout = sp.Layout?.Clone() ?? new DocumentLayout();
            _editor.DocumentLayout.Normalize();
            _editor.ApplyPageWidth();
            _titlePage = sp.TitlePage ?? new TitlePage();
            _revision = sp.Revision ?? new Revision();
            _editor.SetElements(sp.Elements.Count > 0
                ? sp.Elements
                : new List<ScreenElement> { new ScreenElement(ElementType.SceneHeading, string.Empty) });
            _path = path;
            _format = format;
        }
        finally { _loading = false; }

        FitPage();
        SetDirty(path == null);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        UpdateStatus();
    }

    private void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Tutti i formati (*.dlite;*.fountain;*.spmd;*.fdx;*.fdxt;*.txt;*.docx;*.rtf)|" +
                     "*.dlite;*.fountain;*.spmd;*.fdx;*.fdxt;*.txt;*.docx;*.rtf|" +
                     "DraftLite (*.dlite)|*.dlite|" +
                     "Fountain (*.fountain;*.spmd;*.txt)|*.fountain;*.spmd;*.txt|" +
                     "Final Draft (*.fdx;*.fdxt)|*.fdx;*.fdxt|" +
                     "Word e RTF (*.docx;*.rtf)|*.docx;*.rtf|" +
                     "Tutti i file (*.*)|*.*",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    private void ImportDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Word, RTF e testo (*.docx;*.rtf;*.txt)|*.docx;*.rtf;*.txt|Tutti i file (*.*)|*.*",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            Screenplay sp;
            DocFormat format;
            string keepPath = path;

            switch (ext)
            {
                case ".fdx":
                case ".fdxt":
                    sp = FdxIO.Load(path);
                    format = DocFormat.Fdx;
                    break;
                case ".fountain":
                case ".spmd":
                    sp = FountainIO.Load(path);
                    format = DocFormat.Fountain;
                    break;
                case ".docx":
                case ".rtf":
                    sp = TextImporter.Load(path);
                    format = DocFormat.DraftLite;
                    keepPath = null;              // importato: si salva come .dlite
                    break;
                case ".txt":
                    sp = TextImporter.Load(path);
                    format = DocFormat.DraftLite;
                    keepPath = null;
                    break;
                default:
                    sp = DraftLiteFile.Load(path);
                    format = DocFormat.DraftLite;
                    break;
            }

            _settings.LastFolder = Path.GetDirectoryName(path) ?? string.Empty;
            LoadScreenplay(sp, keepPath, format);

            if (keepPath == null)
                MessageBox.Show(this,
                    "Documento importato.\n\nControlla che gli elementi siano quelli giusti, poi salvalo come .dlite.",
                    "DraftLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossibile aprire il file:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool Save()
    {
        if (string.IsNullOrEmpty(_path)) return SaveAs();
        try
        {
            _settings.MakeBackup(_path);

            var sp = CurrentScreenplay();
            switch (_format)
            {
                case DocFormat.Fountain: FountainIO.Save(_path, sp); break;
                case DocFormat.Fdx: FdxIO.Save(_path, sp); break;
                default: DraftLiteFile.Save(_path, sp); break;
            }
            SetDirty(false);
            TryDeleteAutoSave();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossibile salvare:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "DraftLite (*.dlite)|*.dlite|Fountain (*.fountain)|*.fountain|Final Draft (*.fdx)|*.fdx",
            FileName = SuggestedFileName(),
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        _path = dlg.FileName;
        _format = Path.GetExtension(_path).ToLowerInvariant() switch
        {
            ".fountain" => DocFormat.Fountain,
            ".fdx" => DocFormat.Fdx,
            _ => DocFormat.DraftLite
        };
        _settings.LastFolder = Path.GetDirectoryName(_path) ?? string.Empty;

        if (_format != DocFormat.DraftLite)
            MessageBox.Show(this,
                "Nota: colori delle schede e stato della revisione si conservano solo nel formato .dlite.",
                "DraftLite", MessageBoxButtons.OK, MessageBoxIcon.Information);

        return Save();
    }

    private void ExportAs(DocFormat fmt)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = fmt == DocFormat.Fountain ? "Fountain (*.fountain)|*.fountain" : "Final Draft (*.fdx)|*.fdx",
            FileName = Path.ChangeExtension(SuggestedFileName(), fmt == DocFormat.Fountain ? ".fountain" : ".fdx"),
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sp = CurrentScreenplay();
            if (fmt == DocFormat.Fountain) FountainIO.Save(dlg.FileName, sp);
            else FdxIO.Save(dlg.FileName, sp);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportPdf()
    {
        using var opt = new PdfOptionsForm(CurrentPageSetup());
        if (opt.ShowDialog(this) != DialogResult.OK || opt.Setup == null) return;
        _settings.FromPageSetup(opt.Setup);
        MarkStats();

        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.ChangeExtension(SuggestedFileName(), ".pdf"),
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.Export(dlg.FileName, CurrentScreenplay(), opt.Setup);
            OfferOpen(dlg.FileName, "PDF creato.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export PDF non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSides()
    {
        var sp = CurrentScreenplay();
        var characters = sp.CharacterNames();
        if (characters.Count == 0)
        {
            MessageBox.Show(this, "Nel copione non c'e' ancora nessun personaggio.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var pick = new SidesForm(characters);
        if (pick.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(pick.Character)) return;

        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SafeFileName("sides - " + pick.Character) + ".pdf",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.ExportSides(dlg.FileName, sp, pick.Character, CurrentPageSetup());
            OfferOpen(dlg.FileName, "Sides di " + pick.Character + " creati.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportReport()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SafeFileName("statistiche") + ".pdf",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.ExportReport(dlg.FileName, CurrentScreenplay(), CurrentPageSetup());
            OfferOpen(dlg.FileName, "Report creato.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OfferOpen(string path, string message)
    {
        if (MessageBox.Show(this, message + "\n\nAprirlo adesso?", "DraftLite",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static string SafeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Trim();
    }

    private string SuggestedFileName()
    {
        if (!string.IsNullOrEmpty(_path)) return Path.GetFileName(_path);
        var t = _titlePage.Title;
        if (string.IsNullOrWhiteSpace(t)) return "senza titolo.dlite";
        return SafeFileName(t) + ".dlite";
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show(this, "Il copione ha modifiche non salvate.\n\nSalvare prima di continuare?",
            "DraftLite", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return Save();
        return true;
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        var name = string.IsNullOrEmpty(_path) ? "senza titolo" : Path.GetFileName(_path);
        var rev = _revision != null && _revision.Active ? "  [bozza " + _revision.Color + "]" : "";
        Text = "DraftLite - " + name + (dirty ? " *" : "") + rev;
    }

    // ------------------------------------------------------------------ AUTOSALVATAGGIO

    private void AutoSave()
    {
        if (!_dirty || !_settings.AutoSave) return;
        try { DraftLiteFile.Save(AppSettings.AutoSavePath, CurrentScreenplay()); }
        catch { /* best effort */ }
    }

    private static void TryDeleteAutoSave()
    {
        try { if (File.Exists(AppSettings.AutoSavePath)) File.Delete(AppSettings.AutoSavePath); }
        catch { }
    }

    private void OfferAutoSaveRecovery()
    {
        try
        {
            if (!File.Exists(AppSettings.AutoSavePath)) return;
            var when = File.GetLastWriteTime(AppSettings.AutoSavePath);
            var r = MessageBox.Show(this,
                "C'e' un salvataggio automatico del " + when.ToString("dd/MM/yyyy HH:mm") +
                ".\n\nRecuperarlo?", "DraftLite", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                var sp = DraftLiteFile.Load(AppSettings.AutoSavePath);
                LoadScreenplay(sp, null, DocFormat.DraftLite);
                SetDirty(true);
            }
            else TryDeleteAutoSave();
        }
        catch { }
    }

    // ------------------------------------------------------------------ PANNELLI

    private void RefreshScenes()
    {
        _scenes = _editor.GetSceneIndex();

        bool savedLoading = _loading;
        _loading = true;
        try
        {
            int sel = _sceneList.SelectedIndex;
            _sceneList.BeginUpdate();
            _sceneList.Items.Clear();
            foreach (var s in _scenes)
                _sceneList.Items.Add(s.Number + ".  " + s.Text);
            _sceneList.EndUpdate();
            if (sel >= 0 && sel < _sceneList.Items.Count) _sceneList.SelectedIndex = sel;
        }
        finally { _loading = savedLoading; }

        UpdateStatus();
    }

    private void RefreshNotes()
    {
        _notes = _editor.GetNotes();

        bool savedLoading = _loading;
        _loading = true;
        try
        {
            _noteList.BeginUpdate();
            _noteList.Items.Clear();
            foreach (var n in _notes)
            {
                var head = string.IsNullOrWhiteSpace(n.Text) ? "(riga vuota)" : Truncate(n.Text, 28);
                _noteList.Items.Add(head + "  -  " + Truncate(n.Note.Replace("\n", " "), 40));
            }
            _noteList.EndUpdate();
        }
        finally { _loading = savedLoading; }

        _sideTabs.TabPages[1].Text = _notes.Count > 0 ? "Note (" + _notes.Count + ")" : "Note";
    }

    private Point _dragStart;
    private int _dragIndex = -1;

    private void SceneList_MouseDown(object sender, MouseEventArgs e)
    {
        _dragStart = e.Location;
        _dragIndex = _sceneList.IndexFromPoint(e.Location);
    }

    private void SceneList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragIndex < 0) return;
        if (Math.Abs(e.X - _dragStart.X) < 6 && Math.Abs(e.Y - _dragStart.Y) < 6) return;
        _sceneList.DoDragDrop(_dragIndex, DragDropEffects.Move);
    }

    private void SceneList_DragDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(typeof(int))) return;
            int from = (int)e.Data.GetData(typeof(int));
            if (from < 0 || from >= _scenes.Count) return;

            var pt = _sceneList.PointToClient(new Point(e.X, e.Y));
            int target = _sceneList.IndexFromPoint(pt);
            if (target < 0) target = _sceneList.Items.Count - 1;
            if (target == from) return;

            MoveScene(from, target);
        }
        finally { _dragIndex = -1; }
    }

    /// <summary>Divide il copione in blocchi: quello che sta prima della prima scena, poi una scena per blocco.</summary>
    private static List<List<ScreenElement>> SplitScenes(List<ScreenElement> els, out List<ScreenElement> head)
    {
        var starts = new List<int>();
        for (int i = 0; i < els.Count; i++)
        {
            var txt = els[i].Text.Trim();
            if (els[i].Type == ElementType.SceneHeading && txt.Length > 0 &&
                txt.Length <= 120 && txt == txt.ToUpperInvariant())
                starts.Add(i);
        }

        head = starts.Count > 0 ? els.Take(starts[0]).ToList() : new List<ScreenElement>(els);

        var blocks = new List<List<ScreenElement>>();
        for (int k = 0; k < starts.Count; k++)
        {
            int s = starts[k];
            int e = (k + 1 < starts.Count) ? starts[k + 1] : els.Count;
            blocks.Add(els.GetRange(s, e - s));
        }
        return blocks;
    }

    private void MoveScene(int from, int to)
    {
        var els = _editor.GetElements();
        var blocks = SplitScenes(els, out var head);
        if (from < 0 || from >= blocks.Count || to < 0 || to >= blocks.Count) return;

        var moved = blocks[from];
        blocks.RemoveAt(from);
        blocks.Insert(to, moved);

        var result = new List<ScreenElement>(head);
        foreach (var b in blocks) result.AddRange(b);

        _editor.SetElements(result, false);
        SetDirty(true);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        if (to >= 0 && to < _scenes.Count) _editor.GoToCharIndex(_scenes[to].CharIndex, false);
    }

    // ------------------------------------------------------------------ STATO

    private void MarkStats()
    {
        _statsDirty = true;
        _statsTimer.Stop();
        _statsTimer.Start();
    }

    private void RefreshStats()
    {
        if (!_statsDirty) return;
        try
        {
            var els = _editor.GetElements();

            // posizione di ogni paragrafo nel testo, per sapere dove cadono le pagine
            var offsets = new List<int>(els.Count);
            int pos = 0;
            foreach (var el in els)
            {
                offsets.Add(pos);
                int n = el.Text != null ? DraftLite.Model.StyledText.Plain(el.Text).Length : 0;
                // nel testo dell'editor la battuta simultanea finisce con " ^"
                if (el.Type == ElementType.Character && el.Dual) n += 2;
                pos += n + 1;
            }

            // Paginate lavora sugli elementi non vuoti: serve la corrispondenza con i paragrafi
            var compactedToParagraph = new List<int>();
            for (int k = 0; k < els.Count; k++)
                if (!els[k].IsEmpty) compactedToParagraph.Add(k);

            var sp = new Screenplay { Layout = _editor.DocumentLayout.Clone(), TitlePage = _titlePage, Revision = _revision, Elements = els };
            var pages = Paginator.Paginate(sp, CurrentPageSetup());

            var breaks = new List<(int, int)>();
            for (int p = 0; p < pages.Count; p++)
            {
                var first = pages[p].Lines.FirstOrDefault(l => l.ElementIndex >= 0);
                if (first == null) continue;
                if (first.ElementIndex >= compactedToParagraph.Count) continue;
                int paragraph = compactedToParagraph[first.ElementIndex];
                if (paragraph < offsets.Count) breaks.Add((offsets[paragraph], p + 1));
            }
            _pageBreaks = breaks;
            _page.Invalidate();

            int count = pages.Count;
            _lblPages.Text = count + (count == 1 ? " pagina" : " pagine") +
                             "   ~" + count + " min      " + _settings.Paper;
            _statsDirty = false;
        }
        catch { _lblPages.Text = string.Empty; }
    }

    private void UpdateStatus()
    {
        var type = _editor.CurrentType;
        _lblType.Text = "  " + ElementStyle.NameOf(type) +
                        (_editor.IsDualAtCaret ? "  (simultaneo)" : "");

        bool savedLoading = _loading;
        _loading = true;
        try
        {
            int idx = ElementStyle.All.ToList().FindIndex(x => x.Type == type);
            if (idx >= 0 && _cboElement != null && _cboElement.SelectedIndex != idx)
                _cboElement.SelectedIndex = idx;
        }
        finally { _loading = savedLoading; }

        int caret = _editor.SelectionStart;
        var scene = _scenes.LastOrDefault(s => s.CharIndex <= caret);
        _lblScene.Text = scene.Number > 0
            ? "Scena " + scene.Number + ": " + Truncate(scene.Text, 34)
            : "";

        RefreshRibbonState(type);
    }

    /// <summary>
    /// Accende i pulsanti che corrispondono a quello che c'e' sotto il cursore:
    /// grassetto, corsivo, sottolineato, dialogo simultaneo. E sposta gli indicatori
    /// del righello sui rientri dell'elemento corrente.
    /// </summary>
    private void RefreshRibbonState(ElementType type)
    {
        if (_ribbon == null) return;

        try
        {
            var (b, i, u) = _editor.CurrentStyles();
            _ribbon.SetChecked("bold", b);
            _ribbon.SetChecked("italic", i);
            _ribbon.SetChecked("underline", u);
            _ribbon.SetChecked("dual", _editor.IsDualAtCaret);
        }
        catch { /* lo stato dei pulsanti non vale un errore */ }

        if (_settings.ShowRuler)
        {
            var st = _editor.DocumentLayout.Style(type);
            _ruler.MarkerLeftInch = st.LeftInch;
            _ruler.MarkerRightInch = st.LeftInch + st.WidthInch;
            _ruler.Invalidate();
        }
    }

    private static string Truncate(string s, int n)
        => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n - 1) + "...";

    // ------------------------------------------------------------------ FUNZIONI

    private void EditTitlePage()
    {
        using var dlg = new TitlePageForm(_titlePage);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            _titlePage = dlg.Result;
            SetDirty(true);
        }
    }

    private void ShowReport()
    {
        using var dlg = new ReportForm(CurrentScreenplay(), CurrentPageSetup());
        dlg.ShowDialog(this);
    }

    private void ShowFind()
    {
        if (_findForm == null || _findForm.IsDisposed)
        {
            _findForm = new FindForm(_editor);
            _findForm.Owner = this;
        }
        _findForm.Show();
        _findForm.BringToFront();
    }

    private void EditNote()
    {
        var context = _editor.CurrentParagraphText;
        using var dlg = new NoteForm(_editor.NoteAtCaret, context);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _editor.SetNoteAtCaret(dlg.Deleted ? null : dlg.Note);
        RefreshNotes();
        SetDirty(true);
        _editor.Focus();
    }

    private void ShowAppearance()
    {
        using var dlg = new AppearanceForm(_settings.FontFamily, _settings.ThemeName,
                                           _settings.Typewriter, _settings.AskElementOnEnter,
                                           _settings.ShowPageBreaks, _editor.ZoomFactor,
                                           _settings.ElementColors);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settings.FontFamily = dlg.SelectedFont;
        _settings.ThemeName = dlg.SelectedTheme;
        _settings.Typewriter = dlg.Typewriter;
        _settings.AskElementOnEnter = dlg.AskElement;
        _settings.ShowPageBreaks = dlg.PageBreaks;
        _settings.ElementColors = dlg.CustomColors;
        _settings.Zoom = dlg.SelectedZoom;

        if (_typewriterItem != null) _typewriterItem.Checked = dlg.Typewriter;
        if (_askElementItem != null) _askElementItem.Checked = dlg.AskElement;
        if (_breaksItem != null) _breaksItem.Checked = dlg.PageBreaks;

        SetZoom(dlg.SelectedZoom);
        ApplyAppearance();
        _settings.Save();
    }

    private void ShowCards()
    {
        var els = _editor.GetElements();
        var blocks = SplitScenes(els, out _);
        // la numerazione si calcola sugli stessi blocchi che finiscono in bacheca,
        // altrimenti le etichette possono scivolare di una scena
        var numbers = new Screenplay { Elements = blocks.Select(b => b[0]).ToList() }.SceneNumbers();

        var cards = new List<CardsForm.CardData>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var headEl = blocks[i][0];
            cards.Add(new CardsForm.CardData
            {
                SceneIndex = i,
                Number = i < numbers.Count ? numbers[i] : (i + 1).ToString(),
                Heading = headEl.Text,
                Synopsis = headEl.Synopsis ?? string.Empty,
                Color = headEl.Color
            });
        }

        if (cards.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono ancora scene da mostrare.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new CardsForm(cards, _theme);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.GoToScene >= 0)
        {
            RefreshScenes();
            if (dlg.GoToScene < _scenes.Count) _editor.GoToCharIndex(_scenes[dlg.GoToScene].CharIndex, true);
            return;
        }

        if (dlg.Result == null) return;

        var head = new List<ScreenElement>();
        SplitScenes(els, out head);
        var result = new List<ScreenElement>(head);
        foreach (var card in dlg.Result)
        {
            if (card.SceneIndex < 0 || card.SceneIndex >= blocks.Count) continue;
            var block = blocks[card.SceneIndex];
            block[0].Synopsis = string.IsNullOrWhiteSpace(card.Synopsis) ? null : card.Synopsis.Trim();
            block[0].Color = card.Color;
            result.AddRange(block);
        }

        _editor.SetElements(result, false);
        SetDirty(true);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        _editor.Focus();
    }

    private void LockSceneNumbers(bool locked)
    {
        var sp = CurrentScreenplay();
        if (locked) sp.LockSceneNumbers();
        else sp.UnlockSceneNumbers();

        _editor.SetElements(sp.Elements, false);
        SetDirty(true);
        RefreshScenes();
        RefreshNotes();

        _settings.SceneNumbers = locked || _settings.SceneNumbers;
        MessageBox.Show(this,
            locked
                ? "Numerazione bloccata: le scene inserite d'ora in poi prendono le lettere (12A, 12B...)."
                : "Numerazione libera: le scene tornano a 1, 2, 3...",
            "DraftLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowRevision()
    {
        var sp = CurrentScreenplay();
        int changed = sp.Revision.MarkChanged(sp.Compacted()).Count(x => x);

        using var dlg = new RevisionForm(_revision, changed);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _revision.Color = dlg.ColorName;
        _revision.Date = dlg.RevisionDate;
        _revision.Active = dlg.Active;

        if (dlg.FreezeRequested)
            _revision.Snapshot = Revision.TakeSnapshot(CurrentScreenplay().Compacted());

        SetDirty(true);
        MarkStats();
    }

    // ------------------------------------------------------------------ COMANDI NUOVI (1.5)

    private void DeleteSelection()
    {
        if (_editor.SelectionLength == 0) return;
        _editor.DeleteSelection();
        SetDirty(true);
        _editor.Focus();
    }

    private void ShowSidePanel(int tab)
    {
        if (_navItem != null && !_navItem.Checked) _navItem.Checked = true;
        else _split.Panel1Collapsed = false;

        if (tab >= 0 && tab < _sideTabs.TabPages.Count) _sideTabs.SelectedIndex = tab;
        _sideTabs.Focus();
    }

    private void SetTheme(string name)
    {
        _settings.ThemeName = name;
        ApplyAppearance();
        _settings.Save();
        _editor.Focus();
    }

    /// <summary>Riga di soli "=" : da li' in poi si stampa su una pagina nuova.</summary>
    private void InsertPageBreak()
    {
        _editor.InsertElement(ElementType.Action, Screenplay.PageBreakMark);
        _editor.InsertElement(ElementType.Action, string.Empty);
        SetDirty(true);
        MarkStats();
    }

    private void GoTo()
    {
        RefreshScenes();
        RefreshStats();
        int pages = Math.Max(1, _pageBreaks.Count);
        using var dlg = new GoToForm(_scenes.Count, pages);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.ByScene)
        {
            int i = dlg.Number - 1;
            if (i < 0 || i >= _scenes.Count)
            {
                MessageBox.Show(this, "Quella scena non c'e'.", "DraftLite",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _editor.GoToCharIndex(_scenes[i].CharIndex, true);
            return;
        }

        var target = _pageBreaks.FirstOrDefault(b => b.Page == dlg.Number);
        if (dlg.Number <= 1) _editor.GoToCharIndex(0, true);
        else if (target.Page == dlg.Number) _editor.GoToCharIndex(target.CharIndex, true);
        else
            MessageBox.Show(this, "Quella pagina non c'e' ancora.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Cambia un nome di personaggio in tutto il copione: le righe Personaggio
    /// e, se richiesto, anche le sue comparse dentro azioni e dialoghi.
    /// </summary>
    private void RenameCharacter(string preselect)
    {
        var sp = CurrentScreenplay();
        var names = sp.CharacterNames();
        if (names.Count == 0)
        {
            MessageBox.Show(this, "Nel copione non c'e' ancora nessun personaggio.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new RenameCharacterForm(names, preselect);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var from = dlg.OldName;
        var to = dlg.NewName;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) ||
            string.Equals(from, to, StringComparison.Ordinal)) return;

        var els = _editor.GetElements();
        int changed = 0;

        foreach (var el in els)
        {
            if (el.Type == ElementType.Character)
            {
                var plain = StyledText.Plain(el.Text);
                var name = Screenplay.NormalizeCharacterName(plain);
                if (!string.Equals(name, from, StringComparison.OrdinalIgnoreCase)) continue;

                // il nome sta in testa: si sostituisce solo quello, le estensioni restano
                int at = plain.IndexOf(from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                el.Text = plain.Substring(0, at) + to + plain.Substring(at + from.Length);
                changed++;
            }
            else if (dlg.AlsoInText)
            {
                var replaced = ReplaceWord(el.Text, from, to);
                if (!ReferenceEquals(replaced, el.Text)) { el.Text = replaced; changed++; }
            }
        }

        if (changed == 0)
        {
            MessageBox.Show(this, "Non ho trovato niente da cambiare.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _editor.SetElements(els, false);
        SetDirty(true);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        _editor.Focus();
    }

    /// <summary>Sostituisce il nome solo quando e' parola intera (MARTA si', MARTANO no).</summary>
    private static string ReplaceWord(string text, string from, string to)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0)
            return text;

        var pattern = @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(from) + @"(?![\p{L}\p{N}])";

        // il nome nuovo va messo cosi' com'e': "$" nella sostituzione sarebbe un riferimento,
        // e *, _, { farebbero da marcatori di stile al prossimo giro di lettura
        var literal = to.Replace("$", "$$");
        foreach (var ch in new[] { "*", "_", "{" })
            literal = literal.Replace(ch, "\\" + ch);

        return System.Text.RegularExpressions.Regex.Replace(text, pattern, literal,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private void ShowCastList()
    {
        var stats = ScreenplayStats.Compute(CurrentScreenplay(), CurrentPageSetup());
        if (stats.Characters.Count == 0)
        {
            MessageBox.Show(this, "Nel copione non c'e' ancora nessun personaggio.", "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string rename = null, sides = null;
        using (var dlg = new CastListForm(stats, _theme))
        {
            dlg.ShowDialog(this);
            rename = dlg.RenameRequested;
            sides = dlg.SidesRequested;
        }

        if (rename != null) RenameCharacter(rename);
        else if (sides != null) ExportSidesFor(sides);
    }

    private void ExportSidesFor(string character)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SafeFileName("sides - " + character) + ".pdf",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.ExportSides(dlg.FileName, CurrentScreenplay(), character, CurrentPageSetup());
            OfferOpen(dlg.FileName, "Sides di " + character + " creati.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Quanto e' cambiato dall'ultima bozza congelata.</summary>
    private void ShowRevisionSummary()
    {
        var sp = CurrentScreenplay();
        int changed = sp.Revision.MarkChanged(sp.Compacted()).Count(x => x);

        MessageBox.Show(this,
            _revision.Active
                ? "Bozza " + _revision.Color + (string.IsNullOrWhiteSpace(_revision.Date) ? "" : " del " + _revision.Date) +
                  ".\r\n\r\nRighe cambiate dall'ultimo blocco: " + changed + ".\r\n" +
                  "Nel PDF hanno l'asterisco al margine."
                : "La revisione non e' attiva.\r\n\r\nAprila da Revisione per congelare la bozza\r\n" +
                  "e far comparire gli asterischi sulle righe cambiate.",
            "Revisione", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>I rientri non si toccano a mano: li decide il tipo di elemento.</summary>
    private void ShowElementInfo()
    {
        var st = _editor.DocumentLayout.Style(_editor.CurrentType);
        MessageBox.Show(this,
            st.Name + "\r\n\r\n" +
            "Rientro sinistro: " + st.LeftInch.ToString("0.##") + "\"\r\n" +
            "Larghezza: " + st.WidthInch.ToString("0.##") + "\"\r\n" +
            "Righe vuote prima: " + st.SpaceBeforeLines + "\r\n" +
            (st.UpperCase ? "Tutto maiuscolo\r\n" : "") +
            (st.RightAlign ? "Allineato a destra\r\n" : "") +
            "\r\nQuesti valori sono lo standard della sceneggiatura: valgono uguali\r\n" +
            "a video e nel PDF, e non vanno cambiati a mano.\r\n" +
            "Colore e carattere si scelgono da Aspetto.",
            "Rientri dell'elemento", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "SCRITTURA\r\n" +
            "  Invio           elemento successivo (Scena > Azione, Personaggio > Dialogo...)\r\n" +
            "  Shift+Invio     stesso elemento, riga nuova\r\n" +
            "  Tab             cambia tipo dell'elemento corrente\r\n" +
            "  Shift+Tab       tipo precedente\r\n" +
            "  Tab su un nome  apre la parentetica\r\n" +
            "  Invio su riga vuota   torna ad Azione\r\n\r\n" +
            "TIPI DI ELEMENTO\r\n" +
            "  Ctrl+1 Scena      Ctrl+2 Azione       Ctrl+3 Personaggio\r\n" +
            "  Ctrl+4 Parentetica  Ctrl+5 Dialogo    Ctrl+6 Transizione\r\n" +
            "  Ctrl+D  dialogo simultaneo (due colonne nel PDF)\r\n" +
            "  Andando a capo compare il menu degli elementi: frecce o numeri 1-6,\r\n" +
            "  Invio conferma, oppure si continua a scrivere e sparisce.\r\n\r\n" +
            "TESTO\r\n" +
            "  Ctrl+B grassetto   Ctrl+I corsivo   Ctrl+U sottolineato\r\n" +
            "  Shift+F3  MAIUSCOLO / minuscolo / Iniziali\r\n\r\n" +
            "FILE\r\n" +
            "  Ctrl+N nuovo   Ctrl+O apri   Ctrl+S salva   Ctrl+P esporta PDF\r\n\r\n" +
            "SELEZIONE E SPOSTAMENTI\r\n" +
            "  Ctrl+A tutto   Ctrl+Shift+A la scena   Ctrl+G vai a scena o pagina\r\n" +
            "  Ctrl+F trova e sostituisci\r\n\r\n" +
            "INSERIMENTI\r\n" +
            "  Ctrl+Shift+N nuova scena   Ctrl+Invio interruzione di pagina\r\n\r\n" +
            "ALTRO\r\n" +
            "  F4 elenco personaggi   F6 schede scena   F7 frontespizio\r\n" +
            "  F8 statistiche   F9 pannello laterale   F11 macchina da scrivere\r\n" +
            "  Ctrl+M nota sull'elemento   Ctrl+ +/- zoom   Ctrl+0 zoom 100%\r\n\r\n" +
            "SUGGERIMENTI\r\n" +
            "  Scrivendo INT. o EST. in un'azione, la riga diventa una scena.\r\n" +
            "  Nei nomi personaggio e nelle scene compare l'elenco di quelli gia' usati:\r\n" +
            "  frecce per scegliere, Invio o Tab per confermare, Esc per chiudere.",
            "Scorciatoie", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        var v = typeof(MainForm).Assembly.GetName().Version;
        MessageBox.Show(this,
            "DraftLite " + (v != null ? v.ToString(3) : "1.0") + "\r\n\r\n" +
            "Scrittura di sceneggiature, solo quello che serve.\r\n" +
            "Formati: .dlite (nativo), Fountain, Final Draft .fdx, PDF.\r\n" +
            "Import da Word, RTF e testo.\r\n\r\n" +
            UpdateChecker.ReleasesUrl + "\r\n\r\n" +
            "Tastiere Digitali srls",
            "Informazioni", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
