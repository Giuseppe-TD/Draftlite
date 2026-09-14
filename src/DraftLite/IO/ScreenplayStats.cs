using System;
using System.Collections.Generic;
using System.Linq;
using DraftLite.Model;

namespace DraftLite.IO;

public sealed class CharacterStat
{
    public string Name;
    public int Speeches;
    public int Words;
    public int SceneCount;
    public string FirstScene;
    public double SharePercent;
}

public sealed class SceneStat
{
    public string Number;
    public string Heading;
    public string Synopsis;
    public int Page;
    public int Lines;
    public List<string> Characters = new List<string>();
}

/// <summary>
/// Tutti i numeri del copione, calcolati una volta sola e riusati da finestra e PDF.
/// </summary>
public sealed class ScreenplayStats
{
    public string Title = string.Empty;
    public string Author = string.Empty;
    public string Paper = "A4";
    public int LinesPerPage = 55;

    public int Pages;
    public int SceneCount;
    public int IntCount;
    public int ExtCount;
    public int DayCount;
    public int NightCount;
    public int TotalSpeeches;
    public int DialogueWords;
    public int ActionWords;

    public List<CharacterStat> Characters = new List<CharacterStat>();
    public List<SceneStat> Scenes = new List<SceneStat>();

    public TimeSpan Duration => TimeSpan.FromMinutes(Pages);

    public static ScreenplayStats Compute(Screenplay sp, PageSetup setup)
    {
        var s = new ScreenplayStats
        {
            Title = sp.TitlePage.Title ?? string.Empty,
            Author = sp.TitlePage.Author ?? string.Empty,
            Paper = setup.PaperName,
            LinesPerPage = setup.LinesPerPage
        };

        var numbered = setup.Clone();
        numbered.SceneNumbers = true;
        var pages = Paginator.Paginate(sp, numbered);
        s.Pages = pages.Count;

        var pageOfScene = new Dictionary<string, int>();
        for (int p = 0; p < pages.Count; p++)
            foreach (var l in pages[p].Lines)
                if (!string.IsNullOrEmpty(l.SceneNumber) && !pageOfScene.ContainsKey(l.SceneNumber))
                    pageOfScene[l.SceneNumber] = p + 1;

        var els = sp.Compacted();
        var numbers = sp.SceneNumbers();
        var stats = new Dictionary<string, CharacterStat>(StringComparer.OrdinalIgnoreCase);

        SceneStat currentScene = null;
        string currentSpeaker = null;
        int sceneIndex = 0;

        foreach (var e in els)
        {
            var style = ElementStyle.Get(e.Type);
            int lineCount = Paginator.Wrap(e.Text, style.Cols).Count + style.SpaceBeforeLines;

            switch (e.Type)
            {
                case ElementType.SceneHeading:
                    var num = sceneIndex < numbers.Count ? numbers[sceneIndex] : (sceneIndex + 1).ToString();
                    currentScene = new SceneStat
                    {
                        Number = num,
                        Heading = e.Text,
                        Synopsis = e.Synopsis,
                        Page = pageOfScene.TryGetValue(num, out var pg) ? pg : 0
                    };
                    s.Scenes.Add(currentScene);
                    sceneIndex++;
                    s.SceneCount++;

                    var up = e.Text.ToUpperInvariant();
                    if (up.StartsWith("INT")) s.IntCount++;
                    if (up.StartsWith("EST") || up.StartsWith("EXT")) s.ExtCount++;
                    if (up.Contains("NOTTE") || up.Contains("NIGHT")) s.NightCount++;
                    else if (up.Contains("GIORNO") || up.Contains("DAY") || up.Contains("MATTIN") ||
                             up.Contains("POMERIGG") || up.Contains("ALBA") || up.Contains("TRAMONTO")) s.DayCount++;
                    break;

                case ElementType.Character:
                    currentSpeaker = Screenplay.NormalizeCharacterName(e.Text);
                    if (currentSpeaker.Length > 0)
                    {
                        if (!stats.TryGetValue(currentSpeaker, out var cs))
                        {
                            cs = new CharacterStat
                            {
                                Name = currentSpeaker,
                                FirstScene = currentScene?.Number ?? "1"
                            };
                            stats[currentSpeaker] = cs;
                        }
                        cs.Speeches++;
                        s.TotalSpeeches++;
                        if (currentScene != null && !currentScene.Characters.Contains(currentSpeaker))
                        {
                            currentScene.Characters.Add(currentSpeaker);
                            cs.SceneCount++;
                        }
                    }
                    break;

                case ElementType.Dialogue:
                    int w = CountWords(e.Text);
                    s.DialogueWords += w;
                    if (currentSpeaker != null && stats.TryGetValue(currentSpeaker, out var sp2)) sp2.Words += w;
                    break;

                case ElementType.Action:
                    s.ActionWords += CountWords(e.Text);
                    break;
            }

            if (currentScene != null) currentScene.Lines += lineCount;
        }

        foreach (var c in stats.Values)
            c.SharePercent = s.DialogueWords > 0 ? 100.0 * c.Words / s.DialogueWords : 0;

        s.Characters = stats.Values
            .OrderByDescending(x => x.Words)
            .ThenByDescending(x => x.Speeches)
            .ToList();

        return s;
    }

    private static int CountWords(string s)
        => string.IsNullOrWhiteSpace(s) ? 0 : s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
