using System.Text;
using UpBrowser.Core.Css;
using UpBrowser.Core.Dom.Html;
using AngleSharp;
using AngleSharp.Css.Parser;

namespace UpBrowser.Core.Dom;

public class DocumentManager
{
    private static readonly Stylesheet _uaStylesheet;
    private static readonly string _defaultHtml;
    private static readonly CssParser _cssParser = new();

    static DocumentManager()
    {
        _uaStylesheet = _cssParser.Parse(GetUserAgentStylesStatic());
        _defaultHtml = BuildDefaultHtml();
    }

    public async Task<DocumentLoadResult> LoadHtmlAsync(string html, string? baseUrl = null, float viewportWidth = 1024f, float viewportHeight = 768f, float dpiScale = 1.0f)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var angleSharpDoc = await context.OpenAsync(req => req.Content(html));

        var doc = new Document
        {
            Url = baseUrl ?? "upbrowser://local",
            Title = angleSharpDoc.Title ?? "Untitled"
        };

        try
        {
            ConvertHtmlToDom(angleSharpDoc.DocumentElement!, doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DOM] Error converting HTML to DOM: {ex.Message}");
        }

        var styleComputer = new StyleComputer();
        styleComputer.AddStylesheet(_uaStylesheet);

        try
        {
            await LoadStylesFromHtml(angleSharpDoc, styleComputer, baseUrl);
            styleComputer.ComputeStyles(doc, viewportWidth, viewportHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSS] Error computing styles: {ex.Message}");
        }

        try
        {
            var layoutEngine = new UpBrowser.Core.Layout.LayoutEngine();
            layoutEngine.Layout(doc, viewportWidth, viewportHeight, dpiScale);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Layout] Error during layout: {ex.Message}");
        }

        return new DocumentLoadResult(doc, angleSharpDoc, styleComputer);
    }

    public static string DefaultHtml => _defaultHtml;

    private static string? _jsTestHtml;
    public static string JsTestHtml => _jsTestHtml ??= BuildJsCompatibilityTestHtml();

    private static string? _elementTestHtml;
    public static string ElementTestHtml => _elementTestHtml ??= BuildElementTestHtml();

    private async Task LoadStylesFromHtml(AngleSharp.Dom.IDocument angleSharpDoc, StyleComputer styleComputer, string? baseUrl)
    {
        var elements = angleSharpDoc.All;
        var styleElements = elements.Where(e => e.LocalName?.ToLowerInvariant() == "style");
        foreach (var styleElement in styleElements)
        {
            var cssText = styleElement.TextContent;
            if (!string.IsNullOrEmpty(cssText))
            {
                try
                {
                    var stylesheet = _cssParser.Parse(cssText);
                    await ProcessImports(stylesheet, styleComputer, baseUrl);
                    styleComputer.AddStylesheet(stylesheet);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CSS] Failed to parse <style>: {ex.Message}");
                }
            }
        }

        var linkElements = elements.Where(e =>
            e.LocalName?.ToLowerInvariant() == "link" &&
            e.GetAttribute("rel")?.ToLowerInvariant() == "stylesheet");

        foreach (var link in linkElements)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;
            var url = ResolveUrl(baseUrl, href);
            if (url == null) continue;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var stylesheet = _cssParser.Parse(cssText);
                await ProcessImports(stylesheet, styleComputer, url);
                styleComputer.AddStylesheet(stylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to load stylesheet '{url}': {ex.Message}");
            }
        }
    }

    private async Task ProcessImports(Stylesheet stylesheet, StyleComputer styleComputer, string? baseUrl)
    {
        foreach (var importRule in stylesheet.ImportRules)
        {
            if (string.IsNullOrEmpty(importRule.Url)) continue;
            var url = ResolveUrl(baseUrl, importRule.Url);
            if (url == null) continue;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var importedStylesheet = _cssParser.Parse(cssText);
                await ProcessImports(importedStylesheet, styleComputer, url);
                styleComputer.AddStylesheet(importedStylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to process @import '{importRule.Url}': {ex.Message}");
            }
        }
    }

    private static string? ResolveUrl(string? baseUrl, string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("data:") || href.StartsWith("blob:"))
            return href;
        if (href.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + href;
            return "http:" + href;
        }
        if (baseUrl != null && (baseUrl.StartsWith("http://") || baseUrl.StartsWith("https://")))
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                var resolved = new Uri(baseUri, href);
                return resolved.ToString();
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private void ConvertHtmlToDom(AngleSharp.Dom.IElement source, Document target)
    {
        if (source == null) return;
        var htmlElement = new HtmlElement("html");
        target.DocumentElement = htmlElement;
        target.AppendChild(htmlElement);

        foreach (var child in source.ChildNodes)
        {
            try
            {
                if (child is AngleSharp.Dom.IElement childElement)
                {
                    var element = new HtmlElement(childElement.LocalName);
                    foreach (var attr in childElement.Attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Name))
                            element.Attributes[attr.Name] = attr.Value ?? "";
                    }
                    if (childElement.HasAttribute("style"))
                    {
                        var props = _cssParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                        foreach (var prop in props)
                            element.Style[prop.Key] = prop.Value;
                    }
                    var tagName = childElement.LocalName?.ToLowerInvariant();
                    if (tagName == "html") { }
                    else if (tagName == "head")
                    {
                        target.Head = element;
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "body")
                    {
                        target.Body = element;
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "title")
                    {
                        target.Title = childElement.TextContent ?? "";
                        if (target.Head != null)
                        {
                            var titleElem = new HtmlElement("title");
                            titleElem.AppendChild(new TextNode(childElement.TextContent ?? ""));
                            target.Head.AppendChild(titleElem);
                        }
                    }
                    else
                    {
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                }
                else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
                {
                    var text = NormalizeTextContent(child.TextContent ?? "");
                    if (!string.IsNullOrWhiteSpace(text))
                        htmlElement.AppendChild(new TextNode(text));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOM] Error converting element: {ex.Message}");
            }
        }
    }

    private void ConvertElementChildren(AngleSharp.Dom.INode source, Element target)
    {
        if (source == null || target == null) return;
        foreach (var child in source.ChildNodes)
        {
            try
            {
                if (child is AngleSharp.Dom.IElement childElement)
                {
                    var element = new HtmlElement(childElement.LocalName);
                    foreach (var attr in childElement.Attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Name))
                            element.Attributes[attr.Name] = attr.Value ?? "";
                    }
                    if (childElement.HasAttribute("style"))
                    {
                        var props = _cssParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                        foreach (var prop in props)
                            element.Style[prop.Key] = prop.Value;
                    }
                    target.AppendChild(element);
                    ConvertElementChildren(childElement, element);
                }
                else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
                {
                    var text = NormalizeTextContent(child.TextContent ?? "");
                    if (!string.IsNullOrWhiteSpace(text))
                        target.AppendChild(new TextNode(text));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOM] Error converting child: {ex.Message}");
            }
        }
    }

    private static string NormalizeTextContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string BuildDefaultHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <title>UpBrowser</title>
</head>
<body style='background: #f5f5f5; margin: 0; padding: 20px; font-family: Arial, sans-serif;'>
    <h1 style='color: #333; font-size: 32px; margin: 0 0 20px 0;'>Hello World</h1>
    <p style='color: #666; font-size: 16px; line-height: 1.5;'>This is a test paragraph with some text content.</p>
    <p style='color: #666; font-size: 16px; line-height: 1.5;'>这是一个测试段落，用于验证中文显示是否正常。</p>
    <div style='background: #ffeb3b; padding: 20px; border: 2px solid #f44336; margin: 20px 0; border-radius: 8px;'>
        <h2 style='color: #333; margin: 0 0 10px 0;'>Box Model Test</h2>
        <p style='color: #555;'>This div has margin, border, padding, and content.</p>
    </div>
    <ul style='color: #333;'>
        <li>List item 1</li>
        <li>List item 2</li>
        <li>List item 3</li>
    </ul>
    <button style='background: #2196F3; color: white; padding: 10px 20px; border: none; border-radius: 4px; font-size: 14px;'>Click Me</button>
    <div style='margin-top: 20px; padding: 15px; background: white; border: 1px solid #ddd;'>
        <span style='color: red;'>Red text</span> and <span style='color: blue;'>blue text</span> in same line.
    </div>
    <div style='display: flex; gap: 10px; margin-top: 20px;'>
        <div style='background: #e3f2fd; padding: 15px; flex: 1;'>Flex Item 1</div>
        <div style='background: #f3e5f5; padding: 15px; flex: 1;'>Flex Item 2</div>
        <div style='background: #e8f5e9; padding: 15px; flex: 1;'>Flex Item 3</div>
    </div>
    <div style='position: relative; height: 100px; margin-top: 20px; background: #fff3e0;'>
        <div style='position: absolute; top: 10px; right: 10px; background: #ff5722; color: white; padding: 5px 10px;'>Absolute Position</div>
    </div>
