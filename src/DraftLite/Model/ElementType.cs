namespace DraftLite.Model;

/// <summary>
/// I sei elementi base di una sceneggiatura. Volutamente pochi:
/// DraftLite non vuole essere Final Draft, vuole essere la sua parte utile.
/// </summary>
public enum ElementType
{
    SceneHeading = 0,
    Action = 1,
    Character = 2,
    Parenthetical = 3,
    Dialogue = 4,
    Transition = 5
}
