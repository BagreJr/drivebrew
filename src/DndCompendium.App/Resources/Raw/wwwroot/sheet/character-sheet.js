(() => {
  'use strict';

  const layout = globalThis.DRIVEBREW_SHEET_LAYOUT;
  const stage = document.getElementById('sheet-stage');
  const status = document.getElementById('sheet-status');
  const pageButtons = Array.from(document.querySelectorAll('.pageButton'));
  const textFields = new Map();
  const checkFields = new Map();
  const requestedPage = Number(globalThis.location.hash.replace('#', ''));
  const state = {
    currentPage: Number.isInteger(requestedPage) && requestedPage >= 1 && requestedPage <= 3 ? requestedPage : 1,
    zoom: 1,
    readySent: false,
    pending: []
  };

  function bridge() {
    const candidate = globalThis.HybridWebView;
    return candidate && typeof candidate.SendRawMessage === 'function' ? candidate : null;
  }

  function send(payload) {
    const serialized = JSON.stringify(payload);
    const current = bridge();
    if (current) {
      while (state.pending.length) current.SendRawMessage(state.pending.shift());
      current.SendRawMessage(serialized);
      return true;
    }
    if (state.pending.length < 512) state.pending.push(serialized);
    return false;
  }

  function announceReady(attempt = 0) {
    if (!state.readySent) state.readySent = send({ type: 'ready', editor: 'visual-character-sheet', version: 1 });
    if (!state.readySent && attempt < 50) globalThis.setTimeout(() => announceReady(attempt + 1), 50);
  }

  function fieldFont(field) {
    if (field.kind === 'textarea') return 9;
    return Math.max(7, Math.min(12, field.h * 7));
  }

  function position(element, field) {
    element.style.left = `${field.x}%`;
    element.style.top = `${field.y}%`;
    element.style.width = `${field.w}%`;
    element.style.height = `${field.h}%`;
  }

  function createTextField(field) {
    const element = document.createElement(field.kind === 'textarea' ? 'textarea' : 'input');
    if (field.kind === 'text') element.type = 'text';
    element.className = 'sheetField';
    element.dataset.name = field.name;
    element.setAttribute('aria-label', field.name);
    element.autocomplete = 'off';
    element.spellcheck = false;
    element.style.setProperty('--field-font', `${fieldFont(field)}px`);
    position(element, field);
    element.addEventListener('input', () => send({ type: 'change', name: field.name, kind: 'text', value: element.value }));
    element.addEventListener('focus', () => globalThis.setTimeout(() => element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' }), 80));
    textFields.set(field.name, element);
    return element;
  }

  function createCheckField(field) {
    const element = document.createElement('button');
    element.type = 'button';
    element.className = 'sheetCheck';
    element.dataset.name = field.name;
    element.setAttribute('role', 'checkbox');
    element.setAttribute('aria-label', field.name);
    element.setAttribute('aria-checked', 'false');
    position(element, field);
    element.addEventListener('click', () => {
      const checked = !element.classList.contains('checked');
      element.classList.toggle('checked', checked);
      element.setAttribute('aria-checked', String(checked));
      send({ type: 'change', name: field.name, kind: 'checkbox', checked });
    });
    checkFields.set(field.name, element);
    return element;
  }

  function render() {
    if (!layout || !Array.isArray(layout.pages) || !Array.isArray(layout.fields)) {
      status.textContent = 'No se encontró el diseño de la hoja.';
      send({ type: 'error', message: 'Falta el diseño visual de la hoja.' });
      return;
    }

    status.remove();
    for (const page of layout.pages) {
      const viewport = document.createElement('section');
      viewport.className = 'pageViewport';
      viewport.dataset.page = String(page.number);
      viewport.hidden = page.number !== state.currentPage;

      const sheet = document.createElement('div');
      sheet.className = 'sheetPage';
      sheet.dataset.page = String(page.number);
      const image = document.createElement('img');
      image.className = 'pageImage';
      image.src = page.image;
      image.alt = `Hoja de personaje, página ${page.number}`;
      sheet.appendChild(image);

      for (const field of layout.fields.filter(item => item.page === page.number)) {
        sheet.appendChild(field.kind === 'checkbox' ? createCheckField(field) : createTextField(field));
      }
      viewport.appendChild(sheet);
      stage.appendChild(viewport);
    }
    resizePages();
  }

  function resizePages() {
    const available = Math.max(320, stage.clientWidth - 16);
    const baseWidth = Math.min(816, available);
    for (const page of document.querySelectorAll('.sheetPage')) {
      page.style.setProperty('--page-width', `${Math.round(baseWidth * state.zoom)}px`);
      page.style.setProperty('--page-height', `${Math.round(baseWidth * state.zoom * 792 / 612)}px`);
    }
  }

  function showPage(pageNumber) {
    state.currentPage = Math.max(1, Math.min(3, pageNumber));
    for (const page of document.querySelectorAll('.pageViewport')) page.hidden = Number(page.dataset.page) !== state.currentPage;
    for (const button of pageButtons) button.classList.toggle('active', Number(button.dataset.page) === state.currentPage);
    globalThis.history.replaceState(null, '', `#${state.currentPage}`);
    stage.scrollTo({ top: 0, left: 0, behavior: 'instant' });
  }

  function setZoom(value) {
    state.zoom = Math.max(1, Math.min(2.5, value));
    resizePages();
  }

  function loadData(payload) {
    const values = payload?.textFields && typeof payload.textFields === 'object' ? payload.textFields : {};
    const checked = new Set(Array.isArray(payload?.checkedFields) ? payload.checkedFields : []);
    for (const [name, element] of textFields) element.value = typeof values[name] === 'string' ? values[name] : '';
    for (const [name, element] of checkFields) {
      const isChecked = checked.has(name);
      element.classList.toggle('checked', isChecked);
      element.setAttribute('aria-checked', String(isChecked));
    }
  }

  function handleHostMessage(message) {
    try {
      const parsed = typeof message === 'string' ? JSON.parse(message) : message;
      if (parsed?.type === 'load') loadData(parsed.payload);
    } catch (error) {
      send({ type: 'error', message: error instanceof Error ? error.message : String(error) });
    }
  }

  for (const button of pageButtons) button.addEventListener('click', () => showPage(Number(button.dataset.page)));
  document.getElementById('zoom-out').addEventListener('click', () => setZoom(state.zoom - 0.25));
  document.getElementById('zoom-fit').addEventListener('click', () => setZoom(1));
  document.getElementById('zoom-in').addEventListener('click', () => setZoom(state.zoom + 0.25));
  globalThis.addEventListener('resize', resizePages);
  globalThis.addEventListener('HybridWebViewMessageReceived', event => handleHostMessage(event?.detail?.message));
  globalThis.addEventListener('pagehide', () => send({ type: 'save' }));

  render();
  showPage(state.currentPage);
  announceReady();
})();
