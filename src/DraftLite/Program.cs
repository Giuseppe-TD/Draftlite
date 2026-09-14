using System;
using System.Windows.Forms;

namespace DraftLite;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            MessageBox.Show("Errore imprevisto:\n\n" + (e.ExceptionObject as Exception)?.Message,
                "DraftLite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Application.Run(new UI.MainForm(args.Length > 0 ? args[0] : null));
    }
}