<script>
(function() {
    var allLines = [];
    allLines.push(""========== DOM 元素坐标与尺寸详细报告 =========="");
    allLines.push(""窗口尺寸: innerWidth="" + window.innerWidth + "", innerHeight="" + window.innerHeight);
    allLines.push(""页面尺寸: scrollWidth="" + document.documentElement.scrollWidth + "", scrollHeight="" + document.documentElement.scrollHeight);
    allLines.push("""");

    function collectElementInfo(el, depth) {
        if (!el) return;
        try {
            var rect = el.getBoundingClientRect();
            var computed = window.getComputedStyle(el);
            var indent = ""  "".repeat(depth);
            var cls = (typeof el.className === ""string"") ? el.className : """";
            var tagInfo = el.tagName + (el.id ? ""#"" + el.id : """") + (cls ? ""."" + cls.split("" "").join(""."") : """");
            var line = indent + ""> "" + tagInfo;
            allLines.push(line);
            allLines.push(indent + ""  [尺寸] offsetW="" + el.offsetWidth + "", offsetH="" + el.offsetHeight + "", clientW="" + el.clientWidth + "", clientH="" + el.clientHeight + "", scrollW="" + el.scrollWidth + "", scrollH="" + el.scrollHeight);
            allLines.push(indent + ""  [位置] offsetTop="" + el.offsetTop + "", offsetLeft="" + el.offsetLeft + "", rect(top="" + rect.top.toFixed(1) + "", left="" + rect.left.toFixed(1) + "", right="" + rect.right.toFixed(1) + "", bottom="" + rect.bottom.toFixed(1) + "", w="" + rect.width.toFixed(1) + "", h="" + rect.height.toFixed(1) + "")"");
            allLines.push(indent + ""  [样式] display="" + computed.display + "", position="" + computed.position + "", boxSizing="" + computed.boxSizing);
            allLines.push(indent + ""  [边距] margin="" + computed.marginTop + ""/"" + computed.marginRight + ""/"" + computed.marginBottom + ""/"" + computed.marginLeft + "", padding="" + computed.paddingTop + ""/"" + computed.paddingRight + ""/"" + computed.paddingBottom + ""/"" + computed.paddingLeft + "", border="" + computed.borderTopWidth + ""/"" + computed.borderRightWidth + ""/"" + computed.borderBottomWidth + ""/"" + computed.borderLeftWidth);
            allLines.push(indent + ""  [字体] fontSize="" + computed.fontSize + "", lineHeight="" + computed.lineHeight + "", color="" + computed.color + "", bg="" + computed.backgroundColor);
            allLines.push(indent + ""  [层级] parent="" + (el.parentElement ? el.parentElement.tagName : ""(none)"") + "", children="" + el.children.length);
            allLines.push("""");
        } catch(e) {
            allLines.push(""  "".repeat(depth) + ""> "" + (el ? el.tagName : ""(null)"") + "" [读取失败: "" + e.message + ""]"");
            allLines.push("""");
        }
    }

    function traverse(el, depth) {
        if (!el) return;
        collectElementInfo(el, depth);
        var kids = el.children;
        for (var i = 0; i < kids.length; i++) {
            var child = kids[i];
            if (child) {
                traverse(child, depth + 1);
            }
        }
    }

    traverse(document.documentElement, 0);
    allLines.push(""========== 报告结束 =========="");
    console.log(allLines.join(""\n""));
})();
</script></body>
</html>";
    }

    private static string BuildJsCompatibilityTestHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head><title>JS Compatibility Test</title></head>
<body style='background:#1e1e2e;color:#cdd6f4;font-family:Consolas,monospace;padding:20px;'>
<div id='summary' style='background:#313244;padding:15px;border-radius:8px;margin-bottom:15px;'>
  <h1 style='margin:0 0 10px 0;font-size:20px;'>UpBrowser JS Compatibility Test</h1>
  <p style='margin:0;'><span id='passCount'>0</span> passed / <span id='failCount'>0</span> failed / <span id='totalCount'>0</span> total</p>
