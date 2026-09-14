using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DraftLite.UI;

/// <summary>
/// Controllo aggiornamenti: chiede a GitHub qual e' l'ultima release e lo dice solo
/// se ce n'e' una piu' nuova. Nessuna libreria in piu', nessun dato inviato, nessun
/// download automatico: al massimo apre la pagina delle release nel browser.
/// Se la rete non risponde, l'utente non se ne accorge nemmeno.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = "Giuseppe-TD/DraftLite";
    private const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

    private static readonly HttpClient Http = CreateClient();
    private static bool _running;

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v ?? new Version(1, 0, 0, 0);
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // GitHub rifiuta le richieste senza User-Agent
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DraftLite/" + CurrentVersion.ToString(3));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public sealed class ReleaseInfo
    {
        public Version Version;
        public string Tag = string.Empty;
        public string Name = string.Empty;
        public string Notes = string.Empty;
        public string Url = ReleasesUrl;
    }

    /// <summary>Legge l'ultima release pubblicata. Restituisce null se non si riesce.</summary>
    public static async Task<ReleaseInfo> FetchLatestAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(ApiUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var version = ParseVersion(tag);
            if (version == null) return null;

            return new ReleaseInfo
            {
                Version = version,
                Tag = tag ?? string.Empty,
                Name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty,
                Notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? string.Empty) : string.Empty,
                Url = root.TryGetProperty("html_url", out var u) ? (u.GetString() ?? ReleasesUrl) : ReleasesUrl
            };
        }
        catch { return null; }   // rete assente, timeout, JSON strano: non e' un problema dell'utente
    }

    /// <summary>"v1.2.0", "1.2", "v1.2.0-beta" -> Version. Null se non ha senso.</summary>
    public static Version ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash > 0) s = s.Substring(0, dash);
        return Version.TryParse(s.Count(c => c == '.') == 1 ? s + ".0" : s, out var v) ? v : null;
    }

    /// <summary>
    /// Avvia il controllo in sottofondo. In automatico parla solo se c'e' una versione
    /// nuova, non piu' di una volta al giorno e non per una versione gia' saltata.
    /// </summary>
    public static void CheckInBackground(Form owner, AppSettings settings, bool userRequested)
    {
        if (owner == null || settings == null || _running) return;

        if (!userRequested)
        {
            if (!settings.CheckUpdates) return;
            if ((DateTime.UtcNow - settings.LastUpdateCheck).TotalHours < 20) return;
        }

        _running = true;
        _ = Task.Run(async () =>
        {
            ReleaseInfo info = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                info = await FetchLatestAsync(cts.Token).ConfigureAwait(false);
            }
            catch { /* silenzio */ }
            finally { _running = false; }

            try
            {
                if (owner.IsDisposed || !owner.IsHandleCreated) return;
                owner.BeginInvoke(new Action(() => Report(owner, settings, info, userRequested)));
            }
            catch { /* la finestra e' gia' sparita */ }
        });
    }

    private static void Report(Form owner, AppSettings settings, ReleaseInfo info, bool userRequested)
    {
        settings.LastUpdateCheck = DateTime.UtcNow;

        if (info == null)
        {
            if (userRequested)
                MessageBox.Show(owner,
                    "Non sono riuscito a contattare GitHub.\r\n\r\nRiprova piu' tardi, o guarda tu stesso:\r\n" + ReleasesUrl,
                    "Aggiornamenti", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var current = CurrentVersion;
        bool newer = info.Version > new Version(current.Major, current.Minor, current.Build);

        if (!newer)
        {
            if (userRequested)
                MessageBox.Show(owner,
                    "DraftLite " + current.ToString(3) + " e' aggiornato.",
                    "Aggiornamenti", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!userRequested &&
            string.Equals(settings.SkippedVersion, info.Version.ToString(3), StringComparison.OrdinalIgnoreCase))
            return;

        using var dlg = new UpdateForm(current, info);
        var result = dlg.ShowDialog(owner);

        if (dlg.Skip)
            settings.SkippedVersion = info.Version.ToString(3);

        if (result == DialogResult.OK)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = info.Url, UseShellExecute = true });
            }
            catch { /* niente browser, pazienza */ }
        }
    }
}

/// <summary>Avviso di aggiornamento: scarica, piu' tardi, oppure salta questa versione.</summary>
public sealed class UpdateForm : Form
{
    public bool Skip { get; private set; }

    public UpdateForm(Version current, UpdateChecker.ReleaseInfo info)
    {
        Text = "Aggiornamento disponibile";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 300);
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label
        {
            Text = "C'e' DraftLite " + info.Version.ToString(3),
            Left = 16,
            Top = 16,
            Width = 420,
            Height = 26,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold)
        });

        Controls.Add(new Label
        {
            Text = "Hai la " + current.ToString(3) + ". Le tue impostazioni e i tuoi copioni restano dove sono.",
            Left = 16,
            Top = 46,
            Width = 430,
            Height = 20,
            ForeColor = Color.FromArgb(100, 100, 100)
        });

        var notes = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Text = string.IsNullOrWhiteSpace(info.Notes)
                ? "(nessuna nota di rilascio)"
                : info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n").Trim()
        };
        notes.SetBounds(16, 76, 428, 160);
        Controls.Add(notes);

        var download = new Button { Text = "Vai alla release", DialogResult = DialogResult.OK, Width = 130, Left = 314, Top = 250 };
        var later = new Button { Text = "Piu' tardi", DialogResult = DialogResult.Cancel, Width = 100, Left = 208, Top = 250 };
        var skip = new Button { Text = "Salta questa", Width = 110, Left = 16, Top = 250 };
        skip.Click += (s, e) => { Skip = true; DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(download);
        Controls.Add(later);
        Controls.Add(skip);
        AcceptButton = download;
        CancelButton = later;
    }
}
