using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Rendering;

/// <summary>
/// Paints ::selection highlights on text runs. Mirrors highlight_painter.cc.
/// Computes the highlight rect for each text run and emits a DrawRectOp.
/// </summary>
internal sealed class HighlightPainter
{
    private readonly DisplayList _displayList;
    private TextNode? _selAnchorNode;
    private int _selAnchorOffset;
    private TextNode? _selFocusNode;
    private int _selFocusOffset;
    private bool _hasSelection;
    private bool _selStartIsAnchor;

    public HighlightPainter(DisplayList displayList)
    {
        _displayList = displayList;
    }

    public void SetSelectionRange(TextNode? anchorNode, int anchorOffset,
        TextNode? focusNode, int focusOffset)
    {
        _selAnchorNode = anchorNode;
        _selAnchorOffset = anchorOffset;
        _selFocusNode = focusNode;
        _selFocusOffset = focusOffset;
        _hasSelection = anchorNode != null && focusNode != null;
        if (_hasSelection)
        {
            int cmp = CompareDomPosition(anchorNode!, focusNode!);
            _selStartIsAnchor = cmp <= 0;
        }
    }

    public void PaintHighlight(TextNode? runNode, string runText, SKRect runBounds,
        float fontSize, string fontFamily, FontWeight fontWeight, int runStartOffset = 0)
    {
        var rect = ComputeHighlightRect(runNode, runText, runBounds,
            fontSize, fontFamily, fontWeight, runStartOffset);
        if (rect == null)
            return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect.Value;
        op.FillColor = new SKColor(0x1A, 0x73, 0xE8, 0x40);
        op.BorderTopWidth = 0;
        op.BorderBottomWidth = 0;
        op.BorderLeftWidth = 0;
        op.BorderRightWidth = 0;
        op.Bounds = rect.Value;
        _displayList.Add(op);
    }

    private SKRect? ComputeHighlightRect(TextNode? runNode, string runText, SKRect runBounds,
        float fontSize, string fontFamily, FontWeight fontWeight, int runStartOffset)
    {
        if (!_hasSelection || runNode == null) return null;

        int startOff, endOff;

        if (_selAnchorNode == _selFocusNode)
        {
            if (runNode != _selAnchorNode) return null;
            startOff = Math.Min(_selAnchorOffset, _selFocusOffset);
            endOff = Math.Max(_selAnchorOffset, _selFocusOffset);
        }
        else
        {
            TextNode? startNode, endNode;
            if (_selStartIsAnchor)
            {
                startNode = _selAnchorNode;
                startOff = _selAnchorOffset;
                endNode = _selFocusNode;
                endOff = _selFocusOffset;
            }
            else
            {
                startNode = _selFocusNode;
                startOff = _selFocusOffset;
                endNode = _selAnchorNode;
                endOff = _selAnchorOffset;
            }

            if (runNode == startNode)
            {
                int localStart = Math.Max(0, startOff - runStartOffset);
                if (localStart >= runText.Length) return null;
                return GetSubRunBounds(runText, runBounds, localStart, runText.Length, fontSize, fontFamily, fontWeight);
            }
            if (runNode == endNode)
            {
                int localEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
                if (localEnd <= 0) return null;
                return GetSubRunBounds(runText, runBounds, 0, localEnd, fontSize, fontFamily, fontWeight);
            }
            if (IsNodeBetween(runNode, startNode, endNode))
                return runBounds;
            return null;
        }

        int snLocalStart = Math.Max(0, startOff - runStartOffset);
        int snLocalEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
        if (snLocalStart >= snLocalEnd) return null;
        return GetSubRunBounds(runText, runBounds, snLocalStart, snLocalEnd, fontSize, fontFamily, fontWeight);
    }

    private static SKRect? GetSubRunBounds(string text, SKRect runBounds, int startOff, int endOff,
        float fontSize, string fontFamily, FontWeight fontWeight)
    {
        if (startOff >= endOff || string.IsNullOrEmpty(text)) return null;
        int clampedStart = Math.Clamp(startOff, 0, text.Length);
        int clampedEnd = Math.Clamp(endOff, clampedStart, text.Length);
        if (clampedStart >= clampedEnd) return null;

        float left = runBounds.Left;
        if (clampedStart > 0)
        {
            string before = text[..clampedStart];
            left += MeasureTextWidth(before, fontSize, fontFamily, fontWeight);
        }
        float right = runBounds.Left;
        if (clampedEnd <= text.Length)
        {
            string upToEnd = text[..clampedEnd];
            right += MeasureTextWidth(upToEnd, fontSize, fontFamily, fontWeight);
        }
        else
        {
            right = runBounds.Right;
        }

        return new SKRect(left, runBounds.Top, right, runBounds.Bottom);
    }

    private static float MeasureTextWidth(string text, float fontSize, string fontFamily, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (TextMeasurer.Instance != null)
            return TextMeasurer.Instance.MeasureText(text, fontFamily, fontSize, weight);
        return text.Length * fontSize * 0.45f;
    }

    private static bool IsNodeBetween(Node? target, Node? a, Node? b)
    {
        return CompareDomPosition(target, a) > 0 && CompareDomPosition(target, b) < 0;
    }

    private static int CompareDomPosition(Node? a, Node? b)
    {
        if (a == null || b == null) return a == b ? 0 : (a == null ? -1 : 1);
        if (a == b) return 0;
        var aPath = new List<Node>();
        var bPath = new List<Node>();
        var cur = a;
        while (cur != null) { aPath.Add(cur); cur = cur.ParentNode; }
        cur = b;
        while (cur != null) { bPath.Add(cur); cur = cur.ParentNode; }
        aPath.Reverse();
        bPath.Reverse();
        int depth = Math.Min(aPath.Count, bPath.Count);
        for (int i = 0; i < depth; i++)
        {
            if (aPath[i] != bPath[i])
            {
                var parent = aPath[i].ParentNode;
                if (parent != null)
                {
                    int ai = parent.Children.IndexOf(aPath[i]);
                    int bi = parent.Children.IndexOf(bPath[i]);
                    return ai.CompareTo(bi);
                }
                return 0;
            }
        }
        return aPath.Count.CompareTo(bPath.Count);
    }
}