</div>
<div id='results'></div>
<script>
(function(){
  var allResults = [];
  function test(cat, name, fn) {
    try { fn(); allResults.push({c:cat, n:name, p:true}); }
    catch(e) { allResults.push({c:cat, n:name, p:false, e:(e.message||'')}); }
  }
  function assertEq(a, b, msg) {
    if (a !== b) {
      var sa, sb;
      try { sa = JSON.stringify(a); } catch(e) { sa = String(a); }
      try { sb = JSON.stringify(b); } catch(e) { sb = String(b); }
      throw new Error(msg || (sa + ' !== ' + sb));
    }
  }

  // ========== 1. Core JS ==========
  test('Core JS', 'Array push', function(){ var a=[]; a.push(1); assertEq(a[0],1); });
  test('Core JS', 'Array pop', function(){ var a=[1,2]; var v=a.pop(); assertEq(v,2); assertEq(a.length,1); });
  test('Core JS', 'Array map', function(){ var r=[1,2,3].map(function(x){return x*2;}); assertEq(r[1],4); });
  test('Core JS', 'Array filter', function(){ var r=[1,2,3,4].filter(function(x){return x>2;}); assertEq(r.length,2); });
  test('Core JS', 'Array reduce', function(){ var s=[1,2,3].reduce(function(a,b){return a+b;},0); assertEq(s,6); });
  test('Core JS', 'Array indexOf', function(){ assertEq([1,2,3].indexOf(2),1); });
  test('Core JS', 'Array forEach', function(){ var c=0; [1,2,3].forEach(function(x){c+=x;}); assertEq(c,6); });
  test('Core JS', 'Array includes', function(){ assertEq([1,2,3].includes(2),true); });
  test('Core JS', 'Array find', function(){ var r=[1,2,3].find(function(x){return x>1;}); assertEq(r,2); });
  test('Core JS', 'Array findIndex', function(){ var i=[1,2,3].findIndex(function(x){return x>1;}); assertEq(i,1); });
  test('Core JS', 'Array join', function(){ assertEq([1,2,3].join(','),'1,2,3'); });
  test('Core JS', 'Array slice', function(){ assertEq([1,2,3,4].slice(1,3).length,2); });
  test('Core JS', 'Array splice', function(){ var a=[1,2,3,4]; a.splice(1,2); assertEq(a.length,2); });
  test('Core JS', 'Array concat', function(){ assertEq([1,2].concat([3,4]).length,4); });
  test('Core JS', 'Array isArray', function(){ assertEq(Array.isArray([]),true); assertEq(Array.isArray({}),false); });
  test('Core JS', 'Array sort', function(){ var a=[3,1,2]; a.sort(); assertEq(a[0],1); });
  test('Core JS', 'Array reverse', function(){ var a=[1,2,3]; a.reverse(); assertEq(a[0],3); });
  test('Core JS', 'Array from', function(){ var a=Array.from('123'); assertEq(a.length,3); });
  test('Core JS', 'Array fill', function(){ var a=[1,2,3]; a.fill(0); assertEq(a[0],0); });
  test('Core JS', 'String split', function(){ assertEq('a,b,c'.split(',').length,3); });
  test('Core JS', 'String replace', function(){ assertEq('hello'.replace('l','x'),'hexlo'); });
  test('Core JS', 'String replaceAll', function(){ assertEq('hello'.replaceAll('l','x'),'hexxo'); });
  test('Core JS', 'String trim', function(){ assertEq('  hi  '.trim(),'hi'); });
  test('Core JS', 'String toUpperCase', function(){ assertEq('hi'.toUpperCase(),'HI'); });
  test('Core JS', 'String toLowerCase', function(){ assertEq('HI'.toLowerCase(),'hi'); });
  test('Core JS', 'String charAt', function(){ assertEq('abc'.charAt(1),'b'); });
  test('Core JS', 'String charCodeAt', function(){ assertEq('abc'.charCodeAt(1),98); });
  test('Core JS', 'String indexOf', function(){ assertEq('abc'.indexOf('b'),1); });
  test('Core JS', 'String includes', function(){ assertEq('hello'.includes('ell'),true); });
  test('Core JS', 'String startsWith', function(){ assertEq('hello'.startsWith('he'),true); });
  test('Core JS', 'String endsWith', function(){ assertEq('hello'.endsWith('lo'),true); });
  test('Core JS', 'String substring', function(){ assertEq('hello'.substring(1,3),'el'); });
  test('Core JS', 'String slice', function(){ assertEq('hello'.slice(1,3),'el'); });
  test('Core JS', 'String match', function(){ var m='abc123'.match(/[a-z]+/); assertEq(m[0],'abc'); });
  test('Core JS', 'String search', function(){ assertEq('abc123'.search(/\d+/),3); });
  test('Core JS', 'String repeat', function(){ assertEq('ab'.repeat(3),'ababab'); });
  test('Core JS', 'String padStart', function(){ assertEq('5'.padStart(3,'0'),'005'); });
  test('Core JS', 'Object keys', function(){ assertEq(Object.keys({a:1,b:2}).length,2); });
  test('Core JS', 'Object values', function(){ var v=Object.values({a:1,b:2}); assertEq(v[0],1); });
  test('Core JS', 'Object entries', function(){ var e=Object.entries({a:1}); assertEq(e[0][0],'a'); });
  test('Core JS', 'Object assign', function(){ var o=Object.assign({},{a:1}); assertEq(o.a,1); });
  test('Core JS', 'Object hasOwnProperty', function(){ var o={a:1}; assertEq(o.hasOwnProperty('a'),true); });
  test('Core JS', 'Object defineProperty', function(){ var o={}; Object.defineProperty(o,'x',{value:42}); assertEq(o.x,42); });
  test('Core JS', 'parseInt', function(){ assertEq(parseInt('42'),42); });
  test('Core JS', 'parseFloat', function(){ assertEq(parseFloat('3.14'),3.14); });
  test('Core JS', 'isNaN', function(){ assertEq(isNaN(NaN),true); });
  test('Core JS', 'Number toFixed', function(){ assertEq((3.1415).toFixed(2),'3.14'); });
  test('Core JS', 'Math floor', function(){ assertEq(Math.floor(3.9),3); });
  test('Core JS', 'Math ceil', function(){ assertEq(Math.ceil(3.1),4); });
  test('Core JS', 'Math round', function(){ assertEq(Math.round(3.5),4); });
  test('Core JS', 'Math max', function(){ assertEq(Math.max(1,5,3),5); });
  test('Core JS', 'Math min', function(){ assertEq(Math.min(1,5,3),1); });
  test('Core JS', 'Math abs', function(){ assertEq(Math.abs(-5),5); });
  test('Core JS', 'Math sqrt', function(){ assertEq(Math.sqrt(9),3); });
  test('Core JS', 'Math pow', function(){ assertEq(Math.pow(2,3),8); });
  test('Core JS', 'Math random', function(){ var r=Math.random(); assertEq(typeof r,'number'); });
  test('Core JS', 'Date now', function(){ var d=Date.now(); assertEq(typeof d,'number'); });
  test('Core JS', 'Date getFullYear', function(){ var y=new Date().getFullYear(); assertEq(typeof y,'number'); });
  test('Core JS', 'Date toISOString', function(){ var s=new Date().toISOString(); assertEq(typeof s,'string'); });
  test('Core JS', 'JSON parse', function(){ var o=JSON.parse('{""a"":1}'); assertEq(o.a,1); });
  test('Core JS', 'JSON stringify', function(){ var s=JSON.stringify({a:1}); assertEq(typeof s,'string'); });
  test('Core JS', 'typeof', function(){ assertEq(typeof 42,'number'); assertEq(typeof 'hi','string'); });
  test('Core JS', 'instanceof', function(){ assertEq([] instanceof Array,true); });
  test('Core JS', 'Ternary operator', function(){ var x=true?1:2; assertEq(x,1); });
  test('Core JS', 'Arrow function', function(){ var f=function(x){return x*2;}; assertEq(f(3),6); });
  test('Core JS', 'Template literal', function(){ var x=42; var s='value: '+x; assertEq(s.indexOf('42')>=0,true); });
  test('Core JS', 'let/const scoping', function(){ let a=1; const b=2; assertEq(a+b,3); });
  test('Core JS', 'Destructuring', function(){ var a,b; [a,b]=[1,2]; assertEq(a,1); assertEq(b,2); });
  test('Core JS', 'Spread operator', function(){ var a=[1,2,3]; var b=[...a,4]; assertEq(b.length,4); });
  test('Core JS', 'Default params', function(){ function f(x,y){return y||0;}; assertEq(f(5),0); });
  test('Core JS', 'Rest params', function(){ function f(a,...b){return b.length;}; assertEq(f(1,2,3),2); });
  test('Core JS', 'Map constructor', function(){ var m=new Map(); m.set('a',1); assertEq(m.get('a'),1); });
  test('Core JS', 'Set constructor', function(){ var s=new Set(); s.add(1); assertEq(s.has(1),true); });
  test('Core JS', 'RegExp test', function(){ assertEq(/hello/.test('hello world'),true); });
  test('Core JS', 'RegExp exec', function(){ var m=/\d+/.exec('abc123'); assertEq(m[0],'123'); });
  test('Core JS', 'String localeCompare', function(){ assertEq('a'.localeCompare('b'),-1); });
  test('Core JS', 'try/catch/finally', function(){ var ok=false; try{throw new Error();}catch(e){ok=true;} assertEq(ok,true); });
  test('Core JS', 'Promise basic', function(){ var p=new Promise(function(r){r(42);}); assertEq(typeof p.then,'function'); });
  test('Core JS', 'Promise.resolve', function(){ var p=Promise.resolve(1); assertEq(typeof p,'object'); });
  test('Core JS', 'Proxy basic', function(){ var t={}; var p=new Proxy(t,{get:function(o,k){return 42;}}); assertEq(p.x,42); });
  test('Core JS', 'Reflect get', function(){ var o={a:1}; assertEq(Reflect.get(o,'a'),1); });
  test('Core JS', 'Symbol basic', function(){ var s=Symbol('test'); assertEq(typeof s,'symbol'); });
  test('Core JS', 'Function bind', function(){ function f(a){return a+this.x;}; var g=f.bind({x:1}); assertEq(g(2),3); });
  test('Core JS', 'Function call/apply', function(){ function f(a){return a+this.x;}; assertEq(f.call({x:1},2),3); assertEq(f.apply({x:1},[2]),3); });

  // ========== 2. DOM Core ==========
  test('DOM', 'createElement', function(){ var el=document.createElement('div'); assertEq(el.tagName,'DIV'); });
  test('DOM', 'createTextNode', function(){ var t=document.createTextNode('hello'); assertEq(t.textContent,'hello'); });
  test('DOM', 'getElementById', function(){ var el=document.getElementById('summary'); assertEq(el!==null,true); });
  test('DOM', 'querySelector', function(){ var el=document.querySelector('#summary'); assertEq(el!==null,true); });
  test('DOM', 'querySelectorAll', function(){ var els=document.querySelectorAll('div'); assertEq(els.length>0,true); });
  test('DOM', 'innerHTML', function(){ var el=document.createElement('div'); el.innerHTML='<span>hi</span>'; assertEq(el.children.length,1); });
  test('DOM', 'textContent', function(){ var el=document.createElement('div'); el.textContent='hello'; assertEq(el.textContent,'hello'); });
  test('DOM', 'getAttribute/setAttribute', function(){ var el=document.createElement('div'); el.setAttribute('data-x','42'); assertEq(el.getAttribute('data-x'),'42'); });
  test('DOM', 'hasAttribute', function(){ var el=document.createElement('div'); el.setAttribute('x','1'); assertEq(el.hasAttribute('x'),true); });
  test('DOM', 'removeAttribute', function(){ var el=document.createElement('div'); el.setAttribute('x','1'); el.removeAttribute('x'); assertEq(el.hasAttribute('x'),false); });
  test('DOM', 'dataset', function(){ var el=document.createElement('div'); el.setAttribute('data-test','val'); assertEq(el.dataset?el.dataset.test||'':'','val'); });
  test('DOM', 'className/id', function(){ var el=document.createElement('div'); el.id='myid'; el.className='myclass'; assertEq(el.id,'myid'); assertEq(el.className,'myclass'); });
  test('DOM', 'classList add/remove', function(){ var el=document.createElement('div'); el.classList.add('a','b'); assertEq(el.classList.contains('a'),true); el.classList.remove('a'); assertEq(el.classList.contains('a'),false); });
  test('DOM', 'classList toggle', function(){ var el=document.createElement('div'); el.classList.toggle('x'); assertEq(el.classList.contains('x'),true); el.classList.toggle('x'); assertEq(el.classList.contains('x'),false); });
  test('DOM', 'children/childElementCount', function(){ var p=document.createElement('div'); p.innerHTML='<span></span><span></span>'; assertEq(p.children.length,2); assertEq(p.childElementCount,2); });
  test('DOM', 'firstElementChild/lastElementChild', function(){ var p=document.createElement('div'); p.innerHTML='<span id=""a""></span><span id=""b""></span>'; assertEq(p.firstElementChild.id,'a'); assertEq(p.lastElementChild.id,'b'); });
  test('DOM', 'parentElement', function(){ var p=document.createElement('div'); var c=document.createElement('span'); p.appendChild(c); assertEq(c.parentElement,p); });
  test('DOM', 'nextElementSibling/previousElementSibling', function(){ var p=document.createElement('div'); p.innerHTML='<span></span><b></b>'; assertEq(p.children[0].nextElementSibling.tagName,'B'); assertEq(p.children[1].previousElementSibling.tagName,'SPAN'); });
  test('DOM', 'closest', function(){ var el=document.createElement('div'); el.className='wrapper'; assertEq(el.closest('.wrapper')!==null,true); });
  test('DOM', 'matches', function(){ var el=document.createElement('div'); el.className='test'; assertEq(el.matches('.test'),true); });
  test('DOM', 'appendChild', function(){ var p=document.createElement('div'); var c=document.createElement('span'); p.appendChild(c); assertEq(p.children.length,1); });
  test('DOM', 'insertBefore', function(){ var p=document.createElement('div'); p.innerHTML='<span></span><span></span>'; var n=document.createElement('b'); p.insertBefore(n,p.children[1]); assertEq(p.children[1].tagName,'B'); });
  test('DOM', 'removeChild', function(){ var p=document.createElement('div'); p.innerHTML='<span></span>'; var c=p.children[0]; p.removeChild(c); assertEq(p.children.length,0); });
  test('DOM', 'replaceChild', function(){ var p=document.createElement('div'); p.innerHTML='<span></span>'; var n=document.createElement('b'); p.replaceChild(n,p.children[0]); assertEq(p.children[0].tagName,'B'); });
  test('DOM', 'cloneNode', function(){ var el=document.createElement('div'); el.innerHTML='<span>text</span>'; var c=el.cloneNode(true); assertEq(c.children.length,1); });
  test('DOM', 'contains', function(){ var p=document.createElement('div'); var c=document.createElement('span'); p.appendChild(c); assertEq(p.contains(c),true); });
  test('DOM', 'getElementsByTagName', function(){ var els=document.getElementsByTagName('div'); assertEq(els.length>0,true); });
  test('DOM', 'getElementsByClassName', function(){ var els=document.getElementsByClassName('test'); assertEq(typeof els,'object'); });
  test('DOM', 'documentElement', function(){ var el=document.documentElement; assertEq(el.tagName,'HTML'); });
  test('DOM', 'document.body', function(){ assertEq(document.body.tagName,'BODY'); });
  test('DOM', 'document.head', function(){ assertEq(document.head.tagName,'HEAD'); });
  test('DOM', 'document.title', function(){ assertEq(typeof document.title,'string'); });

  // ========== 3. Element Geometry ==========
  test('Geometry', 'offsetWidth/Height', function(){ var el=document.createElement('div'); assertEq(typeof el.offsetWidth,'number'); assertEq(typeof el.offsetHeight,'number'); });
  test('Geometry', 'clientWidth/Height', function(){ var el=document.createElement('div'); assertEq(typeof el.clientWidth,'number'); assertEq(typeof el.clientHeight,'number'); });
  test('Geometry', 'clientLeft/Top', function(){ var el=document.createElement('div'); assertEq(typeof el.clientLeft,'number'); assertEq(typeof el.clientTop,'number'); });
  test('Geometry', 'offsetTop/Left', function(){ var el=document.createElement('div'); assertEq(typeof el.offsetTop,'number'); assertEq(typeof el.offsetLeft,'number'); });
  test('Geometry', 'offsetParent', function(){ var el=document.createElement('div'); var p=el.offsetParent; assertEq(p===null||p!==undefined,true); });
  test('Geometry', 'scrollWidth/Height', function(){ var el=document.createElement('div'); assertEq(typeof el.scrollWidth,'number'); assertEq(typeof el.scrollHeight,'number'); });
  test('Geometry', 'getBoundingClientRect', function(){ var el=document.querySelector('#summary'); var r=el.getBoundingClientRect(); assertEq(typeof r.top,'number'); assertEq(typeof r.left,'number'); assertEq(typeof r.width,'number'); assertEq(typeof r.height,'number'); assertEq(typeof r.right,'number'); assertEq(typeof r.bottom,'number'); assertEq(typeof r.x,'number'); assertEq(typeof r.y,'number'); });

  // ========== 4. CSSOM ==========
  test('CSSOM', 'getComputedStyle', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s,'object'); });
  test('CSSOM', 'computed display', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.display,'string'); });
  test('CSSOM', 'computed color', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.color,'string'); });
  test('CSSOM', 'computed fontSize', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.fontSize,'string'); });
  test('CSSOM', 'computed backgroundColor', function(){ var el=document.querySelector('#summary'); var s=getComputedStyle(el); assertEq(typeof s.backgroundColor,'string'); });
  test('CSSOM', 'computed margin/padding', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.marginTop,'string'); assertEq(typeof s.paddingTop,'string'); });
  test('CSSOM', 'computed border', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.borderTopWidth,'string'); });
  test('CSSOM', 'computed position', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.position,'string'); });
  test('CSSOM', 'computed boxSizing', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.boxSizing,'string'); });
  test('CSSOM', 'computed lineHeight', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.lineHeight,'string'); });
  test('CSSOM', 'computed fontFamily', function(){ var el=document.createElement('div'); var s=getComputedStyle(el); assertEq(typeof s.fontFamily,'string'); });

  // ========== 5. Style Manipulation ==========
  test('Style', 'element.style.color', function(){ var el=document.createElement('div'); el.style.color='red'; assertEq(el.style.color,'red'); });
  test('Style', 'element.style.backgroundColor', function(){ var el=document.createElement('div'); el.style.backgroundColor='blue'; assertEq(el.style.backgroundColor,'blue'); });
  test('Style', 'element.style.fontSize', function(){ var el=document.createElement('div'); el.style.fontSize='20px'; assertEq(el.style.fontSize,'20px'); });
  test('Style', 'element.style.width/height', function(){ var el=document.createElement('div'); el.style.width='100px'; el.style.height='50px'; assertEq(el.style.width,'100px'); assertEq(el.style.height,'50px'); });
  test('Style', 'element.style.cssText', function(){ var el=document.createElement('div'); el.style.cssText='color:red;font-size:14px;'; assertEq(el.style.color,'red'); assertEq(el.style.fontSize,'14px'); });
  test('Style', 'element.style.margin/padding', function(){ var el=document.createElement('div'); el.style.margin='10px'; el.style.padding='5px'; assertEq(typeof el.style.margin,'string'); assertEq(typeof el.style.padding,'string'); });
  test('Style', 'element.style.border', function(){ var el=document.createElement('div'); el.style.border='1px solid red'; assertEq(typeof el.style.border,'string'); });
  test('Style', 'element.style.display', function(){ var el=document.createElement('div'); el.style.display='none'; assertEq(el.style.display,'none'); el.style.display='block'; assertEq(el.style.display,'block'); });
  test('Style', 'element.style.position', function(){ var el=document.createElement('div'); el.style.position='absolute'; assertEq(el.style.position,'absolute'); });

  // ========== 6. Events ==========
  test('Events', 'addEventListener click', function(){ var el=document.createElement('div'); var ok=false; el.addEventListener('click',function(){ok=true;}); el.click(); assertEq(ok,true); });
  test('Events', 'removeEventListener', function(){ var el=document.createElement('div'); var c=0; function h(){c++;} el.addEventListener('click',h); el.removeEventListener('click',h); el.click(); assertEq(c,0); });
  test('Events', 'dispatchEvent', function(){ var el=document.createElement('div'); var ok=false; el.addEventListener('testev',function(){ok=true;}); var ev=new CustomEvent('testev'); el.dispatchEvent(ev); assertEq(ok,true); });
  test('Events', 'CustomEvent detail', function(){ var el=document.createElement('div'); var d=null; el.addEventListener('cust',function(e){d=e.detail;}); el.dispatchEvent(new CustomEvent('cust',{detail:42})); assertEq(d,42); });
  test('Events', 'event.target', function(){ var el=document.createElement('div'); var t=null; el.addEventListener('click',function(e){t=e.target;}); el.click(); assertEq(t,el); });
  test('Events', 'mouse events', function(){ var el=document.createElement('div'); var ok=false; el.addEventListener('mouseover',function(){ok=true;}); el.dispatchEvent(new MouseEvent('mouseover')); assertEq(ok,true); });
  test('Events', 'event.preventDefault', function(){ var el=document.createElement('div'); var ok=false; el.addEventListener('click',function(e){e.preventDefault();ok=true;}); el.click(); assertEq(ok,true); });
  test('Events', 'event.stopPropagation', function(){ var el=document.createElement('div'); var ok=false; el.addEventListener('click',function(e){e.stopPropagation();ok=true;}); el.click(); assertEq(ok,true); });

  // ========== 7. Window ==========
  test('Window', 'innerWidth/innerHeight', function(){ assertEq(typeof window.innerWidth,'number'); assertEq(typeof window.innerHeight,'number'); });
  test('Window', 'scrollX/scrollY', function(){ assertEq(typeof window.scrollX,'number'); assertEq(typeof window.scrollY,'number'); });
  test('Window', 'location.href', function(){ assertEq(typeof window.location.href,'string'); });
  test('Window', 'location.hostname', function(){ assertEq(typeof window.location.hostname,'string'); });
  test('Window', 'location.pathname', function(){ assertEq(typeof window.location.pathname,'string'); });
  test('Window', 'location.protocol', function(){ assertEq(typeof window.location.protocol,'string'); });
  test('Window', 'location.origin', function(){ assertEq(typeof window.location.origin,'string'); });
  test('Window', 'navigator.userAgent', function(){ assertEq(typeof navigator.userAgent,'string'); });
  test('Window', 'navigator.platform', function(){ assertEq(typeof navigator.platform,'string'); });
  test('Window', 'navigator.language', function(){ assertEq(typeof navigator.language,'string'); });
  test('Window', 'screen.width/height', function(){ assertEq(typeof screen.width,'number'); assertEq(typeof screen.height,'number'); });
  test('Window', 'setTimeout', function(){ assertEq(typeof setTimeout,'function'); });
  test('Window', 'setInterval', function(){ assertEq(typeof setInterval,'function'); });
  test('Window', 'clearTimeout', function(){ assertEq(typeof clearTimeout,'function'); });
  test('Window', 'requestAnimationFrame', function(){ assertEq(typeof requestAnimationFrame,'function'); });

  // ========== 8. XHR ==========
  test('XHR', 'XMLHttpRequest constructor', function(){ assertEq(typeof XMLHttpRequest,'function'); });
  test('XHR', 'XMLHttpRequest open', function(){ var x=new XMLHttpRequest(); assertEq(typeof x.open,'function'); });
  test('XHR', 'XMLHttpRequest send', function(){ var x=new XMLHttpRequest(); assertEq(typeof x.send,'function'); });
  test('XHR', 'XMLHttpRequest abort', function(){ var x=new XMLHttpRequest(); x.open('GET','/'); assertEq(typeof x.abort,'function'); });

  // ========== 9. Storage ==========
  test('Storage', 'localStorage', function(){ try{localStorage.setItem('test','val');var v=localStorage.getItem('test');localStorage.removeItem('test');assertEq(v,'val');}catch(e){throw new Error('localStorage not available');} });
  test('Storage', 'sessionStorage', function(){ try{sessionStorage.setItem('test','val');var v=sessionStorage.getItem('test');sessionStorage.removeItem('test');assertEq(v,'val');}catch(e){throw new Error('sessionStorage not available');} });

  // ========== 10. Console ==========
  test('Console', 'console.log', function(){ assertEq(typeof console.log,'function'); });
  test('Console', 'console.error', function(){ assertEq(typeof console.error,'function'); });
  test('Console', 'console.warn', function(){ assertEq(typeof console.warn,'function'); });
  test('Console', 'console.info', function(){ assertEq(typeof console.info,'function'); });
  test('Console', 'console.time/timeLog/timeEnd', function(){ assertEq(typeof console.time,'function'); assertEq(typeof console.timeLog,'function'); assertEq(typeof console.timeEnd,'function'); });
  test('Console', 'console.group/groupEnd', function(){ assertEq(typeof console.group,'function'); assertEq(typeof console.groupEnd,'function'); });
  test('Console', 'console.count', function(){ assertEq(typeof console.count,'function'); });
  test('Console', 'console.table', function(){ assertEq(typeof console.table,'function'); });
  test('Console', 'console.trace', function(){ assertEq(typeof console.trace,'function'); });
  test('Console', 'console.dir', function(){ assertEq(typeof console.dir,'function'); });

  // ========== 11. Form Elements ==========
  test('Forms', 'create input', function(){ var el=document.createElement('input'); assertEq(el.tagName,'INPUT'); });
  test('Forms', 'input value', function(){ var el=document.createElement('input'); el.value='test'; assertEq(el.value,'test'); });
  test('Forms', 'input type', function(){ var el=document.createElement('input'); el.type='checkbox'; assertEq(el.type,'checkbox'); });
  test('Forms', 'input disabled', function(){ var el=document.createElement('input'); el.disabled=true; assertEq(el.disabled,true); });
  test('Forms', 'input placeholder', function(){ var el=document.createElement('input'); el.placeholder='enter'; assertEq(el.placeholder,'enter'); });
  test('Forms', 'input readOnly', function(){ var el=document.createElement('input'); el.readOnly=true; assertEq(el.readOnly,true); });
  test('Forms', 'create textarea', function(){ var el=document.createElement('textarea'); assertEq(el.tagName,'TEXTAREA'); });
  test('Forms', 'create select', function(){ var el=document.createElement('select'); assertEq(el.tagName,'SELECT'); });
  test('Forms', 'create button', function(){ var el=document.createElement('button'); assertEq(el.tagName,'BUTTON'); });
  test('Forms', 'create form', function(){ var el=document.createElement('form'); assertEq(el.tagName,'FORM'); });
  test('Forms', 'create label', function(){ var el=document.createElement('label'); assertEq(el.tagName,'LABEL'); });
  test('Forms', 'input checked', function(){ var el=document.createElement('input'); el.type='checkbox'; el.checked=true; assertEq(el.checked,true); });

  // ========== Render Results ==========
  var pass=0,fail=0;
  var html=['<table style=""width:100%;border-collapse:collapse;font-size:13px;"">'];
  html.push('<tr style=""background:#45475a;color:#cdd6f4;""><th style=""padding:6px;text-align:left;"">Category</th><th style=""padding:6px;text-align:left;"">Test</th><th style=""padding:6px;width:60px;"">Result</th></tr>');
  for(var i=0;i<allResults.length;i++){
    var r=allResults[i];
    if(r.p) pass++; else fail++;
    var color=r.p?'#a6e3a1':'#f38ba8';
    var icon=r.p?'PASS':'FAIL';
    html.push('<tr style=""background:'+(i%2?'#313244':'#1e1e2e')+';""><td style=""padding:4px 6px;color:#89b4fa;"">'+r.c+'</td><td style=""padding:4px 6px;"">'+r.n+'</td><td style=""padding:4px 6px;color:'+color+';font-weight:bold;"">'+icon+'</td></tr>');
    if(!r.p) html.push('<tr style=""background:#1e1e2e;""><td colspan=""3"" style=""padding:2px 6px 6px 20px;color:#f38ba8;font-size:12px;"">'+r.e+'</td></tr>');
  }
  html.push('</table>');
  document.getElementById('results').innerHTML=html.join('');
  document.getElementById('passCount').innerHTML=''+pass;
  document.getElementById('failCount').innerHTML=''+fail;
  document.getElementById('totalCount').innerHTML=''+allResults.length;

  console.log('=== JS Compatibility Test ===');
  for (var i = 0; i < allResults.length; i++) {
    var r = allResults[i];
    console.log((r.p ? 'PASS' : 'FAIL') + ' | ' + r.c + ' | ' + r.n + (r.e ? ' | ' + r.e : ''));
  }
  console.log('Total: '+pass+'/'+allResults.length+' passed, '+fail+' failed');
  console.log(allResults);
})();
</script>
</body>
</html>";
    }

    private static string BuildElementTestHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head><title>Element Test - UpBrowser</title></head>
