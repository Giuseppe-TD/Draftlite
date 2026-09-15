using System;
using System.Drawing;
using System.Windows.Forms;
using DraftLite.Model;

namespace DraftLite.UI;

/// <summary>Dimensioni effettive del documento, separate dallo zoom.</summary>
public sealed class PageLayoutForm : Form
{
    public DocumentLayout LayoutResult { get; private set; }
    public PageLayoutForm(DocumentLayout source)
    {
        Text = "Dimensioni del foglio";
        ClientSize = new Size(390, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 9);
        NumericUpDown Field(string label, double value, int y)
        {
            Controls.Add(new Label { Text = label, Left = 16, Top = y + 3, Width = 230 });
            var n = new NumericUpDown { Left = 255, Top = y, Width = 110,
                DecimalPlaces = 2, Increment = 0.1m, Minimum = 0, Maximum = 50,
                Value = (decimal)(value * 2.54) };
            Controls.Add(n);
            return n;
        }
        var width = Field("Larghezza del foglio (cm)", source.PaperWidth, 16);
        var left = Field("Margine sinistro (cm)", source.LeftMargin, 52);
        var right = Field("Margine destro (cm)", source.RightMargin, 88);
        Controls.Add(new Label { Text = "Il testo andra' a capo nella nuova larghezza.\nI rientri dei tipi di paragrafo si adattano in proporzione.",
            Left = 16, Top = 130, Width = 355, Height = 45 });
        var ok = new Button { Text = "Applica", Left = 180, Top = 195, Width = 90 };
        var cancel = new Button { Text = "Annulla", Left = 278, Top = 195, Width = 90, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        ok.Click += (s, e) =>
        {
            double w = (double)width.Value / 2.54;
            double l = (double)left.Value / 2.54;
            double r = (double)right.Value / 2.54;
            if (w < 5 || w > 20 || w - l - r < 1 || l > w - 2)
            {
                MessageBox.Show(this, "Usa un foglio largo almeno 12,7 cm e lascia almeno 2,54 cm per il testo.",
                    "Dimensioni non valide", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var result = source.Clone();
            double ratio = (w - l - r) / source.TextWidth;
            foreach (var p in result.Paragraphs.Values)
            {
                p.Left = l + (p.Left - source.LeftMargin) * ratio;
                p.Right = l + (p.Right - source.LeftMargin) * ratio;
            }
            result.PaperWidth = w; result.LeftMargin = l; result.RightMargin = r;
            LayoutResult = result;
            DialogResult = DialogResult.OK;
        };
    }
}
