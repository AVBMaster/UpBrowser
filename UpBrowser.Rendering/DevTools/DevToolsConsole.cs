using SkiaSharp;
using UpBrowser.Core;
using UpBrowser.Core.JavaScript;
using UpBrowser.Platform;
using System.Text;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsConsole : IImeSupport
{
    private readonly List<string> _outputLines = new();
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _inputText = "";
    private int _cursorPos;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private bool _showCursor = true;
    private DateTime _lastBlink = DateTime.Now;
    private JavaScriptEngine? _jsEngine;
    private float _scrollOffset;
    private float _contentHeight;
    private float _viewHeight;
    private const float LineHeight = 18;
    private const float PaddingX = 8;
    private const float InputHeight = 24;
    private float _renderX, _renderY, _renderW, _renderH;

    private readonly SKPaint _font = new SKPaint { IsAntialias = true };
    private readonly SKFont _skFont = FontHelper.CreateDevToolsFont(12);

    private bool _thumbDragging;
    private float _thumbDragStartY;
    private float _thumbDragStartOffset;

    // IME composition state
    private bool _isImeComposing;
    private string _imeCompositionString = "";
    private int _imeCompositionCursorPos;

    public Action<char>? OnImeChar { get; set; }
    public Action? OnInputChanged { get; set; }

    public float InputFieldY => _renderY + _renderH - InputHeight;

    public DevToolsConsole()
    {
        _outputLines.Add("UpBrowser DevTools Console v1.0");
        _outputLines.Add("Type JavaScript and press Enter to execute.");
        _outputLines.Add("");
    }

    public void SetJavaScriptEngine(JavaScriptEngine? engine) { _jsEngine = engine; }

    public void AppendOutput(string text)
    {
        _outputLines.Add(text);
        _scrollOffset = float.MaxValue;
    }

    public bool TickCursorBlink()
    {
        if ((DateTime.Now - _lastBlink).TotalMilliseconds > 500)
        {
            _showCursor = !_showCursor;
            _lastBlink = DateTime.Now;
            return true;
        }
        return false;
    }

    public bool HandleWheel(double delta)
    {
        float outputHeight = _viewHeight - InputHeight;
        float maxScroll = Math.Max(0, _contentHeight - outputHeight + LineHeight);
        _scrollOffset -= (float)delta * 3;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        return true;
    }

    public void SetScrollOffset(float offset)
    {
        float outputHeight = _viewHeight - InputHeight;
        float maxScroll = Math.Max(0, _contentHeight - outputHeight + LineHeight);
        _scrollOffset = Math.Max(0, Math.Min(offset, maxScroll));
    }

    public bool HandleThumbDragStart(float y)
    {
        float outputHeight = _viewHeight - InputHeight;
        if (_contentHeight <= outputHeight) return false;
        float sh = outputHeight * outputHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - outputHeight);
        if (maxScroll <= 0) return false;
        float sy = _renderY + (_scrollOffset / maxScroll) * (outputHeight - sh);
        if (y >= sy && y <= sy + sh)
        {
            _thumbDragging = true;
            _thumbDragStartY = y;
            _thumbDragStartOffset = _scrollOffset;
            return true;
        }
        return false;
    }

    public bool HandleThumbDrag(float y)
    {
        if (!_thumbDragging) return false;
        float outputHeight = _viewHeight - InputHeight;
        float sh = outputHeight * outputHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - outputHeight);
        if (maxScroll <= 0) return false;
        float delta = (y - _thumbDragStartY) / Math.Max(1, outputHeight - sh) * maxScroll;
        _scrollOffset = Math.Max(0, Math.Min(maxScroll, _thumbDragStartOffset + delta));
        return true;
    }

    public void HandleThumbDragEnd() { _thumbDragging = false; }

    public bool HandleClick(float x, float y)
    {
        float inputY = _renderY + _renderH - InputHeight;
        if (y >= inputY && y <= inputY + InputHeight)
        {
            float textStartX = _renderX + PaddingX + _skFont.MeasureText("> ");
            float clickX = x - textStartX;
            _selectionStart = -1;
            _selectionEnd = -1;

            float currentX = 0;
            for (int i = 0; i <= _inputText.Length; i++)
            {
                float charWidth = i < _inputText.Length ? _skFont.MeasureText(_inputText[i].ToString()) : _skFont.MeasureText(" ");
                if (currentX + charWidth / 2 >= clickX)
                {
                    _cursorPos = i;
                    break;
                }
                currentX += charWidth;
            }
            OnInputChanged?.Invoke();
            return true;
        }
        return false;
    }

    public void HandleImeChar(char c)
    {
        if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
        {
            int selStart = Math.Min(_selectionStart, _selectionEnd);
            int selEnd = Math.Max(_selectionStart, _selectionEnd);
            _inputText = _inputText[..selStart] + _inputText[selEnd..];
            _cursorPos = selStart;
            _selectionStart = -1;
            _selectionEnd = -1;
        }
        _inputText = _inputText[.._cursorPos] + c + _inputText[_cursorPos..];
        _cursorPos++;
        OnInputChanged?.Invoke();
    }

    public string GetSelectedText()
    {
        if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
        {
            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);
            return _inputText[start..end];
        }
        return "";
    }

    public void Render(SKCanvas canvas, float x, float y, float width, float height, DevToolsTheme theme)
    {
        _renderX = x; _renderY = y; _renderW = width; _renderH = height;
        _viewHeight = height;

        using var bg = new SKPaint { Color = theme.PanelBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bg);

        float inputY = y + height - InputHeight;
        float outputHeight = inputY - y;

        _contentHeight = 0;
        foreach (var line in _outputLines)
            _contentHeight += (int)Math.Ceiling(_skFont.MeasureText(line) / Math.Max(1, width - PaddingX * 2)) * LineHeight;

        float maxScroll = Math.Max(0, _contentHeight - outputHeight + LineHeight);
        if (_scrollOffset == float.MaxValue) _scrollOffset = maxScroll;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        maxScroll = Math.Max(0, _contentHeight - outputHeight);

        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + width, y + outputHeight));
        float dy = y + LineHeight - _scrollOffset;
        foreach (var line in _outputLines)
            DrawColored(canvas, line, x + PaddingX, ref dy, width - PaddingX * 2, theme);
        canvas.Restore();

        DrawScrollbar(canvas, x, y, outputHeight, theme);

        using var inputBg = new SKPaint { Color = theme.InputBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, inputY, width, InputHeight, inputBg);

        _font.Color = theme.AccentBlue;
        canvas.DrawText("> ", x + PaddingX, inputY + InputHeight * 0.7f, SKTextAlign.Left, _skFont, _font);
        float pw = _skFont.MeasureText("> ");

        _font.Color = theme.TextPrimary;
        string displayText = _inputText;
        float textW = _skFont.MeasureText(displayText);
        float textAreaW = width - PaddingX * 2 - pw - 6;
        float textOffsetX = 0;
        float cursorVisualPos = _skFont.MeasureText(displayText[..Math.Min(_cursorPos, displayText.Length)]);
        if (cursorVisualPos > textAreaW)
            textOffsetX = textAreaW - cursorVisualPos;
        else if (cursorVisualPos < 0)
            textOffsetX = 0;

        canvas.Save();
        canvas.ClipRect(new SKRect(x + PaddingX + pw, inputY, x + PaddingX + pw + textAreaW, inputY + InputHeight));

        if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
        {
            int selStart = Math.Min(_selectionStart, _selectionEnd);
            int selEnd = Math.Max(_selectionStart, _selectionEnd);
            float selStartX = _skFont.MeasureText(displayText[..Math.Min(selStart, displayText.Length)]);
            float selEndX = _skFont.MeasureText(displayText[..Math.Min(selEnd, displayText.Length)]);
            using var selBg = new SKPaint { Color = theme.SelectionBg, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x + PaddingX + pw + textOffsetX + selStartX, inputY + 2, selEndX - selStartX, InputHeight - 4, selBg);
        }

        canvas.DrawText(displayText, x + PaddingX + pw + textOffsetX, inputY + InputHeight * 0.7f, SKTextAlign.Left, _skFont, _font);
        canvas.Restore();

        if ((DateTime.Now - _lastBlink).TotalMilliseconds > 500) { _showCursor = !_showCursor; _lastBlink = DateTime.Now; }
        if (_showCursor)
        {
            float cursorX = x + PaddingX + pw + textOffsetX + cursorVisualPos;
            using var cursorPaint = new SKPaint { Color = theme.CursorColor, Style = SKPaintStyle.Fill, StrokeWidth = 1 };
            canvas.DrawLine(cursorX, inputY + 4, cursorX, inputY + InputHeight - 4, cursorPaint);
        }
    }

    private void DrawScrollbar(SKCanvas canvas, float x, float y, float outputHeight, DevToolsTheme theme)
    {
        float maxScroll = Math.Max(0, _contentHeight - outputHeight);
        if (_contentHeight > outputHeight && maxScroll > 0)
        {
            float sh = outputHeight * outputHeight / _contentHeight;
            float sy = y + (_scrollOffset / maxScroll) * (outputHeight - sh);
            using var sp = new SKPaint { Color = theme.ScrollbarThumb, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(x + _renderW - 6, sy, 4, sh, 2, 2, sp);
        }
    }

    public bool HandleKeyPress(char keyChar, Key key)
    {
        bool ctrlPressed = false;

        switch (key)
        {
            case Key.Enter:
                ExecuteInput();
                return true;

            case Key.Backspace:
                if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                {
                    int selStart = Math.Min(_selectionStart, _selectionEnd);
                    int selEnd = Math.Max(_selectionStart, _selectionEnd);
                    _inputText = _inputText[..selStart] + _inputText[selEnd..];
                    _cursorPos = selStart;
                    _selectionStart = -1;
                    _selectionEnd = -1;
                    OnInputChanged?.Invoke();
                }
                else if (_cursorPos > 0)
                {
                    _inputText = _inputText[..(_cursorPos - 1)] + _inputText[_cursorPos..];
                    _cursorPos--;
                    OnInputChanged?.Invoke();
                }
                return true;

            case Key.Delete:
                if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                {
                    int selStart = Math.Min(_selectionStart, _selectionEnd);
                    int selEnd = Math.Max(_selectionStart, _selectionEnd);
                    _inputText = _inputText[..selStart] + _inputText[selEnd..];
                    _cursorPos = selStart;
                    _selectionStart = -1;
                    _selectionEnd = -1;
                    OnInputChanged?.Invoke();
                }
                else if (_cursorPos < _inputText.Length)
                    _inputText = _inputText[.._cursorPos] + _inputText[(_cursorPos + 1)..];
                return true;

            case Key.Left:
                if (ctrlPressed)
                {
                    while (_cursorPos > 0 && !char.IsWhiteSpace(_inputText[_cursorPos - 1])) _cursorPos--;
                }
                else if (_cursorPos > 0) _cursorPos--;
                _selectionStart = -1;
                _selectionEnd = -1;
                OnInputChanged?.Invoke();
                return true;

            case Key.Right:
                if (ctrlPressed)
                {
                    while (_cursorPos < _inputText.Length && !char.IsWhiteSpace(_inputText[_cursorPos])) _cursorPos++;
                }
                else if (_cursorPos < _inputText.Length) _cursorPos++;
                _selectionStart = -1;
                _selectionEnd = -1;
                OnInputChanged?.Invoke();
                return true;

            case Key.Home:
                _cursorPos = 0;
                _selectionStart = -1;
                _selectionEnd = -1;
                OnInputChanged?.Invoke();
                return true;

            case Key.End:
                _cursorPos = _inputText.Length;
                _selectionStart = -1;
                _selectionEnd = -1;
                OnInputChanged?.Invoke();
                return true;

            case Key.Up:
                if (_commandHistory.Count > 0)
                {
                    if (_historyIndex < 0) _historyIndex = _commandHistory.Count - 1;
                    else if (_historyIndex > 0) _historyIndex--;
                    _inputText = _commandHistory[_historyIndex];
                    _cursorPos = _inputText.Length;
                    _selectionStart = -1;
                    _selectionEnd = -1;
                    OnInputChanged?.Invoke();
                }
                return true;

            case Key.Down:
                if (_commandHistory.Count > 0 && _historyIndex >= 0)
                {
                    if (_historyIndex < _commandHistory.Count - 1)
                    {
                        _historyIndex++;
                        _inputText = _commandHistory[_historyIndex];
                    }
                    else
                    {
                        _historyIndex = -1;
                        _inputText = "";
                    }
                    _cursorPos = _inputText.Length;
                    _selectionStart = -1;
                    _selectionEnd = -1;
                    OnInputChanged?.Invoke();
                }
                return true;

            default:
                if (!char.IsControl(keyChar))
                {
                    if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                    {
                        int selStart = Math.Min(_selectionStart, _selectionEnd);
                        int selEnd = Math.Max(_selectionStart, _selectionEnd);
                        _inputText = _inputText[..selStart] + _inputText[selEnd..];
                        _cursorPos = selStart;
                        _selectionStart = -1;
                        _selectionEnd = -1;
                    }
                    _inputText = _inputText[.._cursorPos] + keyChar + _inputText[_cursorPos..];
                    _cursorPos++;
                    OnInputChanged?.Invoke();
                }
                return true;
        }
    }

    private void ExecuteInput()
    {
        var cmd = _inputText.Trim();
        _outputLines.Add($"> {cmd}");

        if (!string.IsNullOrEmpty(cmd))
        {
            _commandHistory.Add(cmd);
        }
        _historyIndex = -1;
        _selectionStart = -1;
        _selectionEnd = -1;

        if (string.IsNullOrEmpty(cmd)) { _inputText = ""; _cursorPos = 0; _scrollOffset = float.MaxValue; OnInputChanged?.Invoke(); return; }

        if (cmd == "clear" || cmd == "cls")
        {
            _outputLines.Clear();
            _inputText = "";
            _cursorPos = 0;
            _scrollOffset = float.MaxValue;
            OnInputChanged?.Invoke();
            return;
        }

        if (_jsEngine != null)
        {
            try
            {
                var result = _jsEngine.Evaluate(cmd);
                _outputLines.Add(result switch
                {
                    null => "undefined",
                    string s => $"\"{s}\"",
                    _ => result.ToString() ?? "undefined"
                });
            }
            catch (Exception ex) { _outputLines.Add($"Error: {ex.Message}"); }
        }
        else _outputLines.Add("Error: JS engine not available");

        _inputText = ""; _cursorPos = 0;
        _scrollOffset = float.MaxValue;
        OnInputChanged?.Invoke();
    }

    private void DrawColored(SKCanvas canvas, string text, float x, ref float y, float maxW, DevToolsTheme theme)
    {
        if (string.IsNullOrEmpty(text)) { y += LineHeight; return; }
        if (_skFont.MeasureText(text) <= maxW) { DrawOne(canvas, text, x, y, theme); y += LineHeight; return; }

        int cpl = Math.Max(1, (int)(maxW / (_skFont.MeasureText("W") * 0.6f)));
        for (int p = 0; p < text.Length; p += cpl)
        {
            int len = Math.Min(cpl, text.Length - p);
            DrawOne(canvas, text.Substring(p, len), x, y, theme);
            y += LineHeight;
        }
    }

    private void DrawOne(SKCanvas canvas, string text, float x, float y, DevToolsTheme theme)
    {
        if (text.StartsWith("> ")) { _font.Color = theme.AccentBlue; canvas.DrawText("> ", x, y, SKTextAlign.Left, _skFont, _font); _font.Color = theme.TextPrimary; canvas.DrawText(text[2..], x + _skFont.MeasureText("> "), y, SKTextAlign.Left, _skFont, _font); }
        else if (text.StartsWith("Error:") || text.StartsWith("[JS Error]")) { _font.Color = theme.AccentRed; canvas.DrawText(text, x, y, SKTextAlign.Left, _skFont, _font); }
        else if (text == "true" || text == "false" || text == "undefined") { _font.Color = theme.AccentBlue; canvas.DrawText(text, x, y, SKTextAlign.Left, _skFont, _font); }
        else if (text.StartsWith("\"") && text.EndsWith("\"")) { _font.Color = theme.AccentOrange; canvas.DrawText(text, x, y, SKTextAlign.Left, _skFont, _font); }
        else if (int.TryParse(text, out _) || float.TryParse(text, out _)) { _font.Color = theme.AccentGreen; canvas.DrawText(text, x, y, SKTextAlign.Left, _skFont, _font); }
        else { _font.Color = theme.TextPrimary; canvas.DrawText(text, x, y, SKTextAlign.Left, _skFont, _font); }
    }

    #region IImeSupport

    public Point GetImeCaretPosition()
    {
        float inputY = _renderY + _renderH - InputHeight;
        float pw = _skFont.MeasureText("> ");
        string displayText = _inputText;
        float textAreaW = _renderW - PaddingX * 2 - pw - 6;
        float textOffsetX = 0;
        float cursorVisualPos = _skFont.MeasureText(displayText[..Math.Min(_cursorPos, displayText.Length)]);
        if (cursorVisualPos > textAreaW)
            textOffsetX = textAreaW - cursorVisualPos;
        else if (cursorVisualPos < 0)
            textOffsetX = 0;

        float cursorX = _renderX + PaddingX + pw + textOffsetX + cursorVisualPos;
        float cursorY = inputY + 4;
        return new Point(cursorX, cursorY);
    }

    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = "";
        _imeCompositionCursorPos = 0;
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursorPos = cursorPosition;
        _isImeComposing = true;
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = "";

        if (resultString != null)
        {
            if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
            {
                int selStart = Math.Min(_selectionStart, _selectionEnd);
                int selEnd = Math.Max(_selectionStart, _selectionEnd);
                _inputText = _inputText[..selStart] + _inputText[selEnd..];
                _cursorPos = selStart;
                _selectionStart = -1;
                _selectionEnd = -1;
            }
            _inputText = _inputText[.._cursorPos] + resultString + _inputText[_cursorPos..];
            _cursorPos += resultString.Length;
            OnInputChanged?.Invoke();
        }
    }

    #endregion
}