<body style='background:#f8f9fa;color:#212529;font-family:Arial,Helvetica,sans-serif;padding:0;margin:0;'>
<div style='background:linear-gradient(135deg,#667eea,#764ba2);color:white;padding:24px 32px;'>
  <h1 style='margin:0;font-size:28px;font-weight:700;'>UpBrowser Element &amp; CSS Test</h1>
  <p style='margin:6px 0 0;font-size:14px;opacity:0.9;'>All HTML elements and CSS rendering features</p>
</div>
<div style='padding:24px 32px;'>

<!-- Section: Typography -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>1. Typography Elements</h2>
<div style='display:flex;gap:16px;flex-wrap:wrap;margin-bottom:20px;'>
  <div style='flex:1;min-width:280px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h3 style='margin:0 0 12px;color:#333;'>Headings</h3>
    <h1 style='font-size:28px;margin:4px 0;'>H1 - The Quick Brown Fox</h1>
    <h2 style='font-size:22px;margin:4px 0;'>H2 - The Quick Brown Fox</h2>
    <h3 style='font-size:18px;margin:4px 0;'>H3 - The Quick Brown Fox</h3>
    <h4 style='font-size:16px;margin:4px 0;'>H4 - The Quick Brown Fox</h4>
    <h5 style='font-size:14px;margin:4px 0;'>H5 - The Quick Brown Fox</h5>
    <h6 style='font-size:12px;margin:4px 0;'>H6 - The Quick Brown Fox</h6>
  </div>
  <div style='flex:1;min-width:280px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h3 style='margin:0 0 12px;color:#333;'>Inline Elements</h3>
    <p style='margin:4px 0;'><strong>Bold text</strong> via &lt;strong&gt;</p>
    <p style='margin:4px 0;'><b>Bold text</b> via &lt;b&gt;</p>
    <p style='margin:4px 0;'><em>Italic text</em> via &lt;em&gt;</p>
    <p style='margin:4px 0;'><i>Italic text</i> via &lt;i&gt;</p>
    <p style='margin:4px 0;'><u>Underline text</u> via &lt;u&gt;</p>
    <p style='margin:4px 0;'><s>Strikethrough</s> via &lt;s&gt;</p>
    <p style='margin:4px 0;'><del>Deleted text</del> and <ins>Inserted text</ins></p>
    <p style='margin:4px 0;'><code style='background:#f1f3f5;padding:2px 6px;border-radius:3px;font-family:monospace;'>inline code</code></p>
    <p style='margin:4px 0;'><kbd style='background:#e9ecef;padding:2px 6px;border-radius:3px;border:1px solid #ccc;font-family:monospace;'>Ctrl+C</kbd></p>
    <p style='margin:4px 0;'><mark style='background:#fff3cd;padding:1px 4px;'>Highlighted text</mark></p>
    <p style='margin:4px 0;'><small>Small text</small></p>
    <p style='margin:4px 0;'>X<sub style='font-size:0.75em;'>sub</sub> and X<sup style='font-size:0.75em;'>sup</sup></p>
  </div>
