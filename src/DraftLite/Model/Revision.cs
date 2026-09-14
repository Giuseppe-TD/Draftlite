using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftLite.Model;

/// <summary>
/// Una bozza congelata. Da quel momento ogni riga cambiata o aggiunta viene marcata
/// con l'asterisco a margine, come si fa sui copioni in lavorazione.
/// I colori seguono l'ordine standard di produzione.
/// </summary>
public sealed class Revision
{
    public static readonly string[] Colors =
    {
        "Bianca", "Blu", "Rosa", "Gialla", "Verde", "Goldenrod", "Salmone", "Ciano",
        "Bianca 2", "Blu 2", "Rosa 2", "Gialla 2"
    };

    /// <summary>Nome del colore della bozza corrente (quella che si sta scrivendo).</summary>
    public string Color { get; set; } = "Bianca";

    /// <summary>Data della bozza, stampata accanto al colore.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Se attiva, le righe cambiate dopo il blocco prendono l'asterisco.</summary>
    public bool Active { get; set; }

    /// <summary>Testo dei paragrafi al momento del blocco: e' il termine di paragone.</summary>
    public List<string> Snapshot { get; set; } = new List<string>();

    public static string NextColor(string current)
    {
        int i = Array.IndexOf(Colors, current ?? "Bianca");
        return i < 0 || i >= Colors.Length - 1 ? Colors[0] : Colors[i + 1];
    }

    public Revision Clone() => new Revision
    {
        Color = Color,
        Date = Date,
        Active = Active,
        Snapshot = new List<string>(Snapshot)
    };

    /// <summary>
    /// Confronta il copione con lo snapshot e dice quali righe sono nuove o modificate.
    /// Prefisso e suffisso uguali restano fuori: cambia solo quello che sta in mezzo.
    /// </summary>
    public bool[] MarkChanged(IList<ScreenElement> elements)
    {
        var flags = new bool[elements.Count];
        if (!Active) return flags;

        var now = elements.Select(e => e.Type + "" + e.Text).ToList();
        var before = Snapshot;

        if (before.Count == 0)
        {
            for (int i = 0; i < flags.Length; i++) flags[i] = true;
            return flags;
        }

        int head = 0;
        while (head < now.Count && head < before.Count && now[head] == before[head]) head++;

        int tail = 0;
        while (tail < now.Count - head && tail < before.Count - head &&
               now[now.Count - 1 - tail] == before[before.Count - 1 - tail]) tail++;

        for (int i = head; i < now.Count - tail; i++) flags[i] = true;
        return flags;
    }

    public static List<string> TakeSnapshot(IEnumerable<ScreenElement> elements)
        => elements.Select(e => e.Type + "" + e.Text).ToList();
}
