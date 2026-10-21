(() => {
  'use strict';

  const PROTOCOL_VERSION = 2;
  const RENDERER_VERSION = '1.0.1';
  const PAGE_WIDTH = 816;
  const PAGE_HEIGHT = 1056;
  const MAX_DOCUMENT_BYTES = 8 * 1024 * 1024;
  const MAX_PAGES = 600;
  const MAX_RESOURCE_BASE64 = 48 * 1024 * 1024;
  const PAGEBREAK_REGEX = /^(?=\\page(?:break)?(?: *\{[^\n{}]*\})?$)/m;
  const BLOCKED_ELEMENTS = [
    'script', 'noscript', 'iframe', 'frame', 'frameset', 'object', 'embed', 'applet',
    'base', 'meta', 'link', 'form', 'input', 'button', 'textarea', 'select',
    'option', 'template', 'foreignObject'
  ].join(',');
  const URL_ATTRIBUTES = new Set(['href', 'src', 'poster', 'action', 'formaction', 'xlink:href']);
  const DROP_ATTRIBUTES = new Set([
    'autofocus', 'autoplay', 'contenteditable', 'crossorigin', 'formaction', 'integrity',
    'nonce', 'ping', 'referrerpolicy', 'srcdoc', 'srcset'
  ]);
  const SAFE_RESOURCE_MIME = /^(?:image\/(?:png|gif|jpe?g|webp|avif)|font\/(?:woff2?|ttf|otf)|application\/(?:font-woff|font-sfnt|octet-stream)|text\/css)(?:;|$)/iu;

  const shell = document.getElementById('reader-app');
  const documentRoot = document.getElementById('document-root');
  const status = document.getElementById('reader-status');
  const titleElement = document.getElementById('reader-title');
  const themeLink = document.getElementById('document-theme');

  const state = {
    mode: 'book', zoom: 1, fontScale: 1, documentId: null, pageCount: 0, currentPage: 1,
    readySent: false, pendingHostMessages: [], renderGeneration: 0, pageObserver: null,
    blobResources: new Map(), resourceStyles: new Map(), resourceIssues: new Set(), metadata: {}
  };

  function pick(object, names, fallback = undefined) {
    if (!object || typeof object !== 'object') return fallback;
    for (const name of names) if (Object.prototype.hasOwnProperty.call(object, name)) return object[name];
    return fallback;
  }

  function clampNumber(value, minimum, maximum, fallback) {
    const number = Number(value);
    return Number.isFinite(number) ? Math.min(maximum, Math.max(minimum, number)) : fallback;
  }

  function normalizeMode(value) {
    const mode = String(value ?? '').trim().toLowerCase();
    return ['mobile', 'reflow', 'responsive', 'móvil', 'movil'].includes(mode) ? 'mobile' : 'book';
  }

  function setStatus(message, isError = false) {
    status.textContent = message;
    status.classList.toggle('isError', isError);
    status.hidden = false;
  }

  function hideStatus() {
    status.hidden = true;
    status.classList.remove('isError');
  }

  function getBridge() {
    const bridge = globalThis.HybridWebView;
    return bridge && typeof bridge.SendRawMessage === 'function' ? bridge : null;
  }

  function flushHostMessages() {
    const bridge = getBridge();
    if (!bridge) return false;
    while (state.pendingHostMessages.length > 0) bridge.SendRawMessage(state.pendingHostMessages.shift());
    return true;
  }

  function sendToHost(payload) {
    const serialized = JSON.stringify(payload);
    const bridge = getBridge();
    if (bridge) {
      flushHostMessages();
      bridge.SendRawMessage(serialized);
      return true;
    }
    if (state.pendingHostMessages.length < 64) state.pendingHostMessages.push(serialized);
    return false;
  }

  function sendError(code, error, details = undefined) {
    sendToHost({
      type: 'error', code, message: error instanceof Error ? error.message : String(error),
      documentId: state.documentId, details
    });
  }

  function decodeHtmlAmpersands(value) { return value.replaceAll('&amp;', '&'); }

  function normalizeResourceMap(input) {
    const result = new Map(state.blobResources);
    if (Array.isArray(input)) {
      for (const entry of input) {
        const source = pick(entry, ['source', 'Source', 'remote', 'Remote', 'url', 'Url']);
        const local = pick(entry, ['local', 'Local', 'path', 'Path', 'value', 'Value']);
        if (typeof source === 'string' && typeof local === 'string') result.set(source, local);
      }
    } else if (input && typeof input === 'object') {
      for (const [source, local] of Object.entries(input)) if (typeof local === 'string') result.set(source, local);
    }
    return result;
  }

  function mappedResource(value, resourceMap) {
    return resourceMap.get(value) ?? resourceMap.get(decodeHtmlAmpersands(value)) ?? value;
  }

  function recordMissingResource(value) {
    if (!/^https?:\/\//iu.test(value) || state.resourceIssues.has(value)) return;
    state.resourceIssues.add(value);
    sendToHost({ type: 'resourceMissing', source: value, documentId: state.documentId });
  }

  function isLocalOrSafeDataUrl(rawValue, attribute, elementName, resourceMap) {
    const original = String(rawValue ?? '').trim();
    const mapped = mappedResource(original, resourceMap);
    if (!mapped || /[\u0000-\u001f\u007f]/u.test(mapped)) return null;
    if (mapped.startsWith('#')) return mapped;
    if (/^data:/iu.test(mapped)) {
      const safeImageData = /^data:image\/(?:png|gif|jpe?g|webp|avif);base64,/iu.test(mapped);
      return ['img', 'source'].includes(elementName) && safeImageData ? mapped : null;
    }
    if (/^blob:/iu.test(mapped)) return ['src', 'poster'].includes(attribute) ? mapped : null;
    if (/^(?:javascript|vbscript|file|filesystem):/iu.test(mapped) || mapped.startsWith('//')) return null;
    try {
      const resolved = new URL(mapped, document.baseURI);
      if (resolved.origin === globalThis.location.origin && ['http:', 'https:'].includes(resolved.protocol)) return mapped;
    } catch { return null; }
    recordMissingResource(original);
    return null;
  }

  function escapeCssUrl(value) {
    return value.replaceAll('\\', '\\\\').replaceAll('"', '\\"').replace(/[\r\n\f]/gu, '');
  }

  function sanitizeCss(cssText, resourceMap) {
    let css = String(cssText ?? '').replaceAll('\u0000', '');
    if (css.length > 512 * 1024) css = css.slice(0, 512 * 1024);
    css = css.replace(/@charset\s+[^;]+;?/giu, '');
    css = css.replace(/@import\s+(?:url\(\s*)?(["']?)(https?:\/\/[^'"\s;)]+)\1\s*\)?[^;]*;?/giu, (_match, _quote, source) => {
      const imported = state.resourceStyles.get(source) ?? state.resourceStyles.get(decodeHtmlAmpersands(source));
      if (!imported) {
        recordMissingResource(source);
        return '';
      }
      // Imported styles are inlined because WebKit cannot intercept HTTPS
      // requests through HybridWebView. Nested imports remain blocked.
      return imported.replace(/@(?:import|charset)\s+[^;]+;?/giu, '');
    });
    css = css.replace(/@import\s+[^;]+;?/giu, '');
    css = css.replace(/(?:position\s*:\s*fixed|behavior\s*:|-moz-binding\s*:)[^;}]*;?/giu, '');
    if (/(?:expression\s*\(|javascript\s*:|vbscript\s*:)/iu.test(css)) return '';
    return css.replace(/url\(\s*(["']?)(.*?)\1\s*\)/giu, (_match, _quote, rawUrl) => {
      const original = rawUrl.trim();
      const mapped = mappedResource(original, resourceMap);
      if (mapped.startsWith('#')) return `url("${escapeCssUrl(mapped)}")`;
      if (/^data:(?:image\/(?:png|gif|jpe?g|webp|avif)|font\/(?:woff2?|ttf|otf));base64,/iu.test(mapped)) return `url("${escapeCssUrl(mapped)}")`;
      if (/^blob:/iu.test(mapped)) return `url("${escapeCssUrl(mapped)}")`;
      if (/^(?:javascript|vbscript|file|filesystem):/iu.test(mapped) || mapped.startsWith('//')) return 'url("")';
      try {
        const resolved = new URL(mapped, document.baseURI);
        if (resolved.origin === globalThis.location.origin && ['http:', 'https:'].includes(resolved.protocol)) return `url("${escapeCssUrl(mapped)}")`;
      } catch { return 'url("")'; }
      recordMissingResource(original);
      return 'url("")';
    });
  }

  function sanitizeHtml(markup, resourceMap) {
    const template = document.createElement('template');
    template.innerHTML = String(markup ?? '');
    template.content.querySelectorAll('link[rel~="stylesheet"][href]').forEach((link) => {
      const source = link.getAttribute('href') || '';
      const imported = state.resourceStyles.get(source) ?? state.resourceStyles.get(decodeHtmlAmpersands(source));
      if (!imported) {
        recordMissingResource(source);
        link.remove();
        return;
      }
      const style = document.createElement('style');
      style.textContent = sanitizeCss(imported, resourceMap);
      link.replaceWith(style);
    });
    template.content.querySelectorAll(BLOCKED_ELEMENTS).forEach((element) => element.remove());
    for (const element of template.content.querySelectorAll('*')) {
      const elementName = element.localName.toLowerCase();
      if (elementName === 'style') element.textContent = sanitizeCss(element.textContent, resourceMap);
      for (const attribute of [...element.attributes]) {
        const name = attribute.name.toLowerCase();
        const value = attribute.value;
        if (name.startsWith('on') || DROP_ATTRIBUTES.has(name)) {
          element.removeAttribute(attribute.name);
          continue;
        }
        if (name === 'style') {
          const safeCss = sanitizeCss(value, resourceMap);
          safeCss ? element.setAttribute(attribute.name, safeCss) : element.removeAttribute(attribute.name);
          continue;
        }
        if (URL_ATTRIBUTES.has(name)) {
          const safeUrl = isLocalOrSafeDataUrl(value, name, elementName, resourceMap);
          if (safeUrl) element.setAttribute(attribute.name, safeUrl);
          else if (elementName === 'a' && name === 'href') {
            element.setAttribute('href', '#');
            element.dataset.externalHref = value;
          } else {
            element.removeAttribute(attribute.name);
            if (['img', 'video', 'source'].includes(elementName)) element.classList.add('resourceMissing');
          }
        }
      }
    }
    return template.content;
  }

  function parseHomebreweryExport(rawSource) {
    let source = String(rawSource ?? '').replace(/^\uFEFF/u, '').replaceAll('\r\n', '\n').replaceAll('\r', '\n');
    const diagnostics = [];
    let metadata = {};
    let customCss = '';
    const metadataMatch = source.match(/^```metadata[^\n]*\n([\s\S]*?)\n```[ \t]*(?:\n{1,2}|$)/u);
    if (metadataMatch) {
      try {
        const parsed = globalThis.jsyaml?.load(metadataMatch[1], { schema: globalThis.jsyaml.JSON_SCHEMA });
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) metadata = parsed;
      } catch (error) {
        diagnostics.push(`Metadata inválida: ${error instanceof Error ? error.message : String(error)}`);
      }
      source = source.slice(metadataMatch[0].length);
    } else if (source.startsWith('```metadata')) diagnostics.push('El bloque metadata no tiene un cierre válido y se conservó como contenido.');

    const cssMatch = source.match(/^```css[^\n]*\n([\s\S]*?)\n```[ \t]*(?:\n{1,2}|$)/u);
    if (cssMatch) {
      customCss = cssMatch[1];
      source = source.slice(cssMatch[0].length);
    } else if (source.startsWith('```css')) diagnostics.push('El bloque CSS no tiene un cierre válido y se conservó como contenido.');
    return { source, metadata, customCss, diagnostics };
  }

  function normalizeTheme(value) {
    return String(value ?? '5ePHB').trim().toLowerCase() === 'blank' ? 'Blank' : '5ePHB';
  }

  function applyTheme(theme) {
    const normalized = normalizeTheme(theme);
    themeLink.disabled = normalized === 'Blank';
    shell.dataset.theme = normalized;
    return normalized;
  }

  function getRenderer() {
    const renderer = globalThis.hbfm?.hbfm;
    if (!renderer || typeof renderer.render !== 'function' || typeof renderer.validate !== 'function') throw new Error('El motor marked-hbfm@1.0.1 no está disponible.');
    return renderer;
  }

  function extractPageInjection(renderer, pageSource) {
    if (!/^\\page(?:break)?(?: *\{[^\n{}]*\})?$/u.test(pageSource.split('\n', 1)[0])) return { source: pageSource, classes: [], styles: {}, attributes: {} };
    const firstLine = pageSource.split('\n', 1)[0];
    let injectedTags;
    try {
      const firstToken = renderer.marked?.lexer(firstLine)?.[0];
      injectedTags = firstToken?.tokens?.find((token) => token.injectedTags !== undefined)?.injectedTags;
    } catch { injectedTags = undefined; }
    const source = pageSource.includes('\n') ? pageSource.slice(pageSource.indexOf('\n') + 1) : '';
    return {
      source,
      classes: Array.isArray(injectedTags?.classes) ? injectedTags.classes : String(injectedTags?.classes ?? '').split(/\s+/u),
      styles: injectedTags?.styles && typeof injectedTags.styles === 'object' ? injectedTags.styles : {},
      attributes: injectedTags?.attributes && typeof injectedTags.attributes === 'object' ? injectedTags.attributes : {}
    };
  }

  function applyPageInjection(page, injection, resourceMap) {
    for (const className of injection.classes) if (/^[a-z_][\w-]*$/iu.test(className)) page.classList.add(className);
    const styleText = Object.entries(injection.styles)
      .filter(([name]) => /^--[\w-]+$/u.test(name) || /^[a-z-]+$/u.test(name))
      .map(([name, value]) => `${name}:${String(value)}`).join(';');
    const safeStyle = sanitizeCss(styleText, resourceMap);
    if (safeStyle) page.setAttribute('style', safeStyle);
    for (const [name, value] of Object.entries(injection.attributes)) {
      if (/^(?:data-[\w-]+|aria-[\w-]+|lang|dir|title)$/iu.test(name)) page.setAttribute(name, String(value));
    }
  }

  function setMode(mode, zoom = state.zoom, fontScale = state.fontScale, notify = true) {
    state.mode = normalizeMode(mode);
    state.zoom = clampNumber(zoom, 0.35, 2.5, state.zoom);
    state.fontScale = clampNumber(fontScale, 0.75, 1.8, state.fontScale);
    shell.classList.toggle('mode-book', state.mode === 'book');
    shell.classList.toggle('mode-mobile', state.mode === 'mobile');
    shell.dataset.mode = state.mode;
    shell.style.setProperty('--reader-zoom', String(state.zoom));
    shell.style.setProperty('--reader-font-scale', String(state.fontScale));
    shell.style.setProperty('--scaled-page-width', `${PAGE_WIDTH * state.zoom}px`);
    shell.style.setProperty('--scaled-page-height', `${PAGE_HEIGHT * state.zoom}px`);
    if (notify) sendToHost({ type: 'modeChanged', mode: state.mode, zoom: state.zoom, fontScale: state.fontScale, documentId: state.documentId });
    return { mode: state.mode, zoom: state.zoom, fontScale: state.fontScale };
  }

  function setZoom(zoom) { return setMode(state.mode, zoom, state.fontScale); }
  function setFontScale(fontScale) { return setMode(state.mode, state.zoom, fontScale); }

  function reportCurrentPage(page) {
    const current = Math.max(1, Math.min(state.pageCount || 1, Number(page) || 1));
    if (current === state.currentPage) return;
    state.currentPage = current;
    sendToHost({ type: 'pageChanged', page: current, pageCount: state.pageCount, documentId: state.documentId });
  }

  function observePages(pages) {
    state.pageObserver?.disconnect();
    const ratios = new Map();
    state.pageObserver = new IntersectionObserver((entries) => {
      for (const entry of entries) ratios.set(Number(entry.target.dataset.pageNumber), entry.intersectionRatio);
      let bestPage = state.currentPage;
      let bestRatio = -1;
      for (const [page, ratio] of ratios) if (ratio > bestRatio) { bestRatio = ratio; bestPage = page; }
      if (bestRatio > 0) reportCurrentPage(bestPage);
    }, { root: null, threshold: [0, 0.15, 0.35, 0.55, 0.75, 1] });
    pages.forEach((page) => state.pageObserver.observe(page));
  }

  function nextFrame() { return new Promise((resolve) => globalThis.requestAnimationFrame(() => resolve())); }

  async function renderDocument(input) {
    const generation = ++state.renderGeneration;
    const payload = typeof input === 'string' ? { content: input } : (input ?? {});
    const rawContent = pick(payload, ['content', 'Content', 'text', 'Text', 'markdown', 'Markdown', 'rawText', 'RawText'], '');
    if (typeof rawContent !== 'string') throw new TypeError('El documento debe contener texto HBFM en content.');
    if (new TextEncoder().encode(rawContent).byteLength > MAX_DOCUMENT_BYTES) throw new Error('El documento supera el límite de 8 MiB.');

    const parsed = parseHomebreweryExport(rawContent);
    const payloadTitle = String(pick(payload, ['title', 'Title', 'name', 'Name'], 'Documento sin título'));
    const documentTitle = String(parsed.metadata.title || payloadTitle);
    const documentId = pick(payload, ['id', 'Id', 'documentId', 'DocumentId'], null);
    const resourceMap = normalizeResourceMap(pick(payload, ['resourceMap', 'ResourceMap', 'resources', 'Resources'], {}));
    const payloadCss = pick(payload, ['css', 'Css', 'customCss', 'CustomCss'], '');
    const customCss = [parsed.customCss, typeof payloadCss === 'string' ? payloadCss : ''].filter(Boolean).join('\n');
    state.documentId = documentId == null ? null : String(documentId);
    state.resourceIssues.clear();
    state.metadata = parsed.metadata;
    setStatus(`Abriendo ${documentTitle}…`);
    shell.dataset.readerState = 'rendering';
    setMode(pick(payload, ['mode', 'Mode'], state.mode), pick(payload, ['zoom', 'Zoom'], state.zoom), pick(payload, ['fontScale', 'FontScale'], state.fontScale), false);
    const appliedTheme = applyTheme(parsed.metadata.theme);

    const renderer = getRenderer();
    const pageSources = parsed.source.split(PAGEBREAK_REGEX);
    if (pageSources.length > MAX_PAGES) throw new Error(`El documento contiene ${pageSources.length} páginas; el máximo es ${MAX_PAGES}.`);
    const validationErrors = renderer.validate(parsed.source);
    const pageErrors = [];
    const pagesContainer = document.createElement('section');
    pagesContainer.className = 'documentPages';
    pagesContainer.setAttribute('aria-label', documentTitle);
    pagesContainer.lang = String(parsed.metadata.lang || 'es');
    document.getElementById('document-custom-style')?.remove();
    if (customCss.trim()) {
      const style = document.createElement('style');
      style.id = 'document-custom-style';
      style.dataset.documentStyle = 'true';
      style.textContent = sanitizeCss(customCss, resourceMap);
      document.head.append(style);
    }

    const pageModels = pageSources.map((pageSource, index) => {
      const injection = extractPageInjection(renderer, pageSource);
      const page = document.createElement('article');
      const columns = document.createElement('div');
      page.className = 'page';
      page.id = `p${index + 1}`;
      page.dataset.pageNumber = String(index + 1);
      page.dataset.index = String(index);
      page.setAttribute('aria-label', `Página ${index + 1} de ${pageSources.length}`);
      columns.className = 'columnWrapper';
      page.append(columns);
      applyPageInjection(page, injection, resourceMap);
      pagesContainer.append(page);
      return { page, columns, source: `${injection.source}\n\n&nbsp;\n\\column\n&nbsp;` };
    });

    documentRoot.replaceChildren(pagesContainer);
    state.pageCount = pageSources.length;
    state.currentPage = 1;
    titleElement.textContent = documentTitle;
    document.title = `${documentTitle} — Lector offline`;
    observePages(pageModels.map((model) => model.page));

    for (let index = 0; index < pageModels.length; index += 1) {
      if (generation !== state.renderGeneration) return null;
      const model = pageModels[index];
      try {
        model.columns.append(sanitizeHtml(renderer.render(model.source, index), resourceMap));
      } catch (error) {
        const renderError = document.createElement('div');
        renderError.className = 'renderError';
        renderError.setAttribute('role', 'alert');
        renderError.textContent = `No se pudo interpretar la página ${index + 1}.`;
        model.columns.append(renderError);
        pageErrors.push({ page: index + 1, message: error instanceof Error ? error.message : String(error) });
      }
      if (index % 2 === 1) {
        setStatus(`Preparando página ${index + 1} de ${pageModels.length}…`);
        await nextFrame();
      }
    }

    if (generation !== state.renderGeneration) return null;
    shell.dataset.readerState = pageErrors.length === pageSources.length ? 'error' : 'ready';
    documentRoot.scrollIntoView({ block: 'start' });
    hideStatus();
    const result = {
      type: 'rendered', documentId: state.documentId, pageCount: state.pageCount, currentPage: 1,
      validationErrors, pageErrors, diagnostics: parsed.diagnostics, resourceIssues: [...state.resourceIssues],
      metadata: { title: documentTitle, renderer: parsed.metadata.renderer || 'V3', theme: appliedTheme, lang: parsed.metadata.lang || 'es' },
      mode: state.mode, zoom: state.zoom, fontScale: state.fontScale
    };
    sendToHost(result);
    sendToHost({ type: 'pageChanged', page: 1, pageCount: state.pageCount, documentId: state.documentId });
    return result;
  }

  function revokeBlobResources() {
    for (const url of state.blobResources.values()) URL.revokeObjectURL(url);
    state.blobResources.clear();
    state.resourceStyles.clear();
  }

  function registerResource(input) {
    const source = String(pick(input, ['source', 'Source', 'url', 'Url'], '')).trim();
    const mimeType = String(pick(input, ['mimeType', 'MimeType', 'contentType', 'ContentType'], '')).trim();
    const base64 = String(pick(input, ['base64', 'Base64', 'content', 'Content'], ''));
    if (!/^https?:\/\//iu.test(source)) throw new Error('El recurso no tiene una URL HTTP/HTTPS válida.');
    if (!SAFE_RESOURCE_MIME.test(mimeType)) throw new Error(`Tipo de recurso no permitido: ${mimeType || '(vacío)'}`);
    if (!base64 || base64.length > MAX_RESOURCE_BASE64) throw new Error('El recurso local está vacío o supera el límite permitido.');
    const binary = globalThis.atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    if (/^text\/css(?:;|$)/iu.test(mimeType)) {
      state.resourceStyles.set(source, new TextDecoder('utf-8', { fatal: true }).decode(bytes));
    }
    const previous = state.blobResources.get(source);
    if (previous) URL.revokeObjectURL(previous);
    const objectUrl = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    state.blobResources.set(source, objectUrl);
    return { source, bytes: bytes.byteLength };
  }

  function clear() {
    ++state.renderGeneration;
    state.pageObserver?.disconnect();
    state.pageObserver = null;
    documentRoot.replaceChildren();
    document.getElementById('document-custom-style')?.remove();
    revokeBlobResources();
    state.documentId = null;
    state.pageCount = 0;
    state.currentPage = 1;
    state.metadata = {};
    shell.dataset.readerState = 'empty';
    titleElement.textContent = 'Lector offline de D&D Compendium';
    document.title = 'Lector offline';
    setStatus('Seleccioná un documento descargado para leerlo sin conexión.');
    return true;
  }

  function scrollToTarget(target) {
    let element = null;
    const candidates = typeof target === 'string' ? target.split('|').map(value => value.trim()).filter(Boolean) : [target];
    const primary = candidates[0];
    const numeric = Number(primary);
    if (Number.isInteger(numeric) && numeric > 0) element = document.getElementById(`p${numeric}`);
    else if (typeof primary === 'string' && primary.trim()) {
      let id;
      try { id = decodeURIComponent(primary.trim().replace(/^#/, '')); }
      catch { id = primary.trim().replace(/^#/, ''); }
      const candidate = document.getElementById(id);
      element = candidate && documentRoot.contains(candidate) ? candidate : null;
    }
    if (!element && candidates.length > 1) {
      const fallback = candidates[1];
      const fallbackNumber = Number(String(fallback).replace(/^p/i, ''));
      if (Number.isInteger(fallbackNumber) && fallbackNumber > 0) element = document.getElementById(`p${fallbackNumber}`);
    }
    if (!element) return false;
    element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    const page = element.classList.contains('page') ? element : element.closest('.page');
    if (page) reportCurrentPage(Number(page.dataset.pageNumber));
    return true;
  }

  function unwrapMessage(message) { return pick(message, ['payload', 'Payload', 'document', 'Document'], message); }

  async function handleHostMessage(rawMessage) {
    try {
      const message = typeof rawMessage === 'string' ? JSON.parse(rawMessage) : rawMessage;
      if (!message || typeof message !== 'object') throw new TypeError('El mensaje del host no es un objeto JSON.');
      const type = String(pick(message, ['type', 'Type', 'command', 'Command'], '')).trim().toLowerCase();
      const payload = unwrapMessage(message);
      switch (type) {
        case 'document': case 'render': case 'setdocument': return await renderDocument(payload);
        case 'resource': case 'registerresource': return registerResource(payload);
        case 'clearresources': revokeBlobResources(); return true;
        case 'mode': case 'setmode': return setMode(pick(payload, ['mode', 'Mode', 'value', 'Value'], state.mode), pick(payload, ['zoom', 'Zoom'], state.zoom), pick(payload, ['fontScale', 'FontScale'], state.fontScale));
        case 'zoom': case 'setzoom': return setZoom(pick(payload, ['zoom', 'Zoom', 'value', 'Value'], payload));
        case 'fontscale': case 'setfontscale': return setFontScale(pick(payload, ['fontScale', 'FontScale', 'value', 'Value'], payload));
        case 'scrollto': case 'navigate': return scrollToTarget(pick(payload, ['target', 'Target', 'id', 'Id', 'page', 'Page'], payload));
        case 'clear': return clear();
        case 'ping': sendToHost({ type: 'pong', protocolVersion: PROTOCOL_VERSION }); return true;
        default: throw new Error(`Tipo de mensaje no compatible: ${type || '(vacío)'}`);
      }
    } catch (error) {
      setStatus('No se pudo procesar el mensaje recibido.', true);
      sendError('invalid-message', error);
      return null;
    }
  }

  function announceReady(attempt = 0) {
    if (!state.readySent) state.readySent = sendToHost({
      type: 'ready', protocolVersion: PROTOCOL_VERSION, renderer: 'marked-hbfm', rendererVersion: RENDERER_VERSION,
      capabilities: ['book', 'mobile', 'zoom', 'fontScale', 'navigation', 'resourceBlobs', 'metadata', 'customCss', 'theme-5ePHB', 'sanitizedHtml']
    });
    else flushHostMessages();
    if (!state.readySent && attempt < 40) globalThis.setTimeout(() => announceReady(attempt + 1), 50);
  }

  globalThis.addEventListener('HybridWebViewMessageReceived', (event) => { void handleHostMessage(event?.detail?.message); });
  documentRoot.addEventListener('click', (event) => {
    const anchor = event.target instanceof Element ? event.target.closest('a') : null;
    if (!anchor || !documentRoot.contains(anchor)) return;
    const externalHref = anchor.dataset.externalHref;
    const href = externalHref || anchor.getAttribute('href') || '';
    event.preventDefault();
    if (!externalHref && href.startsWith('#')) { scrollToTarget(href); return; }
    sendToHost({ type: 'link', href, documentId: state.documentId });
  });
  documentRoot.addEventListener('error', (event) => {
    const media = event.target;
    if (!(media instanceof HTMLImageElement) && !(media instanceof HTMLVideoElement)) return;
    media.classList.add('resourceMissing');
    sendToHost({ type: 'resourceError', source: media.currentSrc || media.getAttribute('src') || '', documentId: state.documentId });
  }, true);

  globalThis.DndReader = Object.freeze({
    renderDocument, registerResource, setMode, setZoom, setFontScale, scrollTo: scrollToTarget, clear,
    handleHostMessage, parseHomebreweryExport,
    getState: () => ({ ...state, blobResources: state.blobResources.size, resourceStyles: state.resourceStyles.size, pendingHostMessages: undefined, pageObserver: undefined })
  });

  try {
    getRenderer();
    shell.dataset.readerState = 'empty';
    setStatus('Seleccioná un documento descargado para leerlo sin conexión.');
  } catch (error) {
    shell.dataset.readerState = 'error';
    setStatus('No se pudo iniciar el motor de lectura offline.', true);
    sendError('renderer-unavailable', error);
  }
  announceReady();
})();
