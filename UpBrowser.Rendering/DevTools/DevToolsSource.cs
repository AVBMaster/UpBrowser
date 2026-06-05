using SkiaSharp;
using UpBrowser.Core;
using UpBrowser.Platform;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsSource : IImeSupport
{
    private string _html = "";
    private float _scrollOffset;
    private float _contentHeight;
    private string[] _lines = Array.Empty<string>();
    private float _viewHeight;
    private float _renderX, _renderY, _renderW, _renderH;

    private bool _thumbDragging;
    private float _thumbDragStartY;
    private float _thumbDragStartOffset;

    private bool _editing;
    private int _editLine;
    private int _editCol;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private bool _showCursor = true;
    private DateTime _lastBlink = DateTime.Now;

    // IME composition state
    private bool _isImeComposing;
    private string _imeCompositionString = "";

    public Action<string>? OnHtmlChanged;
    public Action<char>? OnImeChar;
    public Action? OnInputChanged;

    public bool IsEditing => _editing;
    public int EditLine => _editLine;
    public int EditCol => _editCol;
    public float ScrollOffset => _scrollOffset;

    public void SetHtml(string html) { _html = html ?? ""; _lines = _html.Split('\n'); }

    public bool HandleWheel(double delta)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + 18);
        _scrollOffset -= (float)delta * 3;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        return true;
    }

    public void SetScrollOffset(float offset)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + 18);
        _scrollOffset = Math.Max(0, Math.Min(offset, maxScroll));
    }

    public bool HandleThumbDragStart(float y)
    {
        if (_contentHeight <= _viewHeight) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float sy = _renderY + (_scrollOffset / maxScroll) * (_viewHeight - sh);
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
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float delta = (y - _thumbDragStartY) / Math.Max(1, _viewHeight - sh) * maxScroll;
        _scrollOffset = Math.Max(0, Math.Min(maxScroll, _thumbDragStartOffset + delta));
        return true;
    }

    public void HandleThumbDragEnd() { _thumbDragging = false; }

    public bool HandleClick(float x, float y)
    {
        float localY = y - _renderY;
        int clickedLine = (int)((localY + _scrollOffset) / 18);
        if (clickedLine >= 0 && clickedLine < _lines.Length)
        {
            _editing = true;
            _editLine = clickedLine;
            _selectionStart = -1;
            _selectionEnd = -1;

            float textStartX = _renderX + 50;
            float clickX = x - textStartX;
            string line = _lines[clickedLine];
            _editCol = 0;
            float currentX = 0;
            using var testPaint = new SKPaint { IsAntialias = true };
            using var testFont = FontHelper.CreateDevToolsFont(12);
            for (int i = 0; i < line.Length; i++)
            {
                float charWidth = testFont.MeasureText(line[i].ToString());
                if (currentX + charWidth / 2 >= clickX)
                    break;
                _editCol = i + 1;
                currentX += charWidth;
            }

            OnInputChanged?.Invoke();
            return true;
        }
        _editing = false;
        return false;
    }

    public void HandleImeChar(char c)
    {
        if (!_editing) return;
        if (_editLine < 0 || _editLine >= _lines.Length) return;

        if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
        {
            int selStart = Math.Min(_selectionStart, _selectionEnd);
            int selEnd = Math.Max(_selectionStart, _selectionEnd);
            string line = _lines[_editLine];
            _lines[_editLine] = line[..selStart] + line[selEnd..];
            _editCol = selStart;
            _selectionStart = -1;
            _selectionEnd = -1;
        }

        InsertAtCursor(c.ToString());
    }

    public string GetSelectedText()
    {
        if (!_editing || _editLine < 0 || _editLine >= _lines.Length) return "";
        if (_selectionStart < 0 || _selectionEnd <= _selectionStart) return "";
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        return _lines[_editLine][start..end];
    }

    private void InsertAtCursor(string text)
    {
        string line = _lines[_editLine];
        _lines[_editLine] = line[..Math.Min(_editCol, line.Length)] + text + line[Math.Min(_editCol, line.Length)..];
        _editCol += text.Length;
        _html = string.Join('\n', _lines);
        OnHtmlChanged?.Invoke(_html);
        OnInputChanged?.Invoke();
    }

    public bool HandleKeyPress(char keyChar, Key key)
    {
        if (!_editing) return false;
        if (_editLine < 0 || _editLine >= _lines.Length) return false;

        switch (key)
        {
            case Key.Enter:
                {
                    string line = _lines[_editLine];
                    string before = line[..Math.Min(_editCol, line.Length)];
                    string after = line[Math.Min(_editCol, line.Length)..];
                    _lines[_editLine] = before;
                    var newLines = _lines.ToList();
                    newLines.Insert(_editLine + 1, after);
                    _lines = newLines.ToArray();
                    _editLine++;
                    _editCol = 0;
                    _selectionStart = -1; _selectionEnd = -1;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Backspace:
                {
                    if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                    {
                        int selStart = Math.Min(_selectionStart, _selectionEnd);
                        int selEnd = Math.Max(_selectionStart, _selectionEnd);
                        string line = _lines[_editLine];
                        _lines[_editLine] = line[..selStart] + line[selEnd..];
                        _editCol = selStart;
                        _selectionStart = -1; _selectionEnd = -1;
                    }
                    else if (_editCol > 0)
                    {
                        string line = _lines[_editLine];
                        _lines[_editLine] = line[..(_editCol - 1)] + line[_editCol..];
                        _editCol--;
                    }
                    else if (_editLine > 0)
                    {
                        string currentLine = _lines[_editLine];
                        _editCol = _lines[_editLine - 1].Length;
                        _lines[_editLine - 1] += currentLine;
                        var list = _lines.ToList();
                        list.RemoveAt(_editLine);
                        _lines = list.ToArray();
                        _editLine--;
                    }
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Delete:
                {
                    if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                    {
                        int selStart = Math.Min(_selectionStart, _selectionEnd);
                        int selEnd = Math.Max(_selectionStart, _selectionEnd);
                        string line = _lines[_editLine];
                        _lines[_editLine] = line[..selStart] + line[selEnd..];
                        _editCol = selStart;
                        _selectionStart = -1; _selectionEnd = -1;
                    }
                    else
                    {
                        string line = _lines[_editLine];
                        if (_editCol < line.Length)
                            _lines[_editLine] = line[.._editCol] + line[(_editCol + 1)..];
                        else if (_editLine < _lines.Length - 1)
                        {
                            string nextLine = _lines[_editLine + 1];
                            _lines[_editLine] += nextLine;
                            var list = _lines.ToList();
                            list.RemoveAt(_editLine + 1);
                            _lines = list.ToArray();
                        }
                    }
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            case Key.Left:
                if (_editCol > 0) _editCol--;
                else if (_editLine > 0) { _editLine--; _editCol = _lines[_editLine].Length; }
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.Right:
                if (_editCol < _lines[_editLine].Length) _editCol++;
                else if (_editLine < _lines.Length - 1) { _editLine++; _editCol = 0; }
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.Up:
                if (_editLine > 0) { _editLine--; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.Down:
                if (_editLine < _lines.Length - 1) { _editLine++; _editCol = Math.Min(_editCol, _lines[_editLine].Length); }
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.Home:
                _editCol = 0;
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.End:
                _editCol = _lines[_editLine].Length;
                _selectionStart = -1; _selectionEnd = -1;
                return true;

            case Key.Tab:
                {
                    string line = _lines[_editLine];
                    _lines[_editLine] = line[..Math.Min(_editCol, line.Length)] + "    " + line[Math.Min(_editCol, line.Length)..];
                    _editCol += 4;
                    _selectionStart = -1; _selectionEnd = -1;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                    return true;
                }

            default:
                if (!char.IsControl(keyChar))
                {
                    if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
                    {
                        int selStart = Math.Min(_selectionStart, _selectionEnd);
                        int selEnd = Math.Max(_selectionStart, _selectionEnd);
                        string line = _lines[_editLine];
                        _lines[_editLine] = line[..selStart] + line[selEnd..];
                        _editCol = selStart;
                        _selectionStart = -1; _selectionEnd = -1;
                    }
                    string curLine = _lines[_editLine];
                    _lines[_editLine] = curLine[..Math.Min(_editCol, curLine.Length)] + keyChar + curLine[Math.Min(_editCol, curLine.Length)..];
                    _editCol++;
                    _html = string.Join('\n', _lines);
                    OnHtmlChanged?.Invoke(_html);
                }
                return true;
        }
    }

    public bool TickCursorBlink()
    {
        if (!_editing) return false;
        if ((DateTime.Now - _lastBlink).TotalMilliseconds > 500)
        {
            _showCursor = !_showCursor;
            _lastBlink = DateTime.Now;
            return true;
        }
        return false;
    }

    public void Render(SKCanvas canvas, float x, float y, float width, float height, DevToolsTheme theme)
    {
        _renderX = x; _renderY = y; _renderW = width; _renderH = height;
        _viewHeight = height;

        using var bg = new SKPaint { Color = theme.PanelBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bg);

        using var font = new SKPaint { IsAntialias = true };
        using var skFont = FontHelper.CreateDevToolsFont(12);

        using var tagPaint = new SKPaint { Color = theme.AccentBlue, IsAntialias = true };
        using var attrPaint = new SKPaint { Color = theme.AccentOrange, IsAntialias = true };
        using var strPaint = new SKPaint { Color = theme.AccentOrange, IsAntialias = true };
        using var commentPaint = new SKPaint { Color = theme.AccentGreen, IsAntialias = true };
        using var defPaint = new SKPaint { Color = theme.TextPrimary, IsAntialias = true };

        float lh = 18;
        float lnW = 50;
        _contentHeight = _lines.Length * lh;
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight + lh);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));

        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + width, y + height));

        float drawY = y + lh - _scrollOffset;
        for (int i = 0; i < _lines.Length; i++)
        {
            float lineDrawY = drawY;
            if (lineDrawY < y - lh || lineDrawY > y + height) { drawY += lh; continue; }

            if (_editing && i == _editLine)
            {
                using var hl = new SKPaint { Color = theme.LineHighlight, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + lnW, lineDrawY - lh + 4, width - lnW, lh, hl);
            }

            using var lineNumPaint = new SKPaint { Color = theme.LineNumberText, IsAntialias = true };
            string ln = (i + 1).ToString().PadLeft(4);
            canvas.DrawText(ln, x + 4, lineDrawY, SKTextAlign.Left, skFont, lineNumPaint);

            float tx = x + lnW;
            HighlightLine(canvas, _lines[i], tx, lineDrawY, width - lnW - 8, skFont, tagPaint, attrPaint, strPaint, commentPaint, defPaint);

            if (_editing && i == _editLine && _showCursor)
            {
                float cursorX = tx + skFont.MeasureText(_lines[i][..Math.Min(_editCol, _lines[i].Length)]);
                using var cp = new SKPaint { Color = theme.CursorColor, Style = SKPaintStyle.Fill, StrokeWidth = 1 };
                canvas.DrawLine(cursorX, lineDrawY - lh + 4, cursorX, lineDrawY + 2, cp);
            }

            drawY += lh;
        }

        canvas.Restore();

        if (_contentHeight > _viewHeight)
        {
            float sh = _viewHeight * _viewHeight / _contentHeight;
            float maxScrollBar = Math.Max(0, _contentHeight - _viewHeight);
            float sy = y + (maxScrollBar > 0 ? (_scrollOffset / maxScrollBar) * (_viewHeight - sh) : 0);
            using var sp = new SKPaint { Color = theme.ScrollbarThumb, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(x + width - 6, sy, 4, sh, 2, 2, sp);
        }
    }

    #region IImeSupport

    public Point GetImeCaretPosition()
    {
        if (!_editing || _editLine < 0 || _editLine >= _lines.Length)
            return new Point(_renderX + 50, _renderY);

        float tx = _renderX + 50;
        float lh = 18;
        string line = _lines[_editLine];
        string textBeforeCursor = line[..Math.Min(_editCol, line.Length)];
        using var testFont = FontHelper.CreateDevToolsFont(12);
        float cursorX = tx + testFont.MeasureText(textBeforeCursor);
        float lineDrawY = _renderY + lh - _scrollOffset + _editLine * lh;
        float cursorY = lineDrawY - lh + 4;
        return new Point(cursorX, cursorY);
    }

    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = "";
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _isImeComposing = true;
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = "";

        if (resultString != null && _editing && _editLine >= 0 && _editLine < _lines.Length)
        {
            if (_selectionStart >= 0 && _selectionEnd > _selectionStart)
            {
                int selStart = Math.Min(_selectionStart, _selectionEnd);
                int selEnd = Math.Max(_selectionStart, _selectionEnd);
                string sl = _lines[_editLine];
                _lines[_editLine] = sl[..selStart] + sl[selEnd..];
                _editCol = selStart;
                _selectionStart = -1;
                _selectionEnd = -1;
            }
            InsertAtCursor(resultString);
        }
    }

    #endregion

    private void HighlightLine(SKCanvas canvas, string line, float x, float y, float maxW,
        SKFont font, SKPaint tag, SKPaint attr, SKPaint str, SKPaint comment, SKPaint def)
    {
        if (string.IsNullOrEmpty(line)) return;

        float cx = x;
        int i = 0;
        while (i < line.Length && cx < x + maxW)
        {
            if (i + 3 < line.Length && line[i] == '<' && line[i + 1] == '!' && line[i + 2] == '-' && line[i + 3] == '-')
            {
                int end = line.IndexOf("-->", i + 4);
                if (end < 0) end = line.Length - 1; else end += 3;
                string c = line[i..(Math.Min(end + 1, line.Length))];
                comment.Color = SKColor.Parse("#6A9955");
                canvas.DrawText(c, cx, y, SKTextAlign.Left, font, comment);
                cx += font.MeasureText(c);
                i = end + 1;
                continue;
            }

            if (line[i] == '<')
            {
                int end = line.IndexOf('>', i + 1);
                if (end < 0) end = line.Length - 1;

                int ne = end;
                for (int j = i + 1; j < line.Length; j++)
                {
                    if (char.IsWhiteSpace(line[j]) || line[j] == '>') { ne = j; break; }
                }
                string tn = line[i..(Math.Min(ne + 1, line.Length))];
                tag.Color = SKColor.Parse("#569CD6");
                canvas.DrawText(tn, cx, y, SKTextAlign.Left, font, tag);
                cx += font.MeasureText(tn);
                i = ne;

                while (i < end)
                {
                    while (i < end && char.IsWhiteSpace(line[i])) i++;
                    if (i >= end) break;

                    int ae = i;
                    while (ae < end && line[ae] != '=' && !char.IsWhiteSpace(line[ae])) ae++;
                    if (ae > i)
                    {
                        attr.Color = SKColor.Parse("#CE9178");
                        canvas.DrawText(line[i..ae], cx, y, SKTextAlign.Left, font, attr);
                        cx += font.MeasureText(line[i..ae]);
                        i = ae;
                    }

                    if (i < end && line[i] == '=')
                    {
                        def.Color = SKColor.Parse("#D4D4D4");
                        canvas.DrawText("=", cx, y, SKTextAlign.Left, font, def);
                        cx += font.MeasureText("=");
                        i++;
                        if (i < end && (line[i] == '"' || line[i] == '\''))
                        {
                            char q = line[i];
                            int ve = line.IndexOf(q, i + 1);
                            if (ve < 0 || ve > end) ve = end;
                            string v = line[i..(Math.Min(ve + 1, line.Length))];
                            str.Color = SKColor.Parse("#CE9178");
                            canvas.DrawText(v, cx, y, SKTextAlign.Left, font, str);
                            cx += font.MeasureText(v);
                            i = ve + 1;
                        }
                    }
                }

                if (end < line.Length && line[end] == '>' && ne < end)
                {
                    tag.Color = SKColor.Parse("#569CD6");
                    canvas.DrawText(">", cx, y, SKTextAlign.Left, font, tag);
                    cx += font.MeasureText(">");
                }
                i = end + 1;
            }
            else
            {
                int nt = line.IndexOf('<', i + 1);
                if (nt < 0) nt = line.Length;
                def.Color = SKColor.Parse("#D4D4D4");
                canvas.DrawText(line[i..nt], cx, y, SKTextAlign.Left, font, def);
                cx += font.MeasureText(line[i..nt]);
                i = nt;
            }
        }
    }
}
