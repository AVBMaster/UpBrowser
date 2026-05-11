using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Platform;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsElements
{
    private Document? _document;
    private float _scrollOffset;
    private float _contentHeight;
    private Element? _selectedElement;
    private float _viewHeight;
    private float _renderX, _renderY, _renderW, _renderH;

    private readonly SKPaint _font = FontHelper.CreateMonoPaint(12);
    private readonly SKFont _skFont = FontHelper.CreateMonoFont(12);

    private bool _thumbDragging;
    private float _thumbDragStartY;
    private float _thumbDragStartOffset;

    public void SetDocument(Document? document) { _document = document; _selectedElement = null; _scrollOffset = 0; }

    public bool HandleWheel(double delta)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        _scrollOffset -= (float)delta * 3;
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        return true;
    }

    public void SetScrollOffset(float offset)
    {
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        _scrollOffset = Math.Max(0, Math.Min(offset, maxScroll));
    }

    public bool HandleThumbDragStart(float y)
    {
        if (_contentHeight <= _viewHeight) return false;
        float sh = _viewHeight * _viewHeight / Math.Max(1, _contentHeight);
        float maxScroll = Math.Max(0, _contentHeight - _viewHeight);
        if (maxScroll <= 0) return false;
        float sy = _renderY + (maxScroll > 0 ? (_scrollOffset / maxScroll) * (_viewHeight - sh) : 0);
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

    public void Render(SKCanvas canvas, float x, float y, float width, float height)
    {
        _renderX = x; _renderY = y; _renderW = width; _renderH = height;
        _viewHeight = height;

        using var bg = new SKPaint { Color = SKColor.Parse("#1E1E1E"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bg);

        float dy = y + 16;
        float startY = dy;

        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + width, y + height));

        var rootEl = _document?.DocumentElement ?? _document?.Body;
        if (rootEl != null)
        {
            _font.Color = SKColor.Parse("#569CD6");
            string rootTag = rootEl.TagName.ToLowerInvariant();
            canvas.DrawText($"#{rootTag}", x + 4, dy - _scrollOffset, SKTextAlign.Left, _skFont, _font);
            dy += 18;
            RenderTree(canvas, rootEl, 1, x + 4, ref dy, width - 8);
        }
        else if (_document != null)
        {
            if (_document.Children.Count > 0)
            {
                foreach (var child in _document.Children)
                {
                    if (child is Element ce)
                    {
                        float adjustY = dy - _scrollOffset;
                        if (adjustY + 18 >= y && adjustY <= y + height)
                        {
                            _font.Color = SKColor.Parse("#569CD6");
                            string tn = ce.TagName.ToLowerInvariant();
                            canvas.DrawText(tn, x + 4, adjustY, SKTextAlign.Left, _skFont, _font);
                        }
                        dy += 18;
                        RenderTree(canvas, ce, 1, x + 4, ref dy, width - 8);
                    }
                }
            }
            else
            {
            _font.Color = SKColor.Parse("#D4D4D4");
            canvas.DrawText("(empty document)", x + 4, dy - _scrollOffset, SKTextAlign.Left, _skFont, _font);
            dy += 18;
            }
        }
        else
        {
            _font.Color = SKColor.Parse("#D4D4D4");
            canvas.DrawText("(no document loaded)", x + 4, dy - _scrollOffset, SKTextAlign.Left, _skFont, _font);
            dy += 18;
        }

        _contentHeight = dy - y;
        canvas.Restore();

        float maxScroll = Math.Max(0, _contentHeight - height);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));

        if (_contentHeight > height)
        {
            float sh = height * height / Math.Max(1, _contentHeight);
            float sy = y + (maxScroll > 0 ? (_scrollOffset / maxScroll) * (height - sh) : 0);
            using var sp = new SKPaint { Color = new SKColor(80, 80, 80), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(x + width - 6, sy, 4, sh, 2, 2, sp);
        }

        if (_selectedElement != null)
        {
            float iy = y + height - 60;
            using var ibg = new SKPaint { Color = SKColor.Parse("#252526"), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, iy, width, 60, ibg);
            using var ifont = FontHelper.CreateMonoPaint(11);
            using var ifontFont = FontHelper.CreateMonoFont(11);

            ifont.Color = SKColor.Parse("#569CD6");
            canvas.DrawText($"<{_selectedElement.TagName.ToLowerInvariant()}>", x + 4, iy + 16, SKTextAlign.Left, ifontFont, ifont);

            var cs = _selectedElement.ComputedStyle;
            if (cs != null)
            {
                ifont.Color = SKColor.Parse("#D4D4D4");
                canvas.DrawText($"font-size: {cs.FontSize}px  color: #{cs.Color.Red:X2}{cs.Color.Green:X2}{cs.Color.Blue:X2}", x + 4, iy + 32, SKTextAlign.Left, ifontFont, ifont);
                canvas.DrawText($"display: {cs.Display}  position: {cs.Position}", x + 4, iy + 48, SKTextAlign.Left, ifontFont, ifont);
            }
        }
    }

    private void RenderTree(SKCanvas canvas, Element el, int depth, float x, ref float y, float maxW)
    {
        float indent = depth * 16;
        float lx = x + indent;
        float lineY = y - _scrollOffset;

        bool visible = lineY + 18 >= _renderY && lineY <= _renderY + _renderH;

        if (visible)
        {
            if (el == _selectedElement)
            {
                using var sbg = new SKPaint { Color = SKColor.Parse("#264F78"), Style = SKPaintStyle.Fill };
                canvas.DrawRect(x, lineY - 2, maxW, 18, sbg);
            }

            bool hasKids = el.Children.Any(c => c is Element);
            string marker = hasKids ? "▼ " : "  ";

            _font.Color = SKColor.Parse("#808080");
            canvas.DrawText(marker, lx, lineY, SKTextAlign.Left, _skFont, _font);
            float mw = _skFont.MeasureText(marker);

            _font.Color = SKColor.Parse("#569CD6");
            string tn = el.TagName.ToLowerInvariant();
            canvas.DrawText(tn, lx + mw, lineY, SKTextAlign.Left, _skFont, _font);
            float tw = _skFont.MeasureText(tn);

            float ax = lx + mw + tw;
            if (!string.IsNullOrEmpty(el.Id))
            {
                _font.Color = SKColor.Parse("#CE9178");
                canvas.DrawText($"#{el.Id}", ax, lineY, SKTextAlign.Left, _skFont, _font);
                ax += _skFont.MeasureText($"#{el.Id}");
            }

            var cn = el.ClassName;
            if (!string.IsNullOrEmpty(cn))
            {
                _font.Color = SKColor.Parse("#D7BA7D");
                canvas.DrawText($".{cn.Replace(' ', '.')}", ax, lineY, SKTextAlign.Left, _skFont, _font);
            }
        }

        y += 18;

        bool hasKids2 = el.Children.Any(c => c is Element);
        if (hasKids2)
        {
            foreach (var child in el.Children)
            {
                if (child is Element ce) RenderTree(canvas, ce, depth + 1, x, ref y, maxW);
                else if (child is TextNode tn2 && !tn2.IsWhitespaceOnly)
                {
                    float textLineY = y - _scrollOffset;
                    if (textLineY + 16 >= _renderY && textLineY <= _renderY + _renderH)
                    {
                        string t = tn2.TextContent?.Trim() ?? "";
                        if (t.Length > 80) t = t[..80] + "...";
                        _font.Color = SKColor.Parse("#D4D4D4");
                        canvas.DrawText($"\"{t}\"", x + (depth + 1) * 16, textLineY, SKTextAlign.Left, _skFont, _font);
                    }
                    y += 16;
                }
            }
        }
    }
}
