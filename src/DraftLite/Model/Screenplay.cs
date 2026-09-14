using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DraftLite.Model;

/// <summary>La sceneggiatura completa: frontespizio + elenco di elementi.</summary>
public sealed class Screenplay
{
    public TitlePage TitlePage { get; set; } = new TitlePage();
    public List<ScreenElement> Elements { get; set; } = new List<ScreenElement>();

    /// <summary>Prefissi riconosciuti come intestazione di scena (IT + EN).</summary>
    public static readonly Regex SceneHeadingPattern = new Regex(
        @"^\s*(INT\.?/EST\.?|INT\.?/EXT\.?|EST\.?/INT\.?|I\.?/E\.?|INT\b|EST\b|EXT\b|INTERNO\b|ESTERNO\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Transizioni: in inglese finiscono con "TO:", in italiano con i due punti
    /// (DISSOLVENZA A:, STACCO SU:, TAGLIO A:) oppure sono formule chiuse (FINE, A NERO).
    /// </summary>
    public static readonly Regex TransitionPattern = new Regex(
        @"(:\s*$)|^\s*(FINE|THE END|A NERO|AL NERO|IN NERO|FADE IN|FADE OUT|DISSOLVENZA|DISSOLVENZA INCROCIATA|STACCO|TITOLI DI CODA|TITOLI DI TESTA)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikeSceneHeading(string text)
        => !string.IsNullOrWhiteSpace(text) && SceneHeadingPattern.IsMatch(text);

    /// <summary>Elementi senza le righe vuote, pronti per export/paginazione.</summary>
    public List<ScreenElement> Compacted()
        => Elements.Where(e => !e.IsEmpty).Select(e => e.Clone()).ToList();

    public Screenplay Clone() => new Screenplay
    {
        TitlePage = TitlePage.Clone(),
        Elements = Elements.Select(e => e.Clone()).ToList()
    };

    /// <summary>Numero e testo di ogni intestazione di scena, nell'ordine del copione.</summary>
    public List<(int Index, int Number, string Text)> Scenes()
    {
        var list = new List<(int, int, string)>();
        int n = 0;
        for (int i = 0; i < Elements.Count; i++)
        {
            if (Elements[i].Type == ElementType.SceneHeading && !Elements[i].IsEmpty)
                list.Add((i, ++n, Elements[i].Text));
        }
        return list;
    }

    /// <summary>Nomi dei personaggi, nell'ordine di prima comparsa.</summary>
    public List<string> CharacterNames()
    {
        var seen = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Elements)
        {
            if (e.Type != ElementType.Character || e.IsEmpty) continue;
            var name = NormalizeCharacterName(e.Text);
            if (name.Length > 0 && set.Add(name)) seen.Add(name);
        }
        return seen;
    }

    /// <summary>Toglie estensioni tipo (CONT'D), (V.O.), (F.C.) dal nome personaggio.</summary>
    public static string NormalizeCharacterName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        int p = s.IndexOf('(');
        if (p > 0) s = s.Substring(0, p);
        return s.Trim();
    }
}
