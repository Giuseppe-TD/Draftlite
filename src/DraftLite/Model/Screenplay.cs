using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DraftLite.Model;

/// <summary>La sceneggiatura completa: frontespizio, elementi, stato della revisione.</summary>
public sealed class Screenplay
{
    public TitlePage TitlePage { get; set; } = new TitlePage();
    public List<ScreenElement> Elements { get; set; } = new List<ScreenElement>();
    public Revision Revision { get; set; } = new Revision();

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

    /// <summary>Riga che forza il salto pagina: tre o piu' "=", la convenzione di Fountain.</summary>
    public const string PageBreakMark = "===";

    /// <summary>Vero se l'elemento e' un'interruzione di pagina voluta dall'autore.</summary>
    public static bool IsPageBreak(ScreenElement e)
    {
        if (e == null || e.Type != ElementType.Action) return false;
        var t = StyledText.Plain(e.Text).Trim();
        return t.Length >= 3 && t.All(c => c == '=');
    }

    /// <summary>Elementi senza le righe vuote, pronti per export/paginazione.</summary>
    public List<ScreenElement> Compacted()
        => Elements.Where(e => !e.IsEmpty).Select(e => e.Clone()).ToList();

    public Screenplay Clone() => new Screenplay
    {
        TitlePage = TitlePage.Clone(),
        Elements = Elements.Select(e => e.Clone()).ToList(),
        Revision = Revision.Clone()
    };

    /// <summary>Indice, numero e testo di ogni intestazione di scena, nell'ordine del copione.</summary>
    public List<(int Index, string Number, string Text)> Scenes()
    {
        var list = new List<(int, string, string)>();
        var numbers = SceneNumbers();
        int n = 0;
        for (int i = 0; i < Elements.Count; i++)
        {
            if (Elements[i].Type == ElementType.SceneHeading && !Elements[i].IsEmpty)
            {
                list.Add((i, n < numbers.Count ? numbers[n] : (n + 1).ToString(), Elements[i].Text));
                n++;
            }
        }
        return list;
    }

    /// <summary>
    /// Numeri di scena effettivi. Se nessuna scena e' bloccata sono 1, 2, 3...;
    /// se lo sono, le scene aggiunte dopo il blocco diventano 12A, 12B e le vecchie non slittano.
    /// </summary>
    public List<string> SceneNumbers()
    {
        var scenes = Elements.Where(e => e.Type == ElementType.SceneHeading && !e.IsEmpty).ToList();
        var result = new List<string>(scenes.Count);

        bool anyLocked = scenes.Any(s => !string.IsNullOrWhiteSpace(s.Number));
        if (!anyLocked)
        {
            for (int i = 0; i < scenes.Count; i++) result.Add((i + 1).ToString());
            return result;
        }

        string baseNumber = "0";
        int suffix = 0;
        foreach (var s in scenes)
        {
            if (!string.IsNullOrWhiteSpace(s.Number))
            {
                baseNumber = s.Number.Trim();
                suffix = 0;
                result.Add(baseNumber);
            }
            else
            {
                suffix++;
                result.Add(baseNumber + SuffixLetters(suffix));
            }
        }
        return result;
    }

    /// <summary>1 = A, 26 = Z, 27 = AA...</summary>
    public static string SuffixLetters(int n)
    {
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + n % 26));
            n /= 26;
        }
        return sb.ToString();
    }

    /// <summary>Congela la numerazione attuale: da qui in poi le nuove scene prendono le lettere.</summary>
    public void LockSceneNumbers()
    {
        var numbers = SceneNumbers();
        int n = 0;
        foreach (var e in Elements)
            if (e.Type == ElementType.SceneHeading && !e.IsEmpty)
                e.Number = numbers[n++];
    }

    public void UnlockSceneNumbers()
    {
        foreach (var e in Elements) e.Number = null;
    }

    public bool SceneNumbersLocked
        => Elements.Any(e => e.Type == ElementType.SceneHeading && !string.IsNullOrWhiteSpace(e.Number));

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

    /// <summary>
    /// Toglie estensioni tipo (CONT'D), (V.O.), (F.C.) dal nome personaggio, e anche
    /// i marcatori di stile: il nome deve venire fuori uguale sia che sia stato scritto
    /// normale sia che qualcuno l'abbia messo in corsivo.
    /// </summary>
    public static string NormalizeCharacterName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = StyledText.Plain(text).Trim().TrimEnd('^').Trim();
        int p = s.IndexOf('(');
        if (p > 0) s = s.Substring(0, p);
        return s.Trim();
    }

    /// <summary>
    /// Copione ridotto alle sole scene in cui compare il personaggio (sides per l'attore).
    /// </summary>
    public Screenplay Sides(string character)
    {
        var target = NormalizeCharacterName(character);
        var els = Compacted();
        var result = new Screenplay
        {
            TitlePage = TitlePage.Clone(),
            Revision = Revision.Clone()
        };
        result.TitlePage.Title = (TitlePage.Title ?? string.Empty) + " - " + target;

        var numbers = SceneNumbers();
        int sceneIdx = -1;
        var blocks = new List<(int Scene, List<ScreenElement> Items, bool Has)>();
        List<ScreenElement> current = null;
        bool has = false;

        foreach (var e in els)
        {
            if (e.Type == ElementType.SceneHeading)
            {
                if (current != null) blocks.Add((sceneIdx, current, has));
                sceneIdx++;
                current = new List<ScreenElement> { e };
                has = false;
            }
            else
            {
                current ??= new List<ScreenElement>();
                current.Add(e);
            }

            if (e.Type == ElementType.Character &&
                NormalizeCharacterName(e.Text).Equals(target, StringComparison.OrdinalIgnoreCase))
                has = true;
        }
        if (current != null) blocks.Add((sceneIdx, current, has));

        foreach (var b in blocks.Where(x => x.Has))
        {
            foreach (var item in b.Items)
            {
                var c = item.Clone();
                if (c.Type == ElementType.SceneHeading && b.Scene >= 0 && b.Scene < numbers.Count)
                    c.Number = numbers[b.Scene];
                result.Elements.Add(c);
            }
        }
        return result;
    }
}
