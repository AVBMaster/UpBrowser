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
    public static extern bool ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpComp, int dwCompLen, byte[]? lpRead, int dwReadLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    public const int GCS_COMPSTR = 0x0008;
    public const int GCS_CURSORPOS = 0x0010;
    public const int GCS_RESULTSTR = 0x0800;
}