using System;

namespace DraftLite.IO;

/// <summary>Impostazioni di pagina per paginazione e PDF. Default A4, standard sceneggiatura.</summary>
public sealed class PageSetup
{
    public string PaperName { get; set; } = "A4";
    public double WidthPt { get; set; } = 595.28;
    public double HeightPt { get; set; } = 841.89;

    /// <summary>Margine sinistro del testo (standard: 1.5").</summary>
    public double LeftMarginInch { get; set; } = 1.5;
    /// <summary>Margine superiore (standard: 1.0").</summary>
    public double TopMarginInch { get; set; } = 1.0;
    /// <summary>Righe di testo per pagina (standard: 55 = circa un minuto di girato).</summary>
    public int LinesPerPage { get; set; } = 55;

    public bool PageNumbers { get; set; } = true;
    public bool IncludeTitlePage { get; set; } = true;
    public bool SceneNumbers { get; set; } = false;

    /// <summary>Stampa gli asterischi delle righe cambiate e il colore della bozza.</summary>
    public bool RevisionMarks { get; set; } = true;

    /// <summary>Stampa anche i colori e le evidenziazioni scelti a mano (di norma il copione va in nero).</summary>
    public bool TextColors { get; set; } = false;

    /// <summary>Scritta in diagonale su ogni pagina (es. "BOZZA - NON DISTRIBUIRE").</summary>
    public string Watermark { get; set; }

    /// <summary>Destinatario della copia, stampato in fondo a ogni pagina.</summary>
    public string CopyFor { get; set; }

    public static PageSetup A4() => new PageSetup
    {
        PaperName = "A4",
        WidthPt = 595.28,
        HeightPt = 841.89,
        LeftMarginInch = 1.4,
        LinesPerPage = 55
    };

    public static PageSetup Letter() => new PageSetup
    {
        PaperName = "Letter",
        WidthPt = 612,
        HeightPt = 792,
        LeftMarginInch = 1.5,
        LinesPerPage = 55
    };

    public PageSetup Clone() => (PageSetup)MemberwiseClone();
}
