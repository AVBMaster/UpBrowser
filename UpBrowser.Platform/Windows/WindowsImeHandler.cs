using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using UpBrowser.Native.Windows;

namespace UpBrowser.Platform.Windows;

public class WindowsImeHandler : IImeHandler
{
    private readonly IntPtr _hwnd;
    private readonly ImeCompositionState _compositionState = new();
    private SKPoint _caretPosition;
    private float _lineHeight;
    private bool _compositionActive;

    public WindowsImeHandler(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public ImeCompositionState GetCompositionState()
    {
        ReadCurrentComposition();
        return _compositionState;
    }

    public void SetCaretPosition(SKPoint screenPosition, float lineHeight)
    {
        _caretPosition = screenPosition;
        _lineHeight = lineHeight;

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            var compForm = new Imm32Interop.COMPOSITIONFORM
            {
                dwStyle = Imm32Interop.CFS_FORCE_POSITION,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = (int)screenPosition.X,
                    Y = (int)(screenPosition.Y + lineHeight)
                },
                rcArea = new Imm32Interop.RECT
                {
                    Left = (int)screenPosition.X,
                    Top = (int)screenPosition.Y,
                    Right = (int)screenPosition.X + 200,
                    Bottom = (int)(screenPosition.Y + lineHeight * 2)
                }
            };

            int size = Marshal.SizeOf(compForm);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(compForm, ptr, false);
                Imm32Interop.ImmSetCompositionWindow(hIMC, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    public void Reset()
    {
        _compositionState.Reset();
        _compositionActive = false;

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC != IntPtr.Zero)
        {
            try
            {
                Imm32Interop.ImmNotifyIME(hIMC, Imm32Interop.NI_COMPOSITIONSTR, 0, 0);
            }
            finally
            {
                Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
            }
        }
    }

    public void Dispose()
    {
        Reset();
    }

    public bool IsComposing => _compositionActive;

    private void ReadCurrentComposition()
    {
        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            int compLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_COMPSTR, null, 0);
            if (compLen > 0)
            {
                byte[] buffer = new byte[compLen];
                int read = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_COMPSTR, buffer, compLen);
                if (read > 0)
                {
                    _compositionState.CompositionText = Encoding.Unicode.GetString(buffer, 0, read);
                    _compositionActive = true;
                }
            }
            else
            {
                _compositionState.CompositionText = string.Empty;
                _compositionActive = false;
            }

            int cursorPosLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_CURSORPOS, null, 0);
            if (cursorPosLen >= 4)
            {
                byte[] buf = new byte[4];
                Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_CURSORPOS, buf, 4);
                _compositionState.CursorPosition = BitConverter.ToInt32(buf, 0);
            }
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }
}