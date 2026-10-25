(() => {
  'use strict';

  const layout = globalThis.DRIVEBREW_SHEET_LAYOUT;
  const rules = globalThis.DRIVEBREW_CHARACTER_RULES;
  const stage = document.getElementById('sheet-stage');
  const status = document.getElementById('sheet-status');
  const pageButtons = Array.from(document.querySelectorAll('.pageButton'));
  const backdrop = document.getElementById('edit-backdrop');
  const editTitle = document.getElementById('edit-title');
  const editHelp = document.getElementById('edit-help');
  const fieldInput = document.getElementById('field-input');
  const fieldTextarea = document.getElementById('field-textarea');
  const quickValues = document.getElementById('quick-values');
  const valueDown = document.getElementById('value-down');
  const valueUp = document.getElementById('value-up');
  const autoToggle = document.getElementById('auto-toggle');
  const textFields = new Map();
  const checkFields = new Map();
  const values = new Map();
  const checkedFields = new Set();
  const labels = new Map();
  const numericFields = new Set();
  const autoManaged = new Set();
  const requestedPage = Number(globalThis.location.hash.replace('#', ''));

  const MIRRORS = new Map([
    ['CharacterName', ['CharacterName 2']],
    ['CharacterName 2', ['CharacterName']]
  ]);

  const AUTO_HINTS = new Map([
    ['ProfBonus', 'Se calcula a partir del nivel total.'],
    ['Initiative', 'Se calcula con el modificador de Destreza.'],
    ['Passive', 'Se calcula como 10 + Perception.'],
    ['SpellSaveDC  2', 'Se calcula como 8 + competencia + característica de spellcasting.'],
    ['SpellAtkBonus 2', 'Se calcula con competencia + característica de spellcasting.']
  ]);

  const state = {
    currentPage: Number.isInteger(requestedPage) && requestedPage >= 1 && requestedPage <= 3 ? requestedPage : 1,
    zoom: 1,
    readySent: false,
    pending: [],
    autoCalc: true,
    activeField: null,
    activeElement: null,
    activeInput: null,
    initialValue: '',
    saveTimer: 0
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

  function scheduleSave(delay = 650) {
    globalThis.clearTimeout(state.saveTimer);
    state.saveTimer = globalThis.setTimeout(() => send({ type: 'save' }), delay);
  }

  function announceReady(attempt = 0) {
    if (!state.readySent) state.readySent = send({ type: 'ready', editor: 'visual-character-sheet', version: 2 });
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
    const element = document.createElement('button');
    element.type = 'button';
    element.className = 'sheetField';
    element.dataset.name = field.name;
    element.dataset.kind = field.kind;
    element.setAttribute('aria-label', field.name);
    element.style.setProperty('--field-font', `${fieldFont(field)}px`);
    position(element, field);
    element.addEventListener('click', () => openEditor(field, element));
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
      setChecked(field.name, checked, true);
      recalculateAll();
      scheduleSave();
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

  function displayValue(name) {
    const element = textFields.get(name);
    if (element) element.textContent = values.get(name) ?? '';
  }

  function setValue(name, value, notify = true, recalculate = false) {
    const normalized = String(value ?? '');
    if (normalized) values.set(name, normalized);
    else values.delete(name);
    displayValue(name);
    if (notify) send({ type: 'change', name, kind: 'text', value: normalized });

    for (const mirror of MIRRORS.get(name) ?? []) {
      if (normalized) values.set(mirror, normalized);
      else values.delete(mirror);
      displayValue(mirror);
      if (notify) send({ type: 'change', name: mirror, kind: 'text', value: normalized });
    }
    if (recalculate) recalculateAll();
  }

  function setChecked(name, checked, notify) {
    if (checked) checkedFields.add(name);
    else checkedFields.delete(name);
    const element = checkFields.get(name);
    if (element) {
      element.classList.toggle('checked', checked);
      element.setAttribute('aria-checked', String(checked));
      element.setAttribute('aria-label', `${labels.get(name) ?? name}: ${checked ? 'seleccionado' : 'no seleccionado'}`);
    }
    if (notify) send({ type: 'change', name, kind: 'checkbox', checked });
  }

  function setAutomatic(name, value) {
    const normalized = String(value);
    const current = values.get(name) ?? '';
    if (current && !autoManaged.has(name)) return;
    autoManaged.add(name);
    if (current !== normalized) setValue(name, normalized, true, false);
  }

  function recognizeAutomaticValues() {
    for (const [name, calculated] of rules.calculate(values, checkedFields)) {
      if ((values.get(name) ?? '') === calculated) autoManaged.add(name);
    }
  }

  function recalculateAll() {
    if (!state.autoCalc) return;
    for (const [name, calculated] of rules.calculate(values, checkedFields)) setAutomatic(name, calculated);
  }

  function helpFor(name) {
    if (AUTO_HINTS.has(name)) return AUTO_HINTS.get(name);
    const ability = Object.values(rules.ABILITIES).find(item => item.score === name);
    if (ability) return 'Al cambiarla se recalculan el modificador, la salvación y sus habilidades.';
    if (name === 'ClassLevel') return 'Ejemplo: Fighter 5. La competencia se calcula con el nivel total.';
    if (name === 'SpellcastingAbility 2') return 'Usá INT, WIS o CHA. El ataque y la dificultad se recalculan.';
    const save = rules.SAVES.find(item => item[0] === name);
    if (save) return `Resultado automático: ${rules.ABILITIES[save[1]].label} + competencia si está marcada.`;
    const skill = rules.SKILLS.find(item => item[0] === name);
    if (skill) return `Resultado automático: ${rules.ABILITIES[skill[1]].label} + competencia si está marcada.`;
    return 'El cambio se guarda en la hoja de este personaje.';
  }

  function quickOptionsFor(name) {
    if (Object.values(rules.ABILITIES).some(item => item.score === name)) return ['15', '14', '13', '12', '10', '8'];
    if (name === 'SpellcastingAbility 2') return ['INT', 'WIS', 'CHA'];
    return [];
  }

  function updateQuickValues(name) {
    const options = quickOptionsFor(name);
    quickValues.replaceChildren();
    quickValues.hidden = options.length === 0;
    for (const option of options) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = option;
      button.addEventListener('click', () => {
        state.activeInput.value = option;
        updateActiveValue();
      });
      quickValues.appendChild(button);
    }
  }

  function openEditor(field, element) {
    state.activeField = field;
    state.activeElement = element;
    state.initialValue = values.get(field.name) ?? '';
    const multiline = field.kind === 'textarea';
    fieldInput.hidden = multiline;
    fieldTextarea.hidden = !multiline;
    state.activeInput = multiline ? fieldTextarea : fieldInput;
    state.activeInput.value = state.initialValue;
    fieldInput.inputMode = numericFields.has(field.name) ? 'numeric' : 'text';
    editTitle.textContent = labels.get(field.name) ?? field.name;
    editHelp.textContent = helpFor(field.name);
    valueDown.hidden = valueUp.hidden = !numericFields.has(field.name);
    updateQuickValues(field.name);
    element.classList.add('editing');
    backdrop.hidden = false;
    globalThis.setTimeout(() => {
      state.activeInput.focus();
      state.activeInput.setSelectionRange(state.activeInput.value.length, state.activeInput.value.length);
    }, 80);
  }

  function updateActiveValue() {
    if (!state.activeField || !state.activeInput) return;
    autoManaged.delete(state.activeField.name);
    setValue(state.activeField.name, state.activeInput.value, true, true);
  }

  function closeEditor(save) {
    if (!state.activeField) return;
    if (!save) setValue(state.activeField.name, state.initialValue, true, true);
    state.activeElement?.classList.remove('editing');
    state.activeField = null;
    state.activeElement = null;
    state.activeInput = null;
    backdrop.hidden = true;
    if (save) scheduleSave(100);
  }

  function stepValue(change) {
    if (!state.activeInput) return;
    const current = rules.parseNumber(state.activeInput.value) ?? 0;
    state.activeInput.value = String(current + change);
    updateActiveValue();
  }

  function loadData(payload) {
    values.clear();
    checkedFields.clear();
    labels.clear();
    numericFields.clear();
    autoManaged.clear();

    const initialValues = payload?.textFields && typeof payload.textFields === 'object' ? payload.textFields : {};
    for (const [name, value] of Object.entries(initialValues)) if (typeof value === 'string' && value) values.set(name, value);
    for (const name of Array.isArray(payload?.checkedFields) ? payload.checkedFields : []) checkedFields.add(name);
    for (const [name, label] of Object.entries(payload?.fieldLabels ?? {})) labels.set(name, String(label));
    for (const name of Array.isArray(payload?.numericFields) ? payload.numericFields : []) numericFields.add(name);

    for (const name of textFields.keys()) {
      displayValue(name);
      textFields.get(name).setAttribute('aria-label', labels.get(name) ?? name);
    }
    for (const name of checkFields.keys()) setChecked(name, checkedFields.has(name), false);
    recognizeAutomaticValues();
  }

  function handleHostMessage(message) {
    try {
      const parsed = typeof message === 'string' ? JSON.parse(message) : message;
      if (parsed?.type === 'load') loadData(parsed.payload);
    } catch (error) {
      send({ type: 'error', message: error instanceof Error ? error.message : String(error) });
    }
  }

  fieldInput.addEventListener('input', updateActiveValue);
  fieldTextarea.addEventListener('input', updateActiveValue);
  valueDown.addEventListener('click', () => stepValue(-1));
  valueUp.addEventListener('click', () => stepValue(1));
  document.getElementById('edit-cancel').addEventListener('click', () => closeEditor(false));
  document.getElementById('edit-clear').addEventListener('click', () => {
    if (!state.activeInput) return;
    state.activeInput.value = '';
    updateActiveValue();
    state.activeInput.focus();
  });
  document.getElementById('edit-done').addEventListener('click', () => closeEditor(true));
  autoToggle.addEventListener('click', () => {
    state.autoCalc = !state.autoCalc;
    autoToggle.classList.toggle('active', state.autoCalc);
    autoToggle.textContent = state.autoCalc ? 'Auto: sí' : 'Auto: no';
    if (state.autoCalc) recalculateAll();
  });

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
