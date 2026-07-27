(function() {
    var g = typeof globalThis !== 'undefined' ? globalThis : this;
    g.__g_cbid = 0; g.__g_cbs = {}; g.__g_fnMap = typeof WeakMap !== 'undefined' ? new WeakMap() : {};
    g.__g_store = function(fn) {
        var id = g.__g_fnMap.get ? g.__g_fnMap.get(fn) : undefined;
        if (id !== undefined) return id;
        id = ++g.__g_cbid; g.__g_cbs[id] = fn;
        if (g.__g_fnMap.set) g.__g_fnMap.set(fn, id);
        return id;
    };
    g.__g_invoke = function(id, arg) {
        var fn = g.__g_cbs[id];
        if (fn) { return arg !== undefined ? fn(arg) : fn(); }
    };
    g.__g_remove = function(id) { delete g.__g_cbs[id]; };

    // === DOM Element Proxy ===
    g.__createEl = function(id) {
        return {
            __id: id, __type: 'Element',
            get className() { return __ipc('dom_getPropertyValue', JSON.stringify([this.__id, 'className'])); },
            set className(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'className', v])); },
            get id() { return __ipc('dom_getPropertyValue', JSON.stringify([this.__id, 'id'])); },
            set id(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'id', v])); },
            get tagName() { return __ipc('dom_getTag', JSON.stringify([this.__id])); },
            get textContent() { return __ipc('dom_getTextContent', JSON.stringify([this.__id])); },
            set textContent(v) { __ipc('dom_setTextContent', JSON.stringify([this.__id, v])); },
            get innerHTML() { return __ipc('dom_getInnerHTML', JSON.stringify([this.__id])); },
            set innerHTML(v) { __ipc('dom_setInnerHTML', JSON.stringify([this.__id, v])); },
            get outerHTML() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'outerHTML'])); },
            get value() { return __ipc('dom_getValue', JSON.stringify([this.__id])); },
            set value(v) { __ipc('dom_setValue', JSON.stringify([this.__id, v])); },
            get nodeType() { return parseInt(__ipc('dom_getNodeType', JSON.stringify([this.__id]))); },
            get isConnected() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'isConnected'])) === 'true'; },
            get offsetWidth() { return parseFloat(__ipc('dom_getOffsetWidth', JSON.stringify([this.__id]))); },
            get offsetHeight() { return parseFloat(__ipc('dom_getOffsetHeight', JSON.stringify([this.__id]))); },
            get clientWidth() { return parseFloat(__ipc('dom_getClientWidth', JSON.stringify([this.__id]))); },
            get clientHeight() { return parseFloat(__ipc('dom_getClientHeight', JSON.stringify([this.__id]))); },
            get offsetTop() { return parseFloat(__ipc('dom_getOffsetTop', JSON.stringify([this.__id]))); },
            get offsetLeft() { return parseFloat(__ipc('dom_getOffsetLeft', JSON.stringify([this.__id]))); },
            get scrollTop() { return parseFloat(__ipc('dom_getScrollTop', JSON.stringify([this.__id]))); },
            set scrollTop(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'scrollTop', v])); },
            get scrollLeft() { return parseFloat(__ipc('dom_getScrollLeft', JSON.stringify([this.__id]))); },
            set scrollLeft(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'scrollLeft', v])); },
            get childElementCount() { return parseInt(__ipc('dom_getChildElementCount', JSON.stringify([this.__id]))); },
            get children() { var ids = JSON.parse(__ipc('dom_getChildren', JSON.stringify([this.__id]))); return ids.map(function(i) { return g.__getEl(i); }); },
            get childNodes() { var ids = JSON.parse(__ipc('dom_getChildNodes', JSON.stringify([this.__id]))); return ids.map(function(i) { return g.__getEl(i); }); },
            get parentElement() { var r = __ipc('dom_getParent', JSON.stringify([this.__id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            get previousElementSibling() { var r = __ipc('dom_previousSibling', JSON.stringify([this.__id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            get nextElementSibling() { var r = __ipc('dom_nextSibling', JSON.stringify([this.__id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            get firstElementChild() { var r = __ipc('dom_getFirstElementChild', JSON.stringify([this.__id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            get lastElementChild() { var r = __ipc('dom_getLastElementChild', JSON.stringify([this.__id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            get hidden() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'hidden'])) === 'true'; },
            set hidden(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'hidden', v ? 'true' : 'false'])); },
            get draggable() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'draggable'])) === 'true'; },
            set draggable(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'draggable', v ? 'true' : 'false'])); },
            get disabled() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'disabled'])) === 'true'; },
            set disabled(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'disabled', v ? 'true' : 'false'])); },
            get readOnly() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'readOnly'])) === 'true'; },
            set readOnly(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'readOnly', v ? 'true' : 'false'])); },
            get required() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'required'])) === 'true'; },
            set required(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'required', v ? 'true' : 'false'])); },
            get checked() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'checked'])) === 'true'; },
            set checked(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'checked', v ? 'true' : 'false'])); },
            get type() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'type'])); },
            set type(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'type', v])); },
            get placeholder() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'placeholder'])); },
            set placeholder(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'placeholder', v])); },
            get href() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'href'])); },
            set href(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'href', v])); },
            get rel() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'rel'])); },
            set rel(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'rel', v])); },
            get target() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'target'])); },
            set target(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'target', v])); },
            get src() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'src'])); },
            set src(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'src', v])); },
            get lang() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'lang'])); },
            set lang(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'lang', v])); },
            get dir() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'dir'])); },
            set dir(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'dir', v])); },
            get title() { return __ipc('dom_getProperty', JSON.stringify([this.__id, 'title'])); },
            set title(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'title', v])); },
            get tabIndex() { return parseInt(__ipc('dom_getProperty', JSON.stringify([this.__id, 'tabIndex']))); },
            set tabIndex(v) { __ipc('dom_setProperty', JSON.stringify([this.__id, 'tabIndex', v])); },
            get classList() { return new DOMTokenListHost(this.__id); },
            get style() { return new DOMStyleHost(this.__id); },
            hasAttribute: function(name) { return __ipc('dom_hasAttribute', JSON.stringify([this.__id, name])) === 'true'; },
            getAttribute: function(name) { return __ipc('dom_getAttribute', JSON.stringify([this.__id, name])); },
            setAttribute: function(name, val) { __ipc('dom_setAttribute', JSON.stringify([this.__id, name, val])); },
            removeAttribute: function(name) { __ipc('dom_removeAttribute', JSON.stringify([this.__id, name])); },
            toggleAttribute: function(name) { __ipc('dom_toggleAttribute', JSON.stringify([this.__id, name])); },
            appendChild: function(child) { __ipc('dom_appendChild', JSON.stringify([this.__id, child.__id])); },
            insertBefore: function(newChild, refChild) { __ipc('dom_insertBefore', JSON.stringify([this.__id, newChild.__id, refChild ? refChild.__id : -1])); },
            removeChild: function(child) { __ipc('dom_removeChild', JSON.stringify([this.__id, child.__id])); },
            remove: function() { __ipc('dom_remove', JSON.stringify([this.__id])); },
            replaceWith: function(newEl) { __ipc('dom_replaceWith', JSON.stringify([this.__id, newEl.__id])); },
            before: function(newEl) { __ipc('dom_before', JSON.stringify([this.__id, newEl.__id])); },
            after: function(newEl) { __ipc('dom_after', JSON.stringify([this.__id, newEl.__id])); },
            cloneNode: function(deep) { var r = __ipc('dom_cloneNode', JSON.stringify([this.__id, deep ? 'true' : 'false'])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            insertAdjacentHTML: function(position, html) { __ipc('dom_insertAdjacentHTML', JSON.stringify([this.__id, position, html])); },
            querySelector: function(sel) { var r = __ipc('dom_querySelector', JSON.stringify([this.__id, sel])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            querySelectorAll: function(sel) { var ids = JSON.parse(__ipc('dom_querySelectorAll', JSON.stringify([this.__id, sel]))); return ids.map(function(i) { return g.__getEl(i); }); },
            getElementsByTagName: function(tag) { var ids = JSON.parse(__ipc('dom_getElementsByTagName', JSON.stringify([this.__id, tag]))); return ids.map(function(i) { return g.__getEl(i); }); },
            getElementsByClassName: function(cls) { var ids = JSON.parse(__ipc('dom_getElementsByClassName', JSON.stringify([this.__id, cls]))); return ids.map(function(i) { return g.__getEl(i); }); },
            contains: function(other) { return __ipc('dom_contains', JSON.stringify([this.__id, other ? other.__id : -1])) === 'true'; },
            matches: function(sel) { return __ipc('dom_matches', JSON.stringify([this.__id, sel])) === 'true'; },
            closest: function(sel) { var r = __ipc('dom_closest', JSON.stringify([this.__id, sel])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
            click: function() { __ipc('dom_click', JSON.stringify([this.__id])); },
            focus: function() { __ipc('dom_focus', JSON.stringify([this.__id])); },
            blur: function() { __ipc('dom_blur', JSON.stringify([this.__id])); },
            scrollIntoView: function(align) { __ipc('dom_scrollIntoView', JSON.stringify([this.__id, align ? 'true' : 'false'])); },
            getBoundingClientRect: function() { return JSON.parse(__ipc('dom_getBoundingClientRect', JSON.stringify([this.__id]))); },
            addEventListener: function(type, cb) { var id = __g_store(cb); __ipc('dom_addEventListener', JSON.stringify([this.__id, type, id])); },
            dispatchEvent: function(evt) { __ipc('dom_dispatchEvent', JSON.stringify([this.__id, evt.type || ''])); }
        };
    };
    g.__getEl = function(id) {
        if (!g.__elCache) g.__elCache = {};
        if (g.__elCache[id]) return g.__elCache[id];
        var el = g.__createEl(id);
        g.__elCache[id] = el;
        return el;
    };

    // DOMTokenListHost
    function DOMTokenListHost(elId) {
        var self = this;
        self.__id = elId;
        Object.defineProperty(self, 'length', { get: function() { var cls = self.className; return cls ? cls.split(' ').filter(function(c){return c;}).length : 0; } });
        self.add = function() { var c = self.className; var args = Array.prototype.slice.call(arguments); var parts = c ? c.split(' ') : []; for (var i = 0; i < args.length; i++) if (parts.indexOf(args[i]) === -1) parts.push(args[i]); __ipc('dom_setProperty', JSON.stringify([self.__id, 'className', parts.join(' ')])); };
        self.remove = function(cls) { var c = self.className; var parts = c.split(' ').filter(function(x){return x && x !== cls;}); __ipc('dom_setProperty', JSON.stringify([self.__id, 'className', parts.join(' ')])); };
        self.contains = function(cls) { var c = self.className; return c && c.split(' ').indexOf(cls) !== -1; };
        self.toggle = function(cls) { var c = self.className; if (c && c.split(' ').indexOf(cls) !== -1) { self.remove(cls); return false; } else { self.add(cls); return true; } };
        self.item = function(idx) { var c = self.className; if (!c) return null; var parts = c.split(' '); return idx >= 0 && idx < parts.length ? parts[idx] : null; };
        Object.defineProperty(self, 'className', { get: function() { return __ipc('dom_getProperty', JSON.stringify([self.__id, 'className'])); } });
    }

    // DOMStyleHost
    function DOMStyleHost(elId) {
        var self = this;
        self.__id = elId;
        self.getPropertyValue = function(name) { return __ipc('dom_getProperty', JSON.stringify([self.__id, name])); };
        self.setProperty = function(name, val) { __ipc('dom_setProperty', JSON.stringify([self.__id, name, val])); };
        Object.defineProperty(self, 'display', { get: function() { return self.getPropertyValue('display'); }, set: function(v) { self.setProperty('display', v); } });
        Object.defineProperty(self, 'width', { get: function() { return self.getPropertyValue('width'); }, set: function(v) { self.setProperty('width', v); } });
        Object.defineProperty(self, 'height', { get: function() { return self.getPropertyValue('height'); }, set: function(v) { self.setProperty('height', v); } });
        Object.defineProperty(self, 'backgroundColor', { get: function() { return self.getPropertyValue('backgroundColor'); }, set: function(v) { self.setProperty('backgroundColor', v); } });
        Object.defineProperty(self, 'color', { get: function() { return self.getPropertyValue('color'); }, set: function(v) { self.setProperty('color', v); } });
        Object.defineProperty(self, 'fontSize', { get: function() { return self.getPropertyValue('fontSize'); }, set: function(v) { self.setProperty('fontSize', v); } });
        Object.defineProperty(self, 'fontWeight', { get: function() { return self.getPropertyValue('fontWeight'); }, set: function(v) { self.setProperty('fontWeight', v); } });
        Object.defineProperty(self, 'marginTop', { get: function() { return self.getPropertyValue('marginTop'); }, set: function(v) { self.setProperty('marginTop', v); } });
        Object.defineProperty(self, 'marginRight', { get: function() { return self.getPropertyValue('marginRight'); }, set: function(v) { self.setProperty('marginRight', v); } });
        Object.defineProperty(self, 'marginLeft', { get: function() { return self.getPropertyValue('marginLeft'); }, set: function(v) { self.setProperty('marginLeft', v); } });
        Object.defineProperty(self, 'marginBottom', { get: function() { return self.getPropertyValue('marginBottom'); }, set: function(v) { self.setProperty('marginBottom', v); } });
        Object.defineProperty(self, 'paddingTop', { get: function() { return self.getPropertyValue('paddingTop'); }, set: function(v) { self.setProperty('paddingTop', v); } });
        Object.defineProperty(self, 'paddingRight', { get: function() { return self.getPropertyValue('paddingRight'); }, set: function(v) { self.setProperty('paddingRight', v); } });
        Object.defineProperty(self, 'paddingLeft', { get: function() { return self.getPropertyValue('paddingLeft'); }, set: function(v) { self.setProperty('paddingLeft', v); } });
        Object.defineProperty(self, 'paddingBottom', { get: function() { return self.getPropertyValue('paddingBottom'); }, set: function(v) { self.setProperty('paddingBottom', v); } });
        Object.defineProperty(self, 'opacity', { get: function() { return self.getPropertyValue('opacity'); }, set: function(v) { self.setProperty('opacity', v); } });
        Object.defineProperty(self, 'visibility', { get: function() { return self.getPropertyValue('visibility'); }, set: function(v) { self.setProperty('visibility', v); } });
        Object.defineProperty(self, 'overflow', { get: function() { return self.getPropertyValue('overflow'); }, set: function(v) { self.setProperty('overflow', v); } });
        Object.defineProperty(self, 'position', { get: function() { return self.getPropertyValue('position'); }, set: function(v) { self.setProperty('position', v); } });
        Object.defineProperty(self, 'textAlign', { get: function() { return self.getPropertyValue('textAlign'); }, set: function(v) { self.setProperty('textAlign', v); } });
        Object.defineProperty(self, 'borderTopWidth', { get: function() { return self.getPropertyValue('borderTopWidth'); }, set: function(v) { self.setProperty('borderTopWidth', v); } });
        Object.defineProperty(self, 'borderRightWidth', { get: function() { return self.getPropertyValue('borderRightWidth'); }, set: function(v) { self.setProperty('borderRightWidth', v); } });
        Object.defineProperty(self, 'borderLeftWidth', { get: function() { return self.getPropertyValue('borderLeftWidth'); }, set: function(v) { self.setProperty('borderLeftWidth', v); } });
        Object.defineProperty(self, 'borderBottomWidth', { get: function() { return self.getPropertyValue('borderBottomWidth'); }, set: function(v) { self.setProperty('borderBottomWidth', v); } });
        Object.defineProperty(self, 'boxSizing', { get: function() { return self.getPropertyValue('boxSizing'); }, set: function(v) { self.setProperty('boxSizing', v); } });
        Object.defineProperty(self, 'zIndex', { get: function() { return self.getPropertyValue('zIndex'); }, set: function(v) { self.setProperty('zIndex', v); } });
    }

    // DOMComputedStyleHost
    function DOMComputedStyleHost(elId) {
        var self = this;
        self.__id = elId;
        self.getPropertyValue = function(name) { return __ipc('dom_getComputedStyle', JSON.stringify([self.__id, name])); };
        Object.defineProperty(self, 'display', { get: function() { return self.getPropertyValue('display'); } });
        Object.defineProperty(self, 'width', { get: function() { return self.getPropertyValue('width'); } });
        Object.defineProperty(self, 'height', { get: function() { return self.getPropertyValue('height'); } });
        Object.defineProperty(self, 'backgroundColor', { get: function() { return self.getPropertyValue('backgroundColor'); } });
        Object.defineProperty(self, 'color', { get: function() { return self.getPropertyValue('color'); } });
        Object.defineProperty(self, 'fontSize', { get: function() { return self.getPropertyValue('fontSize'); } });
        Object.defineProperty(self, 'fontWeight', { get: function() { return self.getPropertyValue('fontWeight'); } });
    }

    // === document proxy (ID -1) ===
    g.document = {
        __id: -1, __type: 'HTMLDocument',
        readyState: 'complete', compatMode: 'CSS1Compat',
        characterSet: 'UTF-8', contentType: 'text/html',
        get title() { return __ipc('dom_getProperty', JSON.stringify([-1, 'title'])); },
        set title(v) { __ipc('dom_setTitle', JSON.stringify([v])); },
        get documentElement() { var r = __ipc('dom_getDocumentElement', '[]'); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        get body() { var r = __ipc('dom_getBody', '[]'); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        set body(el) { __ipc('dom_setProperty', JSON.stringify([-1, 'body', el ? el.__id : -1])); },
        get head() { var r = __ipc('dom_getHead', '[]'); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        get activeElement() { var r = __ipc('dom_getActiveElement', '[]'); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        get forms() { var ids = JSON.parse(__ipc('dom_getForms', '[]')); return ids.map(function(i) { return g.__getEl(i); }); },
        get images() { var ids = JSON.parse(__ipc('dom_getImages', '[]')); return ids.map(function(i) { return g.__getEl(i); }); },
        get links() { var ids = JSON.parse(__ipc('dom_getLinks', '[]')); return ids.map(function(i) { return g.__getEl(i); }); },
        get scripts() { var ids = JSON.parse(__ipc('dom_getScripts', '[]')); return ids.map(function(i) { return g.__getEl(i); }); },
        get anchors() { var ids = JSON.parse(__ipc('dom_getAnchors', '[]')); return ids.map(function(i) { return g.__getEl(i); }); },
        getElementById: function(id) { var r = __ipc('dom_getElementById', JSON.stringify([id])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        querySelector: function(sel) { var r = __ipc('dom_querySelector', JSON.stringify([-1, sel])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        querySelectorAll: function(sel) { var ids = JSON.parse(__ipc('dom_querySelectorAll', JSON.stringify([-1, sel]))); return ids.map(function(i) { return g.__getEl(i); }); },
        getElementsByTagName: function(tag) { var ids = JSON.parse(__ipc('dom_getElementsByTagName', JSON.stringify([-1, tag]))); return ids.map(function(i) { return g.__getEl(i); }); },
        getElementsByClassName: function(cls) { var ids = JSON.parse(__ipc('dom_getElementsByClassName', JSON.stringify([-1, cls]))); return ids.map(function(i) { return g.__getEl(i); }); },
        getElementsByName: function(name) { var ids = JSON.parse(__ipc('dom_getElementsByName', JSON.stringify([-1, name]))); return ids.map(function(i) { return g.__getEl(i); }); },
        createElement: function(tag) { var r = __ipc('dom_createElement', JSON.stringify([tag])); return g.__getEl(parseInt(r)); },
        createElementNS: function(ns, tag) { var r = __ipc('dom_createElement', JSON.stringify([tag])); return g.__getEl(parseInt(r)); },
        createTextNode: function(text) { return { nodeType: 3, textContent: text, nodeValue: text }; },
        createComment: function(text) { return { nodeType: 8, textContent: text, nodeValue: text }; },
        write: function(text) { __ipc('dom_write', JSON.stringify([text])); },
        writeln: function(text) { __ipc('dom_write', JSON.stringify([text + '\n'])); },
        addEventListener: function(type, cb) { var id = __g_store(cb); __ipc('dom_addEventListener', JSON.stringify([-1, type, id])); },
        removeEventListener: function(type) { __ipc('dom_removeEventListener', JSON.stringify([-1, type])); },
        dispatchEvent: function(evt) { __ipc('dom_dispatchEvent', JSON.stringify([-1, evt.type || ''])); },
        getComputedStyle: function(el) { return new DOMComputedStyleHost(el.__id); },
        getElementByClassName: function(cls) { var r = __ipc('dom_getElementsByClassName', JSON.stringify([-1, cls])); return r === 'null' ? null : g.__getEl(parseInt(r)); },
        getSelection: function() { return null; },
        elementFromPoint: function() { return null; },
        elementsFromPoint: function() { return []; },
        hasFocus: function() { return true; },
        normalizeDocument: function() {}, open: function() {}, close: function() {},
        adoptNode: function(n) { return n; },
        importNode: function(n, d) { return n; },
        fullscreenEnabled: function() { return false; },
        exitFullscreen: function() { return false; },
        currentScript: null, styleSheets: null, fonts: null,
        doctype: null, implementation: {},
        get URL() { return __ipc('dom_getUrl', '[]'); },
        get documentURI() { return __ipc('dom_getUrl', '[]'); },
        get baseURI() { return __ipc('dom_getUrl', '[]'); },
        get domain() { return ''; }, referrer: '', lastModified: '',
        inputEncoding: 'UTF-8', charset: 'UTF-8', defaultCharset: 'UTF-8',
        dir: 'ltr', visibilityState: 'visible', hidden: false
    };

    // === location ===
    g.location = {
        __id: -1,
        get href() { return __ipc('dom_getProperty', JSON.stringify([-1, 'href'])); },
        set href(v) { __ipc('dom_setWindowLocation', JSON.stringify([v])); },
        get protocol() { return 'http:'; }, get hostname() { return ''; },
        get port() { return ''; }, get pathname() { return '/'; },
        get search() { return ''; }, get hash() { return ''; }, get origin() { return ''; },
        assign: function(v) { __ipc('dom_setWindowLocation', JSON.stringify([v])); },
        reload: function() {},
        replace: function(v) { __ipc('dom_setWindowLocation', JSON.stringify([v])); }
    };

    // === navigator ===
    g.navigator = {
        appName: 'UpBrowser', appVersion: '1.0',
        userAgent: 'UpBrowser/1.0', platform: 'Win32',
        language: 'en-US', languages: ['en-US', 'en'],
        online: true, cookieEnabled: true,
        hardwareConcurrency: 4, deviceMemory: 4, maxTouchPoints: 0,
        connection: { effectiveType: '4g', rtt: 50, downlink: 10, saveData: false },
        plugins: []
    };

    // === localStorage ===
    g.localStorage = {
        getItem: function(k) { return __ipc('dom_localStorage_get', JSON.stringify([k])); },
        setItem: function(k, v) { __ipc('dom_localStorage_set', JSON.stringify([k, v])); },
        removeItem: function(k) { __ipc('dom_localStorage_remove', JSON.stringify([k])); },
        clear: function() { __ipc('dom_localStorage_clear', '[]'); },
        get length() { return 0; }, key: function(i) { return null; }
    };

    // === sessionStorage ===
    g.sessionStorage = {
        getItem: function(k) { return __ipc('dom_sessionStorage_get', JSON.stringify([k])); },
        setItem: function(k, v) { __ipc('dom_sessionStorage_set', JSON.stringify([k, v])); },
        removeItem: function(k) { __ipc('dom_sessionStorage_remove', JSON.stringify([k])); },
        clear: function() { __ipc('dom_sessionStorage_clear', '[]'); },
        get length() { return 0; }, key: function(i) { return null; }
    };

    // === history ===
    g.history = {
        get length() { return parseInt(__ipc('dom_history_getLength', '[]')); },
        pushState: function(state, title, url) { __ipc('dom_history_pushState', JSON.stringify([JSON.stringify(state || {}), title || '', url || ''])); },
        replaceState: function(state, title, url) { __ipc('dom_history_replaceState', JSON.stringify([JSON.stringify(state || {}), title || '', url || ''])); },
        back: function() { __ipc('dom_history_back', '[]'); },
        forward: function() { __ipc('dom_history_forward', '[]'); },
        go: function(steps) { __ipc('dom_history_go', JSON.stringify([steps || 0])); },
        getState: function() { var r = __ipc('dom_history_getState', '[]'); try { return JSON.parse(r); } catch(e) { return r; } }
    };

    // === screen ===
    g.screen = {
        width: 1920, height: 1080,
        availWidth: 1920, availHeight: 1040,
        colorDepth: 24, pixelDepth: 24,
        orientation: { type: 'landscape-primary', angle: 0, change: null }
    };
    g.screenX = 0; g.screenY = 0;
    g.outerWidth = function() { return parseInt(__ipc('dom_getWindowInnerWidth', '[]')); };
    g.outerHeight = function() { return parseInt(__ipc('dom_getWindowInnerHeight', '[]')); };

    // === console ===
    g.console = {
        log: function() { print('[JS Log] ' + Array.prototype.slice.call(arguments).join(' ')); },
        error: function() { print('[JS Error] ' + Array.prototype.slice.call(arguments).join(' ')); },
        warn: function() { print('[JS Warn] ' + Array.prototype.slice.call(arguments).join(' ')); },
        info: function() { print('[JS Info] ' + Array.prototype.slice.call(arguments).join(' ')); },
        debug: function() { print('[JS Debug] ' + Array.prototype.slice.call(arguments).join(' ')); }
    };

    // === timers ===
    g.setTimeout = function(fn, ms) { return __ipc('setTimeout', JSON.stringify([__g_store(fn), ms||0])); };
    g.setInterval = function(fn, ms) { return __ipc('setInterval', JSON.stringify([__g_store(fn), ms||0])); };
    g.clearTimeout = function(id) { try { __ipc('clearTimeout', JSON.stringify([id])); } catch(e) {} };
    g.clearInterval = function(id) { try { __ipc('clearInterval', JSON.stringify([id])); } catch(e) {} };

    // === __upbrowser ===
    g.__upbrowser = {
        setTimeout: g.setTimeout, setInterval: g.setInterval,
        clearTimeout: g.clearTimeout, clearInterval: g.clearInterval,
        innerWidth: function() { return parseInt(__ipc('innerWidth', '[]')); },
        innerHeight: function() { return parseInt(__ipc('innerHeight', '[]')); },
        scrollTo: function(x,y) { __ipc('scrollTo', JSON.stringify([x||0,y||0])); },
        scrollBy: function(x,y) { __ipc('scrollBy', JSON.stringify([x||0,y||0])); },
        alert: function(msg) { __ipc('alert', JSON.stringify([msg||''])); },
        confirm: function(msg) { return __ipc('confirm', JSON.stringify([msg||''])) === 'true'; },
        prompt: function(msg,def) { return __ipc('prompt', JSON.stringify([msg||'',def||''])); },
        decodeURI: function(s) { return decodeURI(s); },
        encodeURI: function(s) { return encodeURI(s); },
        decodeURIComponent: function(s) { return decodeURIComponent(s); },
        encodeURIComponent: function(s) { return encodeURIComponent(s); },
        parseInt: function(s,r) { return parseInt(s, r||10); },
        parseFloat: function(s) { return parseFloat(s); },
        isNaN: function(v) { return isNaN(v); },
        isFinite: function(v) { return isFinite(v); },
        escape: function(s) { return escape(s); },
        unescape: function(s) { return unescape(s); },
        atob: function(s) { return __ipc('atob', JSON.stringify([s||''])); },
        btoa: function(s) { return __ipc('btoa', JSON.stringify([s||''])); },
        _fetch: function(url, opts, resolveId, rejectId) {
            __ipc('fetch', JSON.stringify([url, opts||'', resolveId, rejectId]));
        },
        createXMLHttpRequest: function() { return __ipc('createXHR', '[]'); },
        createURL: function(url,base) { return __ipc('createURL', JSON.stringify([url||'',base||''])); },
        createURLSearchParams: function(q) { return __ipc('createURLSearchParams', JSON.stringify([q||''])); },
        innerWidth: function() { return parseInt(__ipc('innerWidth', '[]')); },
        innerHeight: function() { return parseInt(__ipc('innerHeight', '[]')); },
        devicePixelRatio: function() { return 1; },
        scrollX: function() { return 0; },
        scrollY: function() { return 0; }
    };

    // === window ===
    g.window = g;
    g.alert = function(m) { __ipc('alert', JSON.stringify([m||''])); };
    g.confirm = function(m) { return __ipc('confirm', JSON.stringify([m||''])) === 'true'; };
    g.prompt = function(m,d) { return __ipc('prompt', JSON.stringify([m||'',d||''])); };
    g.fetch = function(url, opts) {
        return new Promise(function(resolve, reject) {
            var rid = __g_store(resolve);
            var rjid = __g_store(reject);
            __ipc('fetch', JSON.stringify([url, JSON.stringify(opts||{}), rid, rjid]));
        });
    };
    g.requestAnimationFrame = function(fn) { return g.setTimeout(fn, 16); };
    g.cancelAnimationFrame = function(id) { g.clearTimeout(id); };
    g.decodeURI = function(s) { try { return decodeURI(s); } catch(e) { return s; } };
    g.encodeURI = function(s) { try { return encodeURI(s); } catch(e) { return s; } };
    g.decodeURIComponent = function(s) { try { return decodeURIComponent(s); } catch(e) { return s; } };
    g.encodeURIComponent = function(s) { try { return encodeURIComponent(s); } catch(e) { return s; } };
    g.parseInt = function(s,r) { return parseInt(s, r||10); };
    g.parseFloat = function(s) { return parseFloat(s); };
    g.isNaN = function(v) { return isNaN(v); };
    g.isFinite = function(v) { return isFinite(v); };
    g.atob = function(s) { return __ipc('atob', JSON.stringify([s||''])); };
    g.btoa = function(s) { return __ipc('btoa', JSON.stringify([s||''])); };
    try { g.Object.defineProperty(g, 'innerWidth', { configurable: true, get: function() { return parseInt(__ipc('innerWidth', '[]')); } }); } catch(e) {}
    try { g.Object.defineProperty(g, 'innerHeight', { configurable: true, get: function() { return parseInt(__ipc('innerHeight', '[]')); } }); } catch(e) {}

    g.__win = {
        alert: g.alert, confirm: g.confirm, prompt: g.prompt,
        innerWidth: function() { return parseInt(__ipc('innerWidth', '[]')); },
        innerHeight: function() { return parseInt(__ipc('innerHeight', '[]')); },
        scrollTo: function(x,y) { __ipc('scrollTo', JSON.stringify([x||0,y||0])); },
        scrollBy: function(x,y) { __ipc('scrollBy', JSON.stringify([x||0,y||0])); }
    };
})();
