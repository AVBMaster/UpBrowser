using UpBrowser.Core;

namespace UpBrowser.Input;

public static class InputMethod
{
    private static IImeSupport? _currentTarget;
    private static bool _isComposing;
    private static string _compositionString = string.Empty;
    private static int _compositionCursor;

    public static IImeSupport? Current => _currentTarget;
    public static bool IsComposing => _isComposing;
    public static string CompositionString => _compositionString;
    public static int CompositionCursor => _compositionCursor;
    public static bool IsInputMethodEnabled { get; set; } = true;

    public static void SetTarget(IImeSupport? element)
    {
        if (_currentTarget != element)
        {
            if (_isComposing)
            {
                EndComposition();
            }
            _currentTarget = element;
        }
    }

    public static void StartComposition()
    {
        _isComposing = true;
        _compositionString = string.Empty;
        _compositionCursor = 0;
        CompositionStarted?.Invoke(null, EventArgs.Empty);
    }

    public static void UpdateComposition(string text, int cursor)
    {
        _compositionString = text;
        _compositionCursor = cursor;
        CompositionUpdated?.Invoke(null, new CompositionEventArgs(text, cursor));
    }

    public static void EndComposition(string? result = null)
    {
        var wasComposing = _isComposing;
        _isComposing = false;
        _compositionString = string.Empty;
        _compositionCursor = 0;

        if (wasComposing)
        {
            CompositionEnded?.Invoke(null, new CompositionResultEventArgs(result));
        }
    }

    public static void CancelComposition()
    {
        if (_isComposing)
        {
            EndComposition(null);
        }
    }

    public static event EventHandler? CompositionStarted;
    public static event EventHandler<CompositionEventArgs>? CompositionUpdated;
    public static event EventHandler<CompositionResultEventArgs>? CompositionEnded;
}

public class CompositionEventArgs : EventArgs
{
    public string Text { get; }
    public int CursorPosition { get; }

    public CompositionEventArgs(string text, int cursorPosition)
    {
        Text = text;
        CursorPosition = cursorPosition;
    }
}

public class CompositionResultEventArgs : EventArgs
{
    public string? Result { get; }
    public bool IsCancelled => Result == null;

    public CompositionResultEventArgs(string? result)
    {
        Result = result;
    }
}