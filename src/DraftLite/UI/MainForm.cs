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
    private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill };
    private readonly Panel _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(120, 120, 124) };
    private readonly Panel _page = new Panel { BackColor = Color.White, Padding = new Padding(26, 18, 14, 10) };
    private readonly StatusStrip _status = new StatusStrip();
    private readonly ToolStripStatusLabel _lblType = new ToolStripStatusLabel { AutoSize = false, Width = 150, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblScene = new ToolStripStatusLabel { AutoSize = false, Width = 300, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblPages = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolStripComboBox _cboType = new ToolStripComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer _statsTimer = new System.Windows.Forms.Timer { Interval = 4000 };
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 120000 };

    private AppSettings _settings = AppSettings.Load();
    private TitlePage _titlePage = new TitlePage();
    private List<(int CharIndex, int Number, string Text)> _scenes = new List<(int, int, string)>();
    private string _path;
    private DocFormat _format = DocFormat.DraftLite;
    private bool _dirty;
    private bool _loading;
    private bool _statsDirty = true;
    private FindForm _findForm;

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

        _split.Panel1.Controls.Add(_sceneList);
        _split.Panel1.Controls.Add(new Label
        {
            Text = "  SCENE",
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(238, 238, 240),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        });
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
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Mi("Nuovo", Keys.Control | Keys.N, (s, e) => { if (ConfirmDiscard()) NewDocument(); }));
        file.DropDownItems.Add(Mi("Apri...", Keys.Control | Keys.O, (s, e) => OpenDialog()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Mi("Salva", Keys.Control | Keys.S, (s, e) => Save()));
        file.DropDownItems.Add(Mi("Salva con nome...", Keys.Control | Keys.Shift | Keys.S, (s, e) => SaveAs()));
        file.DropDownItems.Add(new ToolStripSeparator());

        var export = new ToolStripMenuItem("Esporta");
        export.DropDownItems.Add(Mi("PDF...", Keys.Control | Keys.P, (s, e) => ExportPdf()));
        export.DropDownItems.Add(Mi("Fountain (.fountain)...", Keys.None, (s, e) => ExportAs(DocFormat.Fountain)));
        export.DropDownItems.Add(Mi("Final Draft (.fdx)...", Keys.None, (s, e) => ExportAs(DocFormat.Fdx)));
        file.DropDownItems.Add(export);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Mi("Esci", Keys.None, (s, e) => Close()));
        menu.Items.Add(file);

        var edit = new ToolStripMenuItem("&Modifica");
        edit.DropDownItems.Add(Mi("Annulla", Keys.Control | Keys.Z, (s, e) => _editor.Undo()));
        edit.DropDownItems.Add(Mi("Ripristina", Keys.Control | Keys.Y, (s, e) => _editor.Redo()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Mi("Taglia", Keys.Control | Keys.X, (s, e) => _editor.Cut()));
        edit.DropDownItems.Add(Mi("Copia", Keys.Control | Keys.C, (s, e) => _editor.Copy()));
        edit.DropDownItems.Add(Mi("Incolla", Keys.Control | Keys.V, (s, e) => _editor.Paste()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Mi("Trova e sostituisci...", Keys.Control | Keys.F, (s, e) => ShowFind()));
        menu.Items.Add(edit);

        var format = new ToolStripMenuItem("&Formato");
        int n = 1;
        foreach (var st in ElementStyle.All)
        {
            var type = st.Type;
            format.DropDownItems.Add(Mi(st.Name, Keys.Control | (Keys)((int)Keys.D1 + n - 1),
                (s, e) => { _editor.ApplyType(type); _editor.Focus(); }));
            n++;
        }
        menu.Items.Add(format);

        var view = new ToolStripMenuItem("&Vista");
        var navItem = Mi("Pannello scene", Keys.F9, null);
        navItem.CheckOnClick = true;
        navItem.Checked = _settings.ShowNavigator;
        navItem.CheckedChanged += (s, e) =>
        {
            _settings.ShowNavigator = navItem.Checked;
            _split.Panel1Collapsed = !navItem.Checked;
        };
        view.DropDownItems.Add(navItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Mi("Ingrandisci", Keys.Control | Keys.Oemplus, (s, e) => Zoom(0.1f)));
        view.DropDownItems.Add(Mi("Riduci", Keys.Control | Keys.OemMinus, (s, e) => Zoom(-0.1f)));
        view.DropDownItems.Add(Mi("Zoom 100%", Keys.Control | Keys.D0, (s, e) => SetZoom(1f)));
        menu.Items.Add(view);

        var tools = new ToolStripMenuItem("&Strumenti");
        tools.DropDownItems.Add(Mi("Frontespizio...", Keys.F7, (s, e) => EditTitlePage()));
        tools.DropDownItems.Add(Mi("Statistiche...", Keys.F8, (s, e) => ShowReport()));
        tools.DropDownItems.Add(new ToolStripSeparator());

        var paper = new ToolStripMenuItem("Formato carta");
        var a4 = Mi("A4", Keys.None, (s, e) => { _settings.Paper = "A4"; UpdatePaperChecks(paper); MarkStats(); });
        var letter = Mi("Letter", Keys.None, (s, e) => { _settings.Paper = "Letter"; UpdatePaperChecks(paper); MarkStats(); });
        paper.DropDownItems.Add(a4);
        paper.DropDownItems.Add(letter);
        tools.DropDownItems.Add(paper);
        UpdatePaperChecks(paper);

        var sceneNo = Mi("Numeri di scena nel PDF", Keys.None, null);
        sceneNo.CheckOnClick = true;
        sceneNo.Checked = _settings.SceneNumbers;
        sceneNo.CheckedChanged += (s, e) => _settings.SceneNumbers = sceneNo.Checked;
        tools.DropDownItems.Add(sceneNo);

        var tpInPdf = Mi("Frontespizio nel PDF", Keys.None, null);
        tpInPdf.CheckOnClick = true;
        tpInPdf.Checked = _settings.IncludeTitlePage;
        tpInPdf.CheckedChanged += (s, e) => _settings.IncludeTitlePage = tpInPdf.Checked;
        tools.DropDownItems.Add(tpInPdf);
        menu.Items.Add(tools);

        var help = new ToolStripMenuItem("&?");
        help.DropDownItems.Add(Mi("Scorciatoie da tastiera", Keys.F1, (s, e) => ShowShortcuts()));
        help.DropDownItems.Add(Mi("Informazioni su DraftLite", Keys.None, (s, e) => ShowAbout()));
        menu.Items.Add(help);

        return menu;
    }

    private void UpdatePaperChecks(ToolStripMenuItem paper)
    {
        foreach (ToolStripMenuItem it in paper.DropDownItems.OfType<ToolStripMenuItem>())
            it.Checked = it.Text == _settings.Paper;
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
        _editor.DocumentIndexed += (s, e) => RefreshScenes();

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
        _sceneList.DragOver += (s, e) => e.Effect = DragDropEffects.Move;
        _sceneList.DragDrop += SceneList_DragDrop;

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
            try { _split.SplitterDistance = 250; } catch { }
            LayoutPage();
            _editor.ApplyPageWidth();
            _editor.Focus();
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

    // ------------------------------------------------------------------ DOCUMENTO

    private void NewDocument()
    {
        _loading = true;
        try
        {
            _titlePage = new TitlePage();
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
        MarkStats();
        UpdateStatus();
        _editor.Focus();
    }

    private Screenplay CurrentScreenplay() => new Screenplay
    {
        TitlePage = _titlePage,
        Elements = _editor.GetElements()
    };

    private void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Tutti i formati (*.dlite;*.fountain;*.spmd;*.fdx;*.txt)|*.dlite;*.fountain;*.spmd;*.fdx;*.txt|" +
                     "DraftLite (*.dlite)|*.dlite|" +
                     "Fountain (*.fountain;*.spmd;*.txt)|*.fountain;*.spmd;*.txt|" +
                     "Final Draft (*.fdx)|*.fdx|" +
                     "Tutti i file (*.*)|*.*",
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
            switch (ext)
            {
                case ".fdx": sp = FdxIO.Load(path); _format = DocFormat.Fdx; break;
                case ".fountain":
                case ".spmd":
                case ".txt": sp = FountainIO.Load(path); _format = DocFormat.Fountain; break;
                default: sp = DraftLiteFile.Load(path); _format = DocFormat.DraftLite; break;
            }

            _loading = true;
            try
            {
                _titlePage = sp.TitlePage;
                _editor.SetElements(sp.Elements.Count > 0
                    ? sp.Elements
                    : new List<ScreenElement> { new ScreenElement(ElementType.SceneHeading, string.Empty) });
                _path = path;
            }
            finally { _loading = false; }

            _settings.LastFolder = Path.GetDirectoryName(path) ?? string.Empty;
            SetDirty(false);
            RefreshScenes();
            MarkStats();
            UpdateStatus();
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
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.ChangeExtension(SuggestedFileName(), ".pdf"),
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            PdfExporter.Export(dlg.FileName, CurrentScreenplay(), _settings.ToPageSetup());
            if (MessageBox.Show(this, "PDF creato.\n\nAprirlo adesso?", "DraftLite",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dlg.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export PDF non riuscito:\n\n" + ex.Message, "DraftLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string SuggestedFileName()
    {
        if (!string.IsNullOrEmpty(_path)) return Path.GetFileName(_path);
        var t = _titlePage.Title;
        if (string.IsNullOrWhiteSpace(t)) return "senza titolo.dlite";
        foreach (var c in Path.GetInvalidFileNameChars()) t = t.Replace(c, '-');
        return t.Trim() + ".dlite";
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
        Text = "DraftLite - " + name + (dirty ? " *" : "");
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
                OpenFile(AppSettings.AutoSavePath);
                _path = null;
                SetDirty(true);
            }
            else TryDeleteAutoSave();
        }
        catch { }
    }

    // ------------------------------------------------------------------ SCENE

    private void RefreshScenes()
    {
        var scenes = _editor.GetSceneIndex();
        _scenes = scenes;

        bool savedLoading = _loading;
        _loading = true;
        try
        {
            int sel = _sceneList.SelectedIndex;
            _sceneList.BeginUpdate();
            _sceneList.Items.Clear();
            foreach (var s in scenes)
                _sceneList.Items.Add(s.Number + ".  " + s.Text);
            _sceneList.EndUpdate();
            if (sel >= 0 && sel < _sceneList.Items.Count) _sceneList.SelectedIndex = sel;
        }
        finally { _loading = savedLoading; }

        UpdateStatus();
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
        if (_dragIndex < 0) return;
        var pt = _sceneList.PointToClient(new Point(e.X, e.Y));
        int target = _sceneList.IndexFromPoint(pt);
        if (target < 0) target = _sceneList.Items.Count - 1;
        if (target == _dragIndex) return;

        MoveScene(_dragIndex, target);
        _dragIndex = -1;
    }

    /// <summary>Sposta un'intera scena (intestazione + contenuto) in un'altra posizione.</summary>
    private void MoveScene(int from, int to)
    {
        var els = _editor.GetElements();
        var starts = new List<int>();
        for (int i = 0; i < els.Count; i++)
        {
            // stesso criterio del pannello scene: altrimenti gli indici si disallineano
            var txt = els[i].Text.Trim();
            if (els[i].Type == ElementType.SceneHeading && txt.Length > 0 &&
                txt.Length <= 70 && txt == txt.ToUpperInvariant())
                starts.Add(i);
        }

        if (from < 0 || from >= starts.Count || to < 0 || to >= starts.Count) return;

        var head = els.Take(starts[0]).ToList();
        var blocks = new List<List<ScreenElement>>();
        for (int k = 0; k < starts.Count; k++)
        {
            int s = starts[k];
            int e = (k + 1 < starts.Count) ? starts[k + 1] : els.Count;
            blocks.Add(els.GetRange(s, e - s));
        }

        var moved = blocks[from];
        blocks.RemoveAt(from);
        blocks.Insert(to, moved);

        var result = new List<ScreenElement>(head);
        foreach (var b in blocks) result.AddRange(b);

        _editor.SetElements(result, false);
        SetDirty(true);
        RefreshScenes();
        MarkStats();
        if (to >= 0 && to < _scenes.Count) _editor.GoToCharIndex(_scenes[to].CharIndex);
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
        _lblType.Text = "  " + ElementStyle.NameOf(type);

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

    // ------------------------------------------------------------------ DIALOGHI

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

    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "SCRITTURA\r\n" +
            "  Invio           elemento successivo (Scena > Azione, Personaggio > Dialogo...)\r\n" +
            "  Tab             cambia tipo dell'elemento corrente\r\n" +
            "  Shift+Tab       tipo precedente\r\n" +
            "  Tab su nome     apre la parentetica\r\n" +
            "  Invio su riga vuota   torna ad Azione\r\n\r\n" +
            "TIPI DI ELEMENTO\r\n" +
            "  Ctrl+1 Scena      Ctrl+2 Azione      Ctrl+3 Personaggio\r\n" +
            "  Ctrl+4 Parentetica  Ctrl+5 Dialogo   Ctrl+6 Transizione\r\n\r\n" +
            "FILE\r\n" +
            "  Ctrl+N nuovo   Ctrl+O apri   Ctrl+S salva   Ctrl+P esporta PDF\r\n\r\n" +
            "ALTRO\r\n" +
            "  F7 frontespizio   F8 statistiche   F9 pannello scene\r\n" +
            "  Ctrl+F trova      Ctrl+ +/- zoom\r\n\r\n" +
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
            "Formati: .dlite (nativo), Fountain, Final Draft .fdx, PDF.\r\n\r\n" +
            "Tastiere Digitali srls",
            "Informazioni", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
