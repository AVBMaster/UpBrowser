using System.Runtime.InteropServices;

namespace UpBrowser.Native.Windows;

public static class Imm32Interop
{
    private const string ImmDll = "imm32.dll";

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpComp, int dwCompLen, byte[]? lpRead, int dwReadLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    public const int GCS_COMPSTR = 0x0008;
    public const int GCS_CURSORPOS = 0x0010;
    public const int GCS_RESULTSTR = 0x0800;
    public const int GCS_COMPATTR = 0x0010;
    public const int GCS_COMPCLAUSE = 0x0020;

    public const int WM_IME_COMPOSITION = 0x010F;
    public const int WM_IME_STARTCOMPOSITION = 0x010D;
    public const int WM_IME_ENDCOMPOSITION = 0x010E;
    public const int WM_IME_NOTIFY = 0x0282;
    public const int WM_IME_CHAR = 0x0286;

    public const int IMN_OPENCANDIDATE = 0x0001;
    public const int IMN_CHANGECANDIDATE = 0x0002;
    public const int IMN_CLOSECANDIDATE = 0x0003;

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCompositionWindow(IntPtr hIMC, IntPtr lpCompForm);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCandidateWindow(IntPtr hIMC, IntPtr lpCandidate);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetCandidateCount(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmNotifyIME(IntPtr hIMC, int dwAction, int dwIndex, int dwValue);

    public const int NI_COMPOSITIONSTR = 0x0001;
    public const int CPS_COMPLETE = 0x0004;

    public const int CFS_FORCE_POSITION = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct COMPOSITIONFORM
    {
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CANDIDATEFORM
    {
        public int dwIndex;
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }
}