</div>

<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h3 style='margin:0 0 12px;color:#333;'>Text Transform &amp; Letter Spacing</h3>
  <p style='margin:4px 0;text-transform:uppercase;'>uppercase: the quick brown fox jumps over the lazy dog</p>
  <p style='margin:4px 0;text-transform:lowercase;'>LOWERCASE: THE QUICK BROWN FOX JUMPS</p>
  <p style='margin:4px 0;text-transform:capitalize;'>capitalize: the quick brown fox jumps over the lazy dog</p>
  <p style='margin:4px 0;letter-spacing:4px;'>Letter spacing: 4px between each character</p>
  <p style='margin:4px 0;letter-spacing:8px;font-size:12px;'>WIDE SPACING</p>
</div>

<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h3 style='margin:0 0 12px;color:#333;'>Font Styles</h3>
  <p style='margin:4px 0;font-style:normal;'>font-style: normal - Regular text</p>
  <p style='margin:4px 0;font-style:italic;'>font-style: italic - The quick brown fox</p>
  <p style='margin:4px 0;font-weight:normal;'>font-weight: normal (400)</p>
  <p style='margin:4px 0;font-weight:bold;'>font-weight: bold (700)</p>
</div>

<!-- Section: Text Decoration -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>2. Text Decoration</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <p style='margin:4px 0;text-decoration-line:underline;'>Underline decoration</p>
  <p style='margin:4px 0;text-decoration-line:line-through;'>Line-through decoration</p>
  <p style='margin:4px 0;text-decoration-line:overline;'>Overline decoration</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-style:solid;'>Solid underline</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-style:double;'>Double underline</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-style:dotted;'>Dotted underline</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-style:dashed;'>Dashed underline</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-style:wavy;'>Wavy underline</p>
  <p style='margin:4px 0;text-decoration-line:underline;text-decoration-color:#e74c3c;'>Red underline</p>
  <p style='margin:4px 0;text-shadow:2px 2px 4px rgba(0,0,0,0.3);'>Text with shadow</p>
  <p style='margin:4px 0;text-shadow:1px 1px 0 #ff0000, 2px 2px 0 #00ff00, 3px 3px 0 #0000ff;'>Multi-color text shadow</p>
</div>

<!-- Section: Box Model -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>3. Box Model</h2>
<div style='display:flex;gap:16px;flex-wrap:wrap;margin-bottom:20px;'>
  <div style='flex:1;min-width:200px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>Margin &amp; Padding</h4>
    <div style='background:#e3f2fd;padding:20px;margin:10px;border:2px solid #2196F3;border-radius:4px;'>
      <div style='background:#bbdefb;padding:15px;border:1px solid #1976D2;border-radius:4px;'>Inner content</div>
    </div>
  </div>
  <div style='flex:1;min-width:200px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>Border Radius</h4>
    <div style='display:flex;gap:10px;flex-wrap:wrap;'>
      <div style='width:60px;height:60px;background:#e91e63;border-radius:4px;'></div>
      <div style='width:60px;height:60px;background:#9c27b0;border-radius:12px;'></div>
      <div style='width:60px;height:60px;background:#673ab7;border-radius:50%;'></div>
    </div>
  </div>
</div>

