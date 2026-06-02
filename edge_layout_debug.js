// ============================================================
// UpBrowser Layout Debug Script for Edge/Chrome DevTools Console
// ============================================================
// Paste this into Edge DevTools Console to generate layout info
// that can be compared with UpBrowser's layout_debug.txt
// ============================================================

(function() {
    const results = [];
    const SEP = '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';

    function fmt(n) { return Math.round(n * 10) / 10; }
    function fmtColor(c) {
        const m = c.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
        if (m) return `#${(+m[1]).toString(16).padStart(2,'0')}${(+m[2]).toString(16).padStart(2,'0')}${(+m[3]).toString(16).padStart(2,'0')}`;
        return c;
    }

    // 1. Document Info
    results.push(SEP);
    results.push(' 1. DOCUMENT INFO (Edge/Chrome)');
    results.push(SEP);
    results.push(`    Viewport:     ${window.innerWidth} x ${window.innerHeight} px`);
    results.push(`    URL:          ${location.href}`);
    results.push(`    Title:        ${document.title}`);
    results.push(`    Root Element: ${document.documentElement?.tagName}`);
    results.push(`    Body Element: ${document.body?.tagName}`);

    const allEls = document.querySelectorAll('*');
    let block=0, inline=0, flex=0, grid=0, none=0;
    allEls.forEach(el => {
        const d = getComputedStyle(el).display;
        if (d === 'block' || d === 'list-item') block++;
        else if (d === 'inline' || d === 'inline-block' || d === 'inline-flex') inline++;
        else if (d === 'flex' || d === 'inline-flex') flex++;
        else if (d === 'grid' || d === 'inline-grid') grid++;
        else if (d === 'none') none++;
    });
    results.push(`    Total Elements: ${allEls.length}`);
    results.push(`    Display: block=${block} inline=${inline} flex=${flex} grid=${grid} none=${none}`);
    results.push('');

    // 2. Full Element Styles
    results.push(SEP);
    results.push(' 2. ELEMENT STYLES (Full Detail)');
    results.push(SEP);

    function dumpElement(el, depth) {
        if (depth > 10) return;
        const indent = '  '.repeat(depth);
        const cs = getComputedStyle(el);
        const tag = el.tagName.toLowerCase();
        const id = el.id ? ` #${el.id}` : '';
        const cls = el.className && typeof el.className === 'string' ? ` .${el.className.split(' ')[0]}` : '';

        results.push(`${indent}<${tag}${id}${cls}>`);
        results.push(`${indent}  [display]       ${cs.display}`);
        results.push(`${indent}  [position]      ${cs.position}` + (cs.position !== 'static' ? ` (top=${cs.top} left=${cs.left} right=${cs.right} bottom=${cs.bottom})` : ''));
        results.push(`${indent}  [float]         ${cs.float}  clear=${cs.clear}`);
        results.push(`${indent}  [box-sizing]    ${cs.boxSizing}`);
        results.push(`${indent}  [width]         ${cs.width}  min=${cs.minWidth}  max=${cs.maxWidth}`);
        results.push(`${indent}  [height]        ${cs.height}  min=${cs.minHeight}  max=${cs.maxHeight}`);
        results.push(`${indent}  [margin]        T=${cs.marginTop} R=${cs.marginRight} B=${cs.marginBottom} L=${cs.marginLeft}`);
        results.push(`${indent}  [padding]       T=${cs.paddingTop} R=${cs.paddingRight} B=${cs.paddingBottom} L=${cs.paddingLeft}`);
        results.push(`${indent}  [border]        T=${cs.borderTopWidth}/${cs.borderTopStyle}/${fmtColor(cs.borderTopColor)} R=${cs.borderRightWidth}/${cs.borderRightStyle}/${fmtColor(cs.borderRightColor)} B=${cs.borderBottomWidth}/${cs.borderBottomStyle}/${fmtColor(cs.borderBottomColor)} L=${cs.borderLeftWidth}/${cs.borderLeftStyle}/${fmtColor(cs.borderLeftColor)}`);
        results.push(`${indent}  [border-radius] TL=${cs.borderTopLeftRadius} TR=${cs.borderTopRightRadius} BR=${cs.borderBottomRightRadius} BL=${cs.borderBottomLeftRadius}`);
        results.push(`${indent}  [color]         ${fmtColor(cs.color)}  opacity=${cs.opacity}`);
        results.push(`${indent}  [font]          ${cs.fontFamily} size=${cs.fontSize} weight=${cs.fontWeight} style=${cs.fontStyle} lh=${cs.lineHeight}`);
        results.push(`${indent}  [text]          align=${cs.textAlign} decoration=${cs.textDecoration} transform=${cs.textTransform} overflow=${cs.textOverflow} wrap=${cs.whiteSpace}`);
        results.push(`${indent}  [bg]            color=${fmtColor(cs.backgroundColor)} image=${cs.backgroundImage?.substring(0,60)||'none'} size=${cs.backgroundSize} repeat=${cs.backgroundRepeat}`);
        results.push(`${indent}  [overflow]      ${cs.overflow} x=${cs.overflowX} y=${cs.overflowY}  visibility=${cs.visibility}`);
        results.push(`${indent}  [z-index]       ${cs.zIndex}`);
        results.push(`${indent}  [flex]          dir=${cs.flexDirection} wrap=${cs.flexWrap} grow=${cs.flexGrow} shrink=${cs.flexShrink} basis=${cs.flexBasis}`);
        results.push(`${indent}  [justify]       ${cs.justifyContent}  align=${cs.alignItems}  self=${cs.alignSelf}`);
        if (cs.transform !== 'none') results.push(`${indent}  [transform]     ${cs.transform}  origin=${cs.transformOrigin}`);
        if (cs.filter !== 'none') results.push(`${indent}  [filter]        ${cs.filter}`);
        if (cs.clipPath !== 'none') results.push(`${indent}  [clip-path]     ${cs.clipPath}`);

        // Box rect
        const rect = el.getBoundingClientRect();
        results.push(`${indent}  [rect]          L=${fmt(rect.left)} T=${fmt(rect.top)} R=${fmt(rect.right)} B=${fmt(rect.bottom)} W=${fmt(rect.width)} H=${fmt(rect.height)}`);

        Array.from(el.children).forEach(child => {
            if (child.nodeType === 1) dumpElement(child, depth + 1);
        });
    }

    const root = document.documentElement;
    dumpElement(root, 0);
    results.push('');

    // 3. Stacking Contexts
    results.push(SEP);
    results.push(' 3. STACKING CONTEXTS');
    results.push(SEP);
    allEls.forEach(el => {
        const cs = getComputedStyle(el);
        const creates = cs.position === 'absolute' || cs.position === 'fixed' ||
            (cs.position === 'relative' && cs.zIndex !== 'auto') ||
            cs.opacity < 1 || cs.transform !== 'none' || cs.isolation === 'isolate';
        if (creates) {
            const tag = el.tagName.toLowerCase();
            const id = el.id ? `#${el.id}` : '';
            results.push(`  STACKING <${tag}${id}> z=${cs.zIndex} pos=${cs.position} opacity=${cs.opacity} transform=${cs.transform !== 'none'}`);
        }
    });
    results.push('');

    // 4. Positioned Elements
    results.push(SEP);
    results.push(' 4. POSITIONED ELEMENTS');
    results.push(SEP);
    allEls.forEach(el => {
        const cs = getComputedStyle(el);
        if (cs.position !== 'static') {
            const tag = el.tagName.toLowerCase();
            const id = el.id ? `#${el.id}` : '';
            const rect = el.getBoundingClientRect();
            results.push(`  <${tag}${id}> position=${cs.position} z=${cs.zIndex} rect=W:${fmt(rect.width)} H:${fmt(rect.height)}`);
        }
    });
    results.push('');

    // 5. Flex Layout
    results.push(SEP);
    results.push(' 5. FLEX LAYOUT');
    results.push(SEP);
    allEls.forEach(el => {
        const cs = getComputedStyle(el);
        if (cs.display === 'flex' || cs.display === 'inline-flex') {
            const tag = el.tagName.toLowerCase();
            const id = el.id ? `#${el.id}` : '';
            results.push(`  FLEX <${tag}${id}> dir=${cs.flexDirection} wrap=${cs.flexWrap} justify=${cs.justifyContent} align=${cs.alignItems}`);
            Array.from(el.children).forEach(child => {
                if (child.nodeType === 1) {
                    const ccs = getComputedStyle(child);
                    const ctag = child.tagName.toLowerCase();
                    const cid = child.id ? `#${child.id}` : '';
                    results.push(`    └─ <${ctag}${cid}> grow=${ccs.flexGrow} shrink=${ccs.flexShrink} basis=${ccs.flexBasis} order=${ccs.order}`);
                }
            });
        }
    });
    results.push('');

    // 6. Z-Index Map
    results.push(SEP);
    results.push(' 6. Z-INDEX MAP');
    results.push(SEP);
    allEls.forEach(el => {
        const cs = getComputedStyle(el);
        if (cs.zIndex !== 'auto') {
            const tag = el.tagName.toLowerCase();
            const id = el.id ? `#${el.id}` : '';
            const rect = el.getBoundingClientRect();
            results.push(`  z=${cs.zIndex.padStart(4)}  <${tag}${id}> @ ${fmt(rect.left)},${fmt(rect.top)} pos=${cs.position}`);
        }
    });
    results.push('');

    // 7. Errors & Warnings
    results.push(SEP);
    results.push(' 7. ERRORS & WARNINGS');
    results.push(SEP);
    allEls.forEach(el => {
        const cs = getComputedStyle(el);
        const rect = el.getBoundingClientRect();
        const tag = el.tagName.toLowerCase();
        const id = el.id ? `#${el.id}` : '';
        if (cs.display !== 'none' && rect.width <= 0 && rect.height <= 0) {
            results.push(`  ⚠ ZERO-SIZE <${tag}${id}> display=${cs.display}`);
        }
        if (cs.position === 'absolute' && cs.top === 'auto' && cs.left === 'auto' && cs.right === 'auto' && cs.bottom === 'auto') {
            if (rect.width <= 0 && rect.height <= 0)
                results.push(`  ⚠ ZERO-SIZE absolute <${tag}${id}> (all offsets auto)`);
        }
    });

    results.push('');
    results.push('══════════════════════════════════════════════════════════════════════════════');
    results.push(' END OF EDGE/CHROME REPORT');
    results.push('══════════════════════════════════════════════════════════════════════════════');

    const report = results.join('\n');
    console.log(report);

    // Also copy to clipboard if available
    if (navigator.clipboard) {
        navigator.clipboard.writeText(report).then(() => {
            console.log('✅ Report copied to clipboard! Paste into a .txt file for comparison.');
        }).catch(() => {
            console.log('📋 Copy the above text manually for comparison with UpBrowser layout_debug.txt');
        });
    } else {
        console.log('📋 Copy the above text manually for comparison with UpBrowser layout_debug.txt');
    }

    return report;
})();
