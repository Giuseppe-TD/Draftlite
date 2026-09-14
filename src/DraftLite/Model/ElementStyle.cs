using System;
using System.Collections.Generic;

namespace DraftLite.Model;

/// <summary>
/// Metriche tipografiche standard della sceneggiatura, condivise fra editor a video
/// ed export PDF. Tutte le misure orizzontali sono in pollici a partire dal margine
/// sinistro del testo (1.5" dal bordo pagina). L'area di testo e' larga 6.0".
/// </summary>
public sealed class ElementStyle
{
    public const double TextWidthInch = 6.0;
    public const double LineHeightPt = 12.0;      // Courier 12 = interlinea singola
    public const int TwipsPerInch = 1440;
    public const int TwipsPerLine = 240;          // 12pt * 20
    public const double CharWidthPt = 7.2;        // Courier 12 = 10 caratteri per pollice

    public ElementType Type { get; private set; }
    public string Name { get; private set; }
    public double LeftInch { get; private set; }
    public double WidthInch { get; private set; }
    public int SpaceBeforeLines { get; private set; }
    public bool UpperCase { get; private set; }
    public bool Bold { get; private set; }
    public bool RightAlign { get; private set; }

    public int LeftTwips => (int)Math.Round(LeftInch * TwipsPerInch);
    public int RightTwips => (int)Math.Round((TextWidthInch - LeftInch - WidthInch) * TwipsPerInch);
    public int SpaceBeforeTwips => SpaceBeforeLines * TwipsPerLine;

    /// <summary>Colonna di partenza in caratteri (Courier = 10 cpi).</summary>
    public int Col => (int)Math.Round(LeftInch * 10);
    /// <summary>Larghezza del blocco in caratteri: oltre questa si va a capo.</summary>
    public int Cols => (int)Math.Round(WidthInch * 10);

    private static readonly Dictionary<ElementType, ElementStyle> Map = new()
    {
        [ElementType.SceneHeading] = new ElementStyle
        {
            Type = ElementType.SceneHeading, Name = "Scena",
            LeftInch = 0.0, WidthInch = 6.0, SpaceBeforeLines = 2,
            UpperCase = true, Bold = true, RightAlign = false
        },
        [ElementType.Action] = new ElementStyle
        {
            Type = ElementType.Action, Name = "Azione",
            LeftInch = 0.0, WidthInch = 6.0, SpaceBeforeLines = 1,
            UpperCase = false, Bold = false, RightAlign = false
        },
        [ElementType.Character] = new ElementStyle
        {
            Type = ElementType.Character, Name = "Personaggio",
            LeftInch = 2.2, WidthInch = 3.8, SpaceBeforeLines = 1,
            UpperCase = true, Bold = false, RightAlign = false
        },
        [ElementType.Parenthetical] = new ElementStyle
        {
            Type = ElementType.Parenthetical, Name = "Parentetica",
            LeftInch = 1.6, WidthInch = 2.5, SpaceBeforeLines = 0,
            UpperCase = false, Bold = false, RightAlign = false
        },
        [ElementType.Dialogue] = new ElementStyle
        {
            Type = ElementType.Dialogue, Name = "Dialogo",
            LeftInch = 1.0, WidthInch = 3.5, SpaceBeforeLines = 0,
            UpperCase = false, Bold = false, RightAlign = false
        },
        [ElementType.Transition] = new ElementStyle
        {
            Type = ElementType.Transition, Name = "Transizione",
            LeftInch = 0.0, WidthInch = 6.0, SpaceBeforeLines = 1,
            UpperCase = true, Bold = false, RightAlign = true
        }
    };

    public static ElementStyle Get(ElementType t) => Map[t];

    public static IEnumerable<ElementStyle> All
    {
        get
        {
            yield return Map[ElementType.SceneHeading];
            yield return Map[ElementType.Action];
            yield return Map[ElementType.Character];
            yield return Map[ElementType.Parenthetical];
            yield return Map[ElementType.Dialogue];
            yield return Map[ElementType.Transition];
        }
    }

    public static string NameOf(ElementType t) => Map[t].Name;

    /// <summary>Ciclo dei tipi usato da Tab / Shift+Tab.</summary>
    public static readonly ElementType[] TabCycle =
    {
        ElementType.Action,
        ElementType.Character,
        ElementType.Dialogue,
        ElementType.Parenthetical,
        ElementType.Transition,
        ElementType.SceneHeading
    };

    /// <summary>Tipo dell'elemento creato premendo Invio a fine elemento.</summary>
    public static ElementType NextAfterEnter(ElementType current) => current switch
    {
        ElementType.SceneHeading => ElementType.Action,
        ElementType.Action => ElementType.Action,
        ElementType.Character => ElementType.Dialogue,
        ElementType.Parenthetical => ElementType.Dialogue,
        ElementType.Dialogue => ElementType.Action,
        ElementType.Transition => ElementType.SceneHeading,
        _ => ElementType.Action
    };
}