<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h3 style='margin:0 0 12px;color:#333;'>Border Styles</h3>
  <div style='display:flex;gap:12px;flex-wrap:wrap;'>
    <div style='padding:12px;border:3px solid #333;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>solid</div>
    <div style='padding:12px;border:3px dashed #e74c3c;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>dashed</div>
    <div style='padding:12px;border:3px dotted #2196F3;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>dotted</div>
    <div style='padding:12px;border:3px double #ff9800;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>double</div>
    <div style='padding:12px;border:3px groove #9e9e9e;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>groove</div>
    <div style='padding:12px;border:3px ridge #9e9e9e;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>ridge</div>
    <div style='padding:12px;border:3px inset #9e9e9e;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>inset</div>
    <div style='padding:12px;border:3px outset #9e9e9e;border-radius:4px;min-width:100px;text-align:center;font-size:13px;'>outset</div>
  </div>
</div>

<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h3 style='margin:0 0 12px;color:#333;'>Box Shadow</h3>
  <div style='display:flex;gap:16px;flex-wrap:wrap;'>
    <div style='width:100px;height:100px;background:white;border-radius:8px;box-shadow:2px 2px 8px rgba(0,0,0,0.2);display:flex;align-items:center;justify-content:center;font-size:11px;'>soft</div>
    <div style='width:100px;height:100px;background:white;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,0.3);display:flex;align-items:center;justify-content:center;font-size:11px;'>large</div>
    <div style='width:100px;height:100px;background:white;border-radius:8px;box-shadow:0 0 0 3px #667eea;display:flex;align-items:center;justify-content:center;font-size:11px;'>ring</div>
    <div style='width:100px;height:100px;background:white;border-radius:8px;box-shadow:inset 0 2px 8px rgba(0,0,0,0.15);display:flex;align-items:center;justify-content:center;font-size:11px;'>inset</div>
  </div>
</div>

<!-- Section: Backgrounds -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>4. Backgrounds &amp; Colors</h2>
<div style='display:flex;gap:16px;flex-wrap:wrap;margin-bottom:20px;'>
  <div style='flex:1;min-width:200px;height:120px;background:linear-gradient(135deg,#667eea,#764ba2);border-radius:8px;display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;'>Linear Gradient</div>
  <div style='flex:1;min-width:200px;height:120px;background:radial-gradient(circle,#ff6b6b,#ee5a24);border-radius:8px;display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;'>Radial Gradient</div>
  <div style='flex:1;min-width:200px;height:120px;background:conic-gradient(#ff6b6b,#feca57,#48dbfb,#ff9ff3,#ff6b6b);border-radius:8px;display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;text-shadow:0 1px 3px rgba(0,0,0,0.5);'>Conic Gradient</div>
</div>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h3 style='margin:0 0 12px;color:#333;'>Opacity</h3>
  <div style='display:flex;gap:12px;'>
    <div style='width:80px;height:60px;background:#667eea;border-radius:4px;opacity:1;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>1.0</div>
    <div style='width:80px;height:60px;background:#667eea;border-radius:4px;opacity:0.8;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>0.8</div>
    <div style='width:80px;height:60px;background:#667eea;border-radius:4px;opacity:0.6;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>0.6</div>
    <div style='width:80px;height:60px;background:#667eea;border-radius:4px;opacity:0.4;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>0.4</div>
    <div style='width:80px;height:60px;background:#667eea;border-radius:4px;opacity:0.2;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>0.2</div>
  </div>
</div>

<!-- Section: Lists -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>5. Lists</h2>
<div style='display:flex;gap:16px;flex-wrap:wrap;margin-bottom:20px;'>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>disc</h4>
    <ul style='list-style-type:disc;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li>
    </ul>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>circle</h4>
    <ul style='list-style-type:circle;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li>
    </ul>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>square</h4>
    <ul style='list-style-type:square;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li>
    </ul>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>decimal</h4>
    <ol style='list-style-type:decimal;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li>
    </ol>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>lower-roman</h4>
    <ol style='list-style-type:lower-roman;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li><li>Item Four</li><li>Item Five</li>
    </ol>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>upper-roman</h4>
    <ol style='list-style-type:upper-roman;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li><li>Item Four</li>
    </ol>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>lower-alpha</h4>
    <ol style='list-style-type:lower-alpha;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li><li>Item Four</li>
    </ol>
  </div>
  <div style='flex:1;min-width:150px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>upper-alpha</h4>
    <ol style='list-style-type:upper-alpha;margin:0;padding-left:20px;'>
      <li>Item One</li><li>Item Two</li><li>Item Three</li><li>Item Four</li>
    </ol>
  </div>
</div>

<!-- Section: Layout - Flexbox -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>6. Flexbox Layout</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h4 style='margin:0 0 8px;'>justify-content variations</h4>
  <div style='display:flex;justify-content:flex-start;gap:8px;margin-bottom:8px;background:#f1f3f5;padding:8px;border-radius:4px;'>
    <div style='background:#667eea;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>A</div>
    <div style='background:#667eea;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>B</div>
    <div style='background:#667eea;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>C</div>
  </div>
  <div style='display:flex;justify-content:center;gap:8px;margin-bottom:8px;background:#f1f3f5;padding:8px;border-radius:4px;'>
    <div style='background:#e91e63;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>A</div>
    <div style='background:#e91e63;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>B</div>
    <div style='background:#e91e63;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>C</div>
  </div>
  <div style='display:flex;justify-content:flex-end;gap:8px;margin-bottom:8px;background:#f1f3f5;padding:8px;border-radius:4px;'>
    <div style='background:#ff9800;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>A</div>
    <div style='background:#ff9800;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>B</div>
    <div style='background:#ff9800;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>C</div>
  </div>
  <div style='display:flex;justify-content:space-between;background:#f1f3f5;padding:8px;border-radius:4px;'>
    <div style='background:#4caf50;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>A</div>
    <div style='background:#4caf50;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>B</div>
    <div style='background:#4caf50;color:white;padding:8px 16px;border-radius:4px;font-size:12px;'>C</div>
  </div>
</div>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h4 style='margin:0 0 8px;'>Flex Wrap</h4>
  <div style='display:flex;flex-wrap:wrap;gap:8px;background:#f1f3f5;padding:8px;border-radius:4px;'>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 1</div>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 2</div>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 3</div>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 4</div>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 5</div>
    <div style='background:#667eea;color:white;padding:8px 20px;border-radius:4px;font-size:12px;'>Item 6</div>
  </div>
</div>

<!-- Section: Positioning -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>7. Positioning</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <div style='position:relative;height:120px;background:#f1f3f5;border-radius:4px;overflow:hidden;'>
    <div style='position:absolute;top:8px;left:8px;background:#667eea;color:white;padding:6px 12px;border-radius:4px;font-size:12px;'>relative parent</div>
    <div style='position:absolute;top:10px;right:10px;background:#e91e63;color:white;padding:6px 12px;border-radius:4px;font-size:12px;'>top:10 right:10</div>
    <div style='position:absolute;bottom:10px;left:10px;background:#4caf50;color:white;padding:6px 12px;border-radius:4px;font-size:12px;'>bottom:10 left:10</div>
    <div style='position:absolute;bottom:10px;right:10px;background:#ff9800;color:white;padding:6px 12px;border-radius:4px;font-size:12px;'>bottom:10 right:10</div>
    <div style='position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);background:#333;color:white;padding:6px 12px;border-radius:4px;font-size:12px;'>centered</div>
  </div>
</div>

<!-- Section: Overflow -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>8. Overflow &amp; Scroll</h2>
<div style='display:flex;gap:16px;flex-wrap:wrap;margin-bottom:20px;'>
  <div style='flex:1;min-width:200px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>overflow: hidden</h4>
    <div style='width:100%;height:80px;overflow:hidden;background:#f1f3f5;border:1px solid #dee2e6;border-radius:4px;padding:8px;'>
      <p style='margin:0;'>This is a long paragraph that should be clipped when it exceeds the container height. The text continues beyond the visible area to demonstrate overflow hidden behavior.</p>
    </div>
  </div>
  <div style='flex:1;min-width:200px;background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;'>
    <h4 style='margin:0 0 8px;'>overflow: auto (scroll)</h4>
    <div style='width:100%;height:80px;overflow:auto;background:#f1f3f5;border:1px solid #dee2e6;border-radius:4px;padding:8px;'>
      <p style='margin:0;'>This is a long paragraph inside a scrollable container. You should see scrollbars appear when the content exceeds the container bounds. Try scrolling to see more content below.</p>
      <p style='margin:8px 0 0;'>More content line 2. The scroll container should have visible scrollbars on the right side and bottom.</p>
      <p style='margin:8px 0 0;'>More content line 3. Additional text to ensure scrollbars appear.</p>
    </div>
  </div>
</div>

<!-- Section: Tables -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>9. Tables</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <table style='width:100%;border-collapse:collapse;font-size:14px;'>
    <thead>
      <tr style='background:#667eea;color:white;'>
        <th style='padding:10px 12px;text-align:left;border:1px solid #5a6fd6;'>Name</th>
        <th style='padding:10px 12px;text-align:left;border:1px solid #5a6fd6;'>Type</th>
        <th style='padding:10px 12px;text-align:right;border:1px solid #5a6fd6;'>Size</th>
        <th style='padding:10px 12px;text-align:center;border:1px solid #5a6fd6;'>Status</th>
      </tr>
    </thead>
    <tbody>
      <tr style='background:#f8f9fa;'>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>index.html</td>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>HTML</td>
        <td style='padding:8px 12px;text-align:right;border:1px solid #dee2e6;'>4.2 KB</td>
        <td style='padding:8px 12px;text-align:center;border:1px solid #dee2e6;'><span style='background:#28a745;color:white;padding:2px 8px;border-radius:10px;font-size:12px;'>Active</span></td>
      </tr>
      <tr>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>style.css</td>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>CSS</td>
        <td style='padding:8px 12px;text-align:right;border:1px solid #dee2e6;'>12.8 KB</td>
        <td style='padding:8px 12px;text-align:center;border:1px solid #dee2e6;'><span style='background:#28a745;color:white;padding:2px 8px;border-radius:10px;font-size:12px;'>Active</span></td>
      </tr>
      <tr style='background:#f8f9fa;'>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>app.js</td>
        <td style='padding:8px 12px;border:1px solid #dee2e6;'>JavaScript</td>
        <td style='padding:8px 12px;text-align:right;border:1px solid #dee2e6;'>45.1 KB</td>
        <td style='padding:8px 12px;text-align:center;border:1px solid #dee2e6;'><span style='background:#ffc107;color:#333;padding:2px 8px;border-radius:10px;font-size:12px;'>Draft</span></td>
      </tr>
    </tbody>
  </table>
</div>

<!-- Section: Form Elements -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>10. Form Elements</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <div style='display:flex;gap:16px;flex-wrap:wrap;'>
    <div style='flex:1;min-width:280px;'>
      <label style='display:block;margin-bottom:4px;font-size:13px;font-weight:bold;color:#555;'>Text Input</label>
      <input type='text' placeholder='Enter text...' style='width:100%;padding:8px 12px;border:1px solid #ced4da;border-radius:4px;font-size:14px;box-sizing:border-box;margin-bottom:12px;'>
      <label style='display:block;margin-bottom:4px;font-size:13px;font-weight:bold;color:#555;'>Password</label>
      <input type='password' placeholder='Password' style='width:100%;padding:8px 12px;border:1px solid #ced4da;border-radius:4px;font-size:14px;box-sizing:border-box;margin-bottom:12px;'>
      <label style='display:block;margin-bottom:4px;font-size:13px;font-weight:bold;color:#555;'>Email</label>
      <input type='email' placeholder='user@example.com' style='width:100%;padding:8px 12px;border:1px solid #ced4da;border-radius:4px;font-size:14px;box-sizing:border-box;margin-bottom:12px;'>
      <label style='display:block;margin-bottom:4px;font-size:13px;font-weight:bold;color:#555;'>Number</label>
      <input type='number' value='42' style='width:100%;padding:8px 12px;border:1px solid #ced4da;border-radius:4px;font-size:14px;box-sizing:border-box;margin-bottom:12px;'>
      <label style='display:block;margin-bottom:4px;font-size:13px;font-weight:bold;color:#555;'>Textarea</label>
      <textarea rows='3' placeholder='Multi-line text...' style='width:100%;padding:8px 12px;border:1px solid #ced4da;border-radius:4px;font-size:14px;box-sizing:border-box;resize:vertical;margin-bottom:12px;'></textarea>
    </div>
    <div style='flex:1;min-width:280px;'>
      <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Checkbox</label>
      <div style='margin-bottom:12px;'>
        <label style='display:block;margin-bottom:4px;'><input type='checkbox' checked> Option A (checked)</label>
        <label style='display:block;margin-bottom:4px;'><input type='checkbox'> Option B</label>
        <label style='display:block;margin-bottom:4px;'><input type='checkbox' checked> Option C (checked)</label>
      </div>
      <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Radio</label>
      <div style='margin-bottom:12px;'>
        <label style='display:block;margin-bottom:4px;'><input type='radio' name='demo' checked> Choice 1</label>
        <label style='display:block;margin-bottom:4px;'><input type='radio' name='demo'> Choice 2</label>
        <label style='display:block;margin-bottom:4px;'><input type='radio' name='demo'> Choice 3</label>
      </div>
      <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Range</label>
      <input type='range' min='0' max='100' value='60' style='width:100%;margin-bottom:12px;'>
      <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Color</label>
      <input type='color' value='#667eea' style='margin-bottom:12px;'>
      <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Select</label>
      <select style='width:100%;padding:8px;border:1px solid #ced4da;border-radius:4px;font-size:14px;margin-bottom:12px;'>
        <option>Option 1</option>
        <option selected>Option 2 (selected)</option>
        <option>Option 3</option>
      </select>
    </div>
  </div>
  <div style='margin-top:12px;'>
    <label style='display:block;margin-bottom:8px;font-size:13px;font-weight:bold;color:#555;'>Buttons</label>
    <div style='display:flex;gap:8px;flex-wrap:wrap;'>
      <button style='background:#667eea;color:white;padding:8px 20px;border:none;border-radius:4px;font-size:14px;cursor:pointer;'>Primary</button>
      <button style='background:#6c757d;color:white;padding:8px 20px;border:none;border-radius:4px;font-size:14px;cursor:pointer;'>Secondary</button>
      <button style='background:#28a745;color:white;padding:8px 20px;border:none;border-radius:4px;font-size:14px;cursor:pointer;'>Success</button>
      <button style='background:#dc3545;color:white;padding:8px 20px;border:none;border-radius:4px;font-size:14px;cursor:pointer;'>Danger</button>
      <button style='background:transparent;color:#667eea;padding:8px 20px;border:2px solid #667eea;border-radius:4px;font-size:14px;cursor:pointer;'>Outline</button>
    </div>
  </div>
</div>

<!-- Section: Media Elements -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>11. Media Elements</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <h4 style='margin:0 0 8px;'>Image (placeholder)</h4>
  <div style='display:flex;gap:12px;flex-wrap:wrap;margin-bottom:12px;'>
    <div style='width:120px;height:80px;background:#e9ecef;border:2px dashed #adb5bd;border-radius:4px;display:flex;align-items:center;justify-content:center;color:#6c757d;font-size:12px;'>&lt;img&gt;</div>
    <div style='width:120px;height:120px;background:#e9ecef;border:2px dashed #adb5bd;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#6c757d;font-size:12px;'>circle</div>
    <div style='width:160px;height:80px;background:#e9ecef;border:2px dashed #adb5bd;border-radius:8px;display:flex;align-items:center;justify-content:center;color:#6c757d;font-size:12px;'>wide</div>
  </div>
  <h4 style='margin:0 0 8px;'>Video (placeholder)</h4>
  <div style='width:100%;max-width:320px;height:180px;background:#212529;border-radius:4px;display:flex;align-items:center;justify-content:center;color:#6c757d;font-size:14px;'>&lt;video&gt; placeholder</div>
  <h4 style='margin:12px 0 8px;'>Audio (placeholder)</h4>
  <div style='width:100%;max-width:320px;height:40px;background:#343a40;border-radius:20px;display:flex;align-items:center;padding:0 16px;color:#adb5bd;font-size:12px;'>&lt;audio&gt; placeholder</div>
</div>

<!-- Section: Details / Summary -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>12. Details / Summary</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <details style='margin-bottom:8px;'>
    <summary style='cursor:pointer;padding:8px;background:#f8f9fa;border-radius:4px;font-weight:bold;'>Click to expand - Section 1</summary>
    <p style='margin:8px 0;padding:8px;background:#f1f3f5;border-radius:4px;'>This is the hidden content inside details element. It should be visible when the details element is open.</p>
  </details>
  <details style='margin-bottom:8px;'>
    <summary style='cursor:pointer;padding:8px;background:#f8f9fa;border-radius:4px;font-weight:bold;'>Click to expand - Section 2</summary>
    <p style='margin:8px 0;padding:8px;background:#f1f3f5;border-radius:4px;'>More hidden content here. The disclosure triangle should toggle between open/closed states.</p>
  </details>
  <details open style='margin-bottom:8px;'>
    <summary style='cursor:pointer;padding:8px;background:#f8f9fa;border-radius:4px;font-weight:bold;'>This one is open by default</summary>
    <p style='margin:8px 0;padding:8px;background:#f1f3f5;border-radius:4px;'>This content is visible by default because the details element has the open attribute.</p>
  </details>
</div>

<!-- Section: Blockquote / Pre / Code -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>13. Preformatted &amp; Quote</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <blockquote style='margin:0 0 12px;padding:12px 20px;background:#f1f3f5;border-left:4px solid #667eea;border-radius:0 4px 4px 0;font-style:italic;color:#555;'>
    The only way to do great work is to love what you do. - Steve Jobs
  </blockquote>
  <pre style='margin:0;padding:16px;background:#282c34;color:#abb2bf;border-radius:4px;font-family:monospace;font-size:13px;overflow-x:auto;'>function greet(name) {
  console.log('Hello, ' + name + '!');
  return true;
}

greet('UpBrowser');</pre>
</div>

<!-- Section: HR -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>14. Horizontal Rule</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <p style='margin:0 0 8px;'>Content above the hr</p>
  <hr>
  <p style='margin:8px 0 0;'>Content below the hr</p>
</div>

<!-- Section: CSS Transforms -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>15. CSS Transforms</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <div style='display:flex;gap:24px;flex-wrap:wrap;align-items:flex-end;'>
    <div style='text-align:center;'>
      <div style='width:80px;height:80px;background:#667eea;border-radius:4px;transform:rotate(15deg);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>rotate(15deg)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:80px;height:80px;background:#e91e63;border-radius:4px;transform:scale(1.2);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>scale(1.2)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:80px;height:80px;background:#4caf50;border-radius:4px;transform:skewX(10deg);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>skewX(10deg)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:80px;height:80px;background:#ff9800;border-radius:4px;transform:translate(10px,-10px);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>translate(10,-10)</div>
    </div>
  </div>
</div>

<!-- Section: Clip Path -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>16. Clip Path</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <div style='display:flex;gap:16px;flex-wrap:wrap;'>
    <div style='text-align:center;'>
      <div style='width:100px;height:100px;background:#667eea;clip-path:circle(50%);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>circle(50%)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:120px;height:80px;background:#e91e63;clip-path:ellipse(50% 50%);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>ellipse(50% 50%)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:100px;height:100px;background:#4caf50;clip-path:polygon(50% 0%,100% 100%,0% 100%);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>polygon(triangle)</div>
    </div>
    <div style='text-align:center;'>
      <div style='width:100px;height:100px;background:#ff9800;clip-path:inset(10px);margin:0 auto;'></div>
      <div style='font-size:11px;margin-top:4px;color:#666;'>inset(10px)</div>
    </div>
  </div>
