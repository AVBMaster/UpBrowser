using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Parser;
using UpBrowser.Core.Css;
using UpBrowser.Core.Input;
using UpBrowser.Core.Layout;
using UpBrowser.Rendering;

// ── Load the real test page through the same pipeline as the app shell ──
string htmlPath = Path.GetFullPath("test_scroll_interactive.html");
if (!File.Exists(htmlPath))
    htmlPath = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test_scroll_interactive.html"));
string html = File.ReadAllText(htmlPath);
string baseUrl = new Uri(htmlPath).AbsoluteUri;

RenderSnapshot.EnsureInitialized();
var dm = new DocumentManager();
var load = dm.LoadHtmlAsync(html, baseUrl, 1009, 730, 1f).GetAwaiter().GetResult();
new LayoutEngine().Layout(load.Document, 1009, 730);

Element? target = null;
void Walk(Element el)
{
    if (target != null) return;
    if (el.GetAttribute("class") == "default-scroll") { target = el; return; }
    foreach (var c in el.Children) if (c is Element ce) Walk(ce);
}
if (load.Document.DocumentElement != null) Walk(load.Document.DocumentElement);
if (target == null) { Console.WriteLine("[FAIL] .default-scroll not found"); return; }

var box = target!.LayoutBox!;
var bb = box.BorderBox;
var pb = box.PaddingBox;
Console.WriteLine($"[box] border=({bb.Left:F0},{bb.Top:F0} {bb.Width:F0}x{bb.Height:F0}) padding=({pb.Left:F0},{pb.Top:F0} {pb.Width:F0}x{pb.Height:F0})");
Console.WriteLine($"[box] IsScrollContainer={box.IsScrollContainer} contentH={box.ContentBox.Height:F0} scrollContentH={box.ScrollContentHeight:F0}");

// Scrollbar placeholder: content box must shrink by the bar thickness and
// children must not extend under the reserved strip.
float reserved = pb.Right - box.ContentBox.Right;
float maxChildRight = 0;
foreach (var c in box.Children) maxChildRight = Math.Max(maxChildRight, c.MarginBox.Right);
Console.WriteLine($"[reserve] contentRight={box.ContentBox.Right:F1} paddingRight={pb.Right:F1} reserved={reserved:F1} maxChildRight={maxChildRight:F1} underBar={maxChildRight > box.ContentBox.Right + 0.5f}");

float thickness = 12f;
float sbLeft = pb.Right - thickness;

// Thumb geometry exactly like ScrollInteraction.BeginVerticalDrag
float trackH = pb.Height;
float thumbRatio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
float thumbH = Math.Max(20f, trackH * Math.Min(1, thumbRatio));
float maxScroll = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
float thumbTop = (trackH - thumbH) * (Math.Clamp(box.ScrollY, 0, maxScroll) / maxScroll);
float grabY = pb.Top + thumbTop + thumbH / 2;   // center of thumb
float grabX = sbLeft + thickness / 2;

Console.WriteLine($"[geom] thumbTop={thumbTop:F1} thumbH={thumbH:F1} maxScroll={maxScroll:F0} grab=({grabX:F0},{grabY:F0})");

var scrollInt = new ScrollInteraction();
scrollInt.SetDocument(load.Document);
bool scrollFired = false;
scrollInt.OnScrollChanged = () => scrollFired = true;

// ── Simulate drag: mousedown on thumb, move up 40px ──
bool grabbed = scrollInt.HandleMouseDown(grabX, grabY);
Console.WriteLine($"[down] grabbed={grabbed}");
scrollInt.HandleMouseMove(grabX, grabY + 40);
Console.WriteLine($"[move] scrollY={box.ScrollY:F1} (expect ~{maxScroll * (40f / (trackH - thumbH)):F1}) eventFired={scrollFired}");

// ── Rebuild display list like BrowserApp B1 branch and rasterize ──
SKBitmap Rasterize(float contentOffsetY)
{
    var visitor = new PaintVisitor(contentOffsetY, null, null,
        SKFontManager.Default.FontFamilies.ToArray(), baseUrl, 1009, 730);
    visitor.SetSkipInputTextOverlay(true);
    visitor.VisitDocumentStacking(load.Document);
    var dl = visitor.GetDisplayList();
    dl.SortByZIndex();
    var info = new SKImageInfo(1009, 730, SKColorType.Rgba8888, SKAlphaType.Premul);
    var bmp = new SKBitmap(info);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.White);
    // mimic SkiaRenderer.RenderWithScroll page translate (page scroll = 0 here)
    dl.Execute(canvas);
    canvas.Flush();
    return bmp;
}

using var before = Rasterize(0);

// Now scroll back via wheel path and compare again
scrollInt.HandleWheel(0, -120 * 10, (float)(bb.Left + 20), (float)(bb.Top + 20));
Console.WriteLine($"[wheel] scrollY={box.ScrollY:F1}");
using var after = Rasterize(0);

// Compare ONLY the container's content region (exclude scrollbar strip on the right)
int rx0 = (int)pb.Left, ry0 = (int)pb.Top + 1;
int rx1 = (int)(pb.Right - thickness - 2), ry1 = (int)pb.Bottom - 1;
int diffPx = 0, total = 0;
for (int y = ry0; y < ry1; y++)
    for (int x = rx0; x < rx1; x++)
    {
        total++;
        var a = before.GetPixel(x, y);
        var b = after.GetPixel(x, y);
        if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 10)
            diffPx++;
    }
Console.WriteLine($"[render] content region ({rx0},{ry0})-({rx1},{ry1}): {diffPx}/{total} px differ ({100.0 * diffPx / total:F1}%)");

Console.WriteLine($"[verdict] grabbed={grabbed} scrolled={box.ScrollY > 0} contentMoved={diffPx > total * 0.05} -> {(grabbed && box.ScrollY > 0 && diffPx > total * 0.05 ? "PASS" : "FAIL")}");
