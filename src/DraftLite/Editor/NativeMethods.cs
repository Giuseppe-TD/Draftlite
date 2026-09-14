using System;
using System.Runtime.InteropServices;

namespace DraftLite.Editor;

[StructLayout(LayoutKind.Sequential)]
internal struct PARAFORMAT2
{
    public int cbSize;
    public uint dwMask;
    public short wNumbering;
    public short wEffects;
    public int dxStartIndent;
    public int dxRightIndent;
    public int dxOffset;
    public short wAlignment;
    public short cTabCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public int[] rgxTabs;
    public int dySpaceBefore;
    public int dySpaceAfter;
    public int dyLineSpacing;
    public short sStyle;
    public byte bLineSpacingRule;
    public byte bOutlineLevel;
    public short wShadingWeight;
    public short wShadingStyle;
    public short wNumberingStart;
    public short wNumberingStyle;
    public short wNumberingTab;
    public short wBorderSpace;
    public short wBorderWidth;
    public short wBorders;

    public static PARAFORMAT2 Create()
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf(typeof(PARAFORMAT2)),
            rgxTabs = new int[32]
        };
        return pf;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

internal static class NativeMethods
{
    public const int WM_USER = 0x0400;
    public const int WM_SETREDRAW = 0x000B;

    public const int EM_LINESCROLL = WM_USER + 6;       // 0x406
    public const int EM_GETPARAFORMAT = WM_USER + 61;   // 0x43D
    public const int EM_SETPARAFORMAT = WM_USER + 71;   // 0x447
    public const int EM_SETTARGETDEVICE = WM_USER + 72; // 0x448
    public const int EM_GETSCROLLPOS = WM_USER + 221;   // 0x4DD
    public const int EM_SETSCROLLPOS = WM_USER + 222;   // 0x4DE

    public const int SCF_SELECTION = 0x0001;

    public const uint PFM_STARTINDENT = 0x00000001;
    public const uint PFM_RIGHTINDENT = 0x00000002;
    public const uint PFM_OFFSET = 0x00000004;
    public const uint PFM_ALIGNMENT = 0x00000008;
    public const uint PFM_SPACEBEFORE = 0x00000040;
    public const uint PFM_SPACEAFTER = 0x00000080;

    public const short PFA_LEFT = 1;
    public const short PFA_RIGHT = 2;
    public const short PFA_CENTER = 3;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref PARAFORMAT2 lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