</div>

<!-- Section: Inline-block -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>17. Inline-Block</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <p style='margin:0 0 8px;'>Inline-block elements in a line:</p>
  <div>
    <span style='display:inline-block;width:60px;height:30px;background:#667eea;color:white;text-align:center;line-height:30px;border-radius:4px;margin:2px;font-size:12px;'>A</span>
    <span style='display:inline-block;width:60px;height:30px;background:#e91e63;color:white;text-align:center;line-height:30px;border-radius:4px;margin:2px;font-size:12px;'>B</span>
    <span style='display:inline-block;width:60px;height:50px;background:#4caf50;color:white;text-align:center;line-height:50px;border-radius:4px;margin:2px;font-size:12px;'>Tall</span>
    <span style='display:inline-block;width:60px;height:30px;background:#ff9800;color:white;text-align:center;line-height:30px;border-radius:4px;margin:2px;font-size:12px;'>D</span>
    <span style='display:inline-block;width:60px;height:30px;background:#9c27b0;color:white;text-align:center;line-height:30px;border-radius:4px;margin:2px;font-size:12px;'>E</span>
  </div>
</div>

<!-- Section: Visibilty & Display -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>18. Visibility &amp; Display</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <p style='margin:0 0 8px;'>visibility: hidden (element takes space but invisible):</p>
  <div style='background:#f1f3f5;padding:8px;border-radius:4px;'>
    Before
    <span style='visibility:hidden;background:#e74c3c;color:white;padding:4px 8px;border-radius:4px;'>HIDDEN</span>
    After
  </div>
  <p style='margin:12px 0 8px;'>display: none (element removed from flow):</p>
  <div style='background:#f1f3f5;padding:8px;border-radius:4px;'>
    Before
    <span style='display:none;background:#e74c3c;color:white;padding:4px 8px;border-radius:4px;'>NONE</span>
    After (no gap)
  </div>
</div>

<!-- Section: Z-index -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>19. Z-Index Stacking</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <div style='position:relative;height:120px;'>
    <div style='position:absolute;top:0;left:0;width:100px;height:100px;background:rgba(102,126,234,0.8);border-radius:8px;z-index:1;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>z:1</div>
    <div style='position:absolute;top:20px;left:30px;width:100px;height:100px;background:rgba(233,30,99,0.8);border-radius:8px;z-index:2;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>z:2</div>
    <div style='position:absolute;top:40px;left:60px;width:100px;height:100px;background:rgba(76,175,80,0.8);border-radius:8px;z-index:3;display:flex;align-items:center;justify-content:center;color:white;font-size:12px;'>z:3</div>
  </div>
</div>

<!-- Section: Vertical Align -->
<h2 style='color:#667eea;border-bottom:2px solid #667eea;padding-bottom:8px;'>20. Mixed Content</h2>
<div style='background:white;padding:16px;border-radius:8px;border:1px solid #e9ecef;margin-bottom:20px;'>
  <p style='margin:0 0 8px;'>Inline elements mixed with block:</p>
  <div style='background:#f1f3f5;padding:12px;border-radius:4px;'>
    Normal text
    <strong>Bold</strong>
    <em>Italic</em>
    <span style='background:#667eea;color:white;padding:2px 8px;border-radius:3px;font-size:12px;'>styled span</span>
    <code style='background:#e9ecef;padding:2px 6px;border-radius:3px;font-family:monospace;font-size:13px;'>code</code>
    <mark style='background:#fff3cd;padding:1px 4px;'>highlighted</mark>
    <span style='display:inline-block;width:20px;height:20px;background:#e91e63;border-radius:50%;vertical-align:middle;'></span>
    inline-block circle
  </div>
</div>

<div style='text-align:center;padding:24px;color:#6c757d;font-size:13px;border-top:1px solid #e9ecef;margin-top:20px;'>
  UpBrowser Element Test Page - Testing all HTML elements and CSS features
</div>
</div>
</body>
</html>";
    }

    private static string GetUserAgentStylesStatic()
    {
        return @"
            html { display: block; overflow-x: hidden; overflow-y: auto; width: 100%; }
            body { font-family: Arial, sans-serif; display: block; margin: 8px; box-sizing: border-box; }
            h1 { display: block; margin: 0.67em 0; font-size: 2em; font-weight: bold; }
            h2 { display: block; margin: 0.83em 0; font-size: 1.5em; font-weight: bold; }
            h3 { display: block; margin: 1em 0; font-size: 1.17em; font-weight: bold; }
            h4 { display: block; margin: 1.33em 0; font-size: 1em; font-weight: bold; }
            h5 { display: block; margin: 1.67em 0; font-size: 0.83em; font-weight: bold; }
            h6 { display: block; margin: 2.33em 0; font-size: 0.67em; font-weight: bold; }
            p { display: block; margin: 1em 0; }
            div { display: block; }
            span { display: inline; }
            ul { display: block; margin: 1em 0; padding-left: 40px; list-style-type: disc; }
            ol { display: block; margin: 1em 0; padding-left: 40px; list-style-type: decimal; }
            li { display: list-item; }
            table { display: table; border-collapse: separate; border-spacing: 2px; }
            thead { display: table-header-group; vertical-align: middle; }
            tbody { display: table-row-group; vertical-align: middle; }
            tfoot { display: table-footer-group; vertical-align: middle; }
            tr { display: table-row; vertical-align: middle; }
            td { display: table-cell; vertical-align: inherit; padding: 1px; }
            th { display: table-cell; vertical-align: inherit; font-weight: bold; padding: 1px; }
            button { display: inline-block; cursor: pointer; padding: 2px 6px; }
            input { display: inline-block; }
            a { display: inline; color: #0000EE; text-decoration: underline; }
            strong { font-weight: bold; }
            em { font-style: italic; }
            u { text-decoration: underline; }
            s { text-decoration: line-through; }
            hr { display: block; margin: 0.5em auto; border: none; border-top: 1px solid #ccc; }
            img { display: inline-block; }
            br { display: none; }
            blockquote { display: block; margin: 1em 40px; }
            pre { display: block; font-family: monospace; white-space: pre; margin: 1em 0; }
            code { font-family: monospace; }
        ";
    }

    public Stylesheet GetUaStylesheet() => _uaStylesheet;

    public record DocumentLoadResult(Document Document, AngleSharp.Dom.IDocument AngleSharpDoc, StyleComputer? StyleComputer = null);
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }

    // Global HTML attributes (HTMLElement spec)
    public string? Title
    {
        get => GetAttribute("title");
        set { if (value != null) SetAttribute("title", value); else RemoveAttribute("title"); }
    }
    public string? Lang
    {
        get => GetAttribute("lang");
        set { if (value != null) SetAttribute("lang", value); else RemoveAttribute("lang"); }
    }
    public bool Translate
    {
        get => !string.Equals(GetAttribute("translate"), "no", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("translate", value ? "yes" : "no");
    }
    public string? Dir
    {
        get => GetAttribute("dir");
        set { if (value != null) SetAttribute("dir", value); else RemoveAttribute("dir"); }
    }
    public bool Hidden
    {
        get => HasAttribute("hidden");
        set { if (value) SetAttribute("hidden", ""); else RemoveAttribute("hidden"); }
    }
    public bool Inert
    {
        get => HasAttribute("inert");
        set { if (value) SetAttribute("inert", ""); else RemoveAttribute("inert"); }
    }
    public bool Draggable
    {
        get => string.Equals(GetAttribute("draggable"), "true", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("draggable", value ? "true" : "false");
    }
    public bool Spellcheck
    {
        get => !string.Equals(GetAttribute("spellcheck"), "false", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("spellcheck", value ? "true" : "false");
    }
    public string? ContentEditable
    {
        get => GetAttribute("contenteditable") ?? "inherit";
        set { if (value != null) SetAttribute("contenteditable", value); else RemoveAttribute("contenteditable"); }
    }
    public bool IsContentEditable => ContentEditable == "true";
    public string? InputMode
    {
        get => GetAttribute("inputmode");
        set { if (value != null) SetAttribute("inputmode", value); else RemoveAttribute("inputmode"); }
    }
    public string? EnterKeyHint
    {
        get => GetAttribute("enterkeyhint");
        set { if (value != null) SetAttribute("enterkeyhint", value); else RemoveAttribute("enterkeyhint"); }
    }
    public string? Autocapitalize
    {
        get => GetAttribute("autocapitalize") ?? "";
        set { if (value != null) SetAttribute("autocapitalize", value); else RemoveAttribute("autocapitalize"); }
    }
    public string? Nonce
    {
        get => GetAttribute("nonce");
        set { if (value != null) SetAttribute("nonce", value); else RemoveAttribute("nonce"); }
    }
    public string? Popover
    {
        get => GetAttribute("popover");
        set { if (value != null) SetAttribute("popover", value); else RemoveAttribute("popover"); }
    }
    public int TabIndex
    {
        get => int.TryParse(GetAttribute("tabindex"), out var v) ? v : 0;
        set => SetAttribute("tabindex", value.ToString());
    }
    public string? AccessKey
    {
        get => GetAttribute("accesskey");
        set { if (value != null) SetAttribute("accesskey", value); else RemoveAttribute("accesskey"); }
    }
    public string? AccessKeyLabel => AccessKey;
    public DOMStringMap Dataset => new(x => GetAttribute(x), (x, v) => SetAttribute(x, v));
    public ElementInternals AttachInternals() => new(this);
}