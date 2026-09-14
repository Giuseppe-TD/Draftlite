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
    private readonly Panel _pageHost = new Panel { Dock = DockStyle.Fill, Tag = "desk" };
    private readonly Panel _page = new Panel { Padding = new Padding(26, 18, 14, 10), Tag = "page" };
    private readonly StatusStrip _status = new StatusStrip();
    private readonly ToolStripStatusLabel _lblType = new ToolStripStatusLabel { AutoSize = false, Width = 150, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblScene = new ToolStripStatusLabel { AutoSize = false, Width = 300, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblPages = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolStripComboBox _cboType = new ToolStripComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
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
        var tool = BuildToolbar();

        _page.Controls.Add(_editor);
        _editor.Dock = DockStyle.Fill;
        _pageHost.Controls.Add(_page);

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
        Controls.Add(tool);
        Controls.Add(menu);
        Controls.Add(_status);
        MainMenuStrip = menu;
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
        edit.DropDownItems.Add(Mi("Trova e sostituisci...", Keys.Control | Keys.F, (s, e) => ShowFind()));
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

        // ---------------- Vista
        var view = new ToolStripMenuItem("&Vista");
        var navItem = Mi("Pannello laterale", Keys.F9, null);
        navItem.CheckOnClick = true;
        navItem.Checked = _settings.ShowNavigator;
        navItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowNavigator = navItem.Checked;
            _split.Panel1Collapsed = !navItem.Checked;
        };
        view.DropDownItems.Add(navItem);
        view.DropDownItems.Add(Mi("Schede scena...", Keys.F6, (s, e) => ShowCards()));
        view.DropDownItems.Add(new ToolStripSeparator());

        _typewriterItem = Mi("Macchina da scrivere", Keys.F11, null);
        _typewriterItem.CheckOnClick = true;
        _typewriterItem.Checked = _settings.Typewriter;
        _typewriterItem.CheckedChanged += (s, e) =>
        {
            _settings.Typewriter = _typewriterItem.Checked;
            _editor.TypewriterMode = _typewriterItem.Checked;
        };
        view.DropDownItems.Add(_typewriterItem);
        view.DropDownItems.Add(Mi("Aspetto (carattere e tema)...", Keys.None, (s, e) => ShowAppearance()));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Mi("Ingrandisci", Keys.Control | Keys.Oemplus, (s, e) => Zoom(0.1f)));
        view.DropDownItems.Add(Mi("Riduci", Keys.Control | Keys.OemMinus, (s, e) => Zoom(-0.1f)));
        view.DropDownItems.Add(Mi("Zoom 100%", Keys.Control | Keys.D0, (s, e) => SetZoom(1f)));
        menu.Items.Add(view);

        // ---------------- Strumenti
        var tools = new ToolStripMenuItem("&Strumenti");
        tools.DropDownItems.Add(Mi("Frontespizio...", Keys.F7, (s, e) => EditTitlePage()));
        tools.DropDownItems.Add(Mi("Statistiche...", Keys.F8, (s, e) => ShowReport()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Mi("Nota sull'elemento...", Keys.Control | Keys.M, (s, e) => EditNote()));

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

    private ToolStrip BuildToolbar()
    {
        var tool = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };

        foreach (var st in ElementStyle.All) _cboType.Items.Add(st.Name);
        _cboType.SelectedIndexChanged += (s, e) =>
        {
            if (_loading || _cboType.SelectedIndex < 0) return;
            var type = ElementStyle.All.ElementAt(_cboType.SelectedIndex).Type;
            if (type != _editor.CurrentType) { _editor.ApplyType(type); _editor.Focus(); }
        };

        tool.Items.Add(new ToolStripLabel("Elemento:"));
        tool.Items.Add(_cboType);
        tool.Items.Add(new ToolStripSeparator());

        void Btn(string text, string tip, EventHandler h)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += h;
            tool.Items.Add(b);
        }

        Btn("Schede", "Bacheca delle scene (F6)", (s, e) => ShowCards());
        Btn("Nota", "Nota sull'elemento corrente (Ctrl+M)", (s, e) => EditNote());
        Btn("Frontespizio", "Titolo, autore, contatti (F7)", (s, e) => EditTitlePage());
        Btn("Statistiche", "Pagine, scene, battute (F8)", (s, e) => ShowReport());
        tool.Items.Add(new ToolStripSeparator());
        Btn("Esporta PDF", "Esporta in PDF standard (Ctrl+P)", (s, e) => ExportPdf());

        return tool;
    }

    private void WireEvents()
    {
        _editor.TextChanged += (s, e) => { if (!_loading) { SetDirty(true); MarkStats(); } };
        _editor.CurrentTypeChanged += (s, e) => UpdateStatus();
        _editor.DocumentIndexed += (s, e) => { RefreshScenes(); RefreshNotes(); };

        _pageHost.Resize += (s, e) => LayoutPage();
        _pageHost.MouseDown += (s, e) => _editor.Focus();

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
            LayoutPage();
            _editor.ApplyPageWidth();
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

    private void LayoutPage()
    {
        int w = Math.Min(_editor.PageWidthPixels + _page.Padding.Horizontal, _pageHost.ClientSize.Width);
        int x = Math.Max(0, (_pageHost.ClientSize.Width - w) / 2);
        _page.Bounds = new Rectangle(x, 0, w, _pageHost.ClientSize.Height);
    }

    private void Zoom(float delta) => SetZoom(_editor.ZoomFactor + delta);

    private void SetZoom(float z)
    {
        _editor.ZoomFactor = Math.Max(0.6f, Math.Min(2.5f, z));
        _editor.ApplyPageWidth();
        LayoutPage();
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
        _editor.SetEditorFont(_settings.FontFamily);
        _editor.TypewriterMode = _settings.Typewriter;
        LayoutPage();
    }

    // ------------------------------------------------------------------ DOCUMENTO

    private void NewDocument()
    {
        _loading = true;
        try
        {
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

        SetDirty(false);
        RefreshScenes();
        RefreshNotes();
        MarkStats();
        UpdateStatus();
        _editor.Focus();
    }

    private Screenplay CurrentScreenplay() => new Screenplay
    {
        TitlePage = _titlePage,
        Revision = _revision,
        Elements = _editor.GetElements()
    };

    private void LoadScreenplay(Screenplay sp, string path, DocFormat format)
    {
        _loading = true;
        try
        {
            _titlePage = sp.TitlePage ?? new TitlePage();
            _revision = sp.Revision ?? new Revision();
            _editor.SetElements(sp.Elements.Count > 0
                ? sp.Elements
                : new List<ScreenElement> { new ScreenElement(ElementType.SceneHeading, string.Empty) });
            _path = path;
            _format = format;
        }
        finally { _loading = false; }

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
        using var opt = new PdfOptionsForm(_settings.ToPageSetup());
        if (opt.ShowDialog(this) != DialogResult.OK || opt.Setup == null) return;
        _settings.FromPageSetup(opt.Setup);

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
            PdfExporter.ExportSides(dlg.FileName, sp, pick.Character, _settings.ToPageSetup());
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
            PdfExporter.ExportReport(dlg.FileName, CurrentScreenplay(), _settings.ToPageSetup());
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
            var pages = Paginator.CountPages(CurrentScreenplay(), _settings.ToPageSetup());
            _lblPages.Text = pages + (pages == 1 ? " pagina" : " pagine") +
                             "   ~" + pages + " min      " + _settings.Paper;
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
            if (idx >= 0 && _cboType.SelectedIndex != idx) _cboType.SelectedIndex = idx;
        }
        finally { _loading = savedLoading; }

        int caret = _editor.SelectionStart;
        var scene = _scenes.LastOrDefault(s => s.CharIndex <= caret);
        _lblScene.Text = scene.Number > 0
            ? "Scena " + scene.Number + ": " + Truncate(scene.Text, 34)
            : "";
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
        using var dlg = new ReportForm(CurrentScreenplay(), _settings.ToPageSetup());
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
        using var dlg = new AppearanceForm(_settings.FontFamily, _settings.ThemeName, _settings.Typewriter);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settings.FontFamily = dlg.SelectedFont;
        _settings.ThemeName = dlg.SelectedTheme;
        _settings.Typewriter = dlg.Typewriter;
        if (_typewriterItem != null) _typewriterItem.Checked = dlg.Typewriter;
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
            "  Ctrl+D  dialogo simultaneo (due colonne nel PDF)\r\n\r\n" +
            "FILE\r\n" +
            "  Ctrl+N nuovo   Ctrl+O apri   Ctrl+S salva   Ctrl+P esporta PDF\r\n\r\n" +
            "ALTRO\r\n" +
            "  F6 schede scena   F7 frontespizio   F8 statistiche\r\n" +
            "  F9 pannello laterale   F11 macchina da scrivere\r\n" +
            "  Ctrl+M nota sull'elemento   Ctrl+F trova   Ctrl+ +/- zoom\r\n\r\n" +
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
