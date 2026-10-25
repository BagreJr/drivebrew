(() => {
  'use strict';

  const layout = globalThis.DRIVEBREW_SHEET_LAYOUT;
  const rules = globalThis.DRIVEBREW_CHARACTER_RULES;
  const sheetStage = document.getElementById('sheet-stage');
  const editStage = document.getElementById('edit-stage');
  const playStage = document.getElementById('play-stage');
  const sectionTabs = document.getElementById('section-tabs');
  const status = document.getElementById('sheet-status');
  const pageButtons = Array.from(document.querySelectorAll('.pageButton'));
  const modeButtons = Array.from(document.querySelectorAll('.modeButton'));
  const pdfOnly = Array.from(document.querySelectorAll('.pdfOnly'));
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
  const abilityBaseScores = new Map();
  const proficiencyRanks = new Map();
  const otherBonuses = new Map();
  const overrides = new Map();
  const autoManaged = new Set();
  const fieldDefinitions = new Map();
  let modifiers = [];
  let features = [];
  let featureSearchResults = [];
  let identitySearchResults = [];
  let identitySearchQuery = '';
  let preparedSpells = [];
  let knownSpells = [];
  let spellSearchResults = [];
  let spellSearchQuery = '';
  let spellSearchLevel = '';
  let resources = [];
  let editSections = [];
  let ruleSet = '2014';

  const requestedPage = Number(globalThis.location.hash.replace('#', ''));
  const ABILITY_NAMES = new Set(Object.keys(rules.ABILITIES));
  const RESULT_FIELDS = new Set([
    ...Object.values(rules.ABILITIES).map(item => item.modifier),
    ...rules.SAVES.map(item => item[0]), ...rules.SKILLS.map(item => item[0]),
    'ProfBonus', 'Initiative', 'Passive', 'SpellSaveDC  2', 'SpellAtkBonus 2'
  ]);
  const MIRRORS = new Map([
    ['CharacterName', ['CharacterName 2']],
    ['CharacterName 2', ['CharacterName']]
  ]);
  const AUTO_HINTS = new Map([
    ['ProfBonus', 'Se calcula a partir del nivel total. Podés usar un override para reglas homebrew.'],
    ['Initiative', 'Destreza + otras bonificaciones activas.'],
    ['Passive', '10 + Perception + otras bonificaciones.'],
    ['SpellSaveDC  2', '8 + competencia + característica de spellcasting + otros bonos.'],
    ['SpellAtkBonus 2', 'Competencia + característica de spellcasting + otros bonos.']
  ]);

  const state = {
    mode: 'edit',
    sectionId: 'overview',
    currentPage: Number.isInteger(requestedPage) && requestedPage >= 1 && requestedPage <= 3 ? requestedPage : 1,
    zoom: 1,
    readySent: false,
    pending: [],
    autoCalc: true,
    structuredEditing: false,
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
    if (!state.readySent) state.readySent = send({ type: 'ready', editor: 'character-sheet-2', version: 3 });
    if (!state.readySent && attempt < 50) globalThis.setTimeout(() => announceReady(attempt + 1), 50);
  }

  function structuredOptions() {
    return { abilityBaseScores, proficiencyRanks, otherBonuses, overrides, modifiers };
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
    element.addEventListener('click', () => openPdfFieldEditor(field, element));
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
      setChecked(field.name, !checkedFields.has(field.name), true);
      state.structuredEditing = true;
      recalculateAll();
      scheduleSave();
    });
    checkFields.set(field.name, element);
    return element;
  }

  function renderPdfPages() {
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
      for (const field of layout.fields.filter(item => item.page === page.number))
        sheet.appendChild(field.kind === 'checkbox' ? createCheckField(field) : createTextField(field));
      viewport.appendChild(sheet);
      sheetStage.appendChild(viewport);
    }
    resizePages();
  }

  function resizePages() {
    const available = Math.max(320, sheetStage.clientWidth - 16);
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
    if (state.mode === 'pdf') sheetStage.scrollTo({ top: 0, left: 0, behavior: 'instant' });
  }

  function setZoom(value) {
    state.zoom = Math.max(1, Math.min(2.5, value));
    resizePages();
  }

  function setMode(mode) {
    state.mode = ['edit', 'play', 'pdf'].includes(mode) ? mode : 'edit';
    for (const button of modeButtons) button.classList.toggle('active', button.dataset.mode === state.mode);
    editStage.hidden = state.mode !== 'edit';
    playStage.hidden = state.mode !== 'play';
    sheetStage.hidden = state.mode !== 'pdf';
    sectionTabs.hidden = state.mode !== 'edit';
    for (const element of pdfOnly) element.hidden = state.mode !== 'pdf';
    if (state.mode === 'play') renderPlayMode();
    if (state.mode === 'pdf') resizePages();
  }

  function displayValue(name) {
    const value = values.get(name) ?? '';
    const pdfElement = textFields.get(name);
    if (pdfElement) pdfElement.textContent = value;
    for (const element of document.querySelectorAll('[data-value-name]')) {
      if (element.dataset.valueName === name) element.textContent = value || '—';
    }
    for (const input of document.querySelectorAll('[data-field-name]')) {
      if (input.dataset.fieldName === name && document.activeElement !== input && input.value !== value) input.value = value;
    }
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

  function setBaseValue(name, value, notify = true) {
    const parsed = rules.parseNumber(value);
    if (parsed === null) abilityBaseScores.delete(name);
    else abilityBaseScores.set(name, Math.max(1, Math.min(30, Math.trunc(parsed))));
    state.structuredEditing = true;
    if (notify) send({ type: 'change', name, kind: 'base', value: parsed === null ? '' : String(Math.trunc(parsed)) });
    recalculateAll();
  }

  function setRank(name, rank, notify = true) {
    const normalized = Math.max(0, Math.min(2, Number(rank) || 0));
    if (normalized) proficiencyRanks.set(name, normalized);
    else proficiencyRanks.delete(name);
    const toggle = fieldDefinitions.get(name)?.toggleName;
    if (toggle) setChecked(toggle, normalized > 0, false);
    state.structuredEditing = true;
    if (notify) send({ type: 'change', name, kind: 'proficiency', value: String(normalized) });
    recalculateAll();
  }

  function setBonus(name, value, notify = true) {
    const parsed = rules.parseNumber(value);
    if (parsed === null || parsed === 0) otherBonuses.delete(name);
    else otherBonuses.set(name, parsed);
    state.structuredEditing = true;
    if (notify) send({ type: 'change', name, kind: 'bonus', value: parsed === null ? '' : String(parsed) });
    recalculateAll();
  }

  function setOverride(name, value, notify = true) {
    const normalized = String(value ?? '').trim();
    if (normalized) overrides.set(name, normalized);
    else overrides.delete(name);
    state.structuredEditing = true;
    if (notify) send({ type: 'change', name, kind: 'override', value: normalized });
    recalculateAll();
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
    for (const input of document.querySelectorAll('[data-check-name]')) {
      if (input.dataset.checkName === name && input.checked !== checked) input.checked = checked;
    }
    if (notify) send({ type: 'change', name, kind: 'checkbox', checked });
  }

  function setAutomatic(name, value) {
    const normalized = String(value);
    const current = values.get(name) ?? '';
    const structuredTarget = ABILITY_NAMES.has(name) || RESULT_FIELDS.has(name);
    if (current && !autoManaged.has(name) && !(state.structuredEditing && structuredTarget)) return;
    autoManaged.add(name);
    if (current !== normalized) setValue(name, normalized, true, false);
  }

  function recognizeAutomaticValues() {
    for (const [name, calculated] of rules.calculate(values, checkedFields, structuredOptions()))
      if ((values.get(name) ?? '') === calculated) autoManaged.add(name);
  }

  function refreshCalculatedViews() {
    for (const ability of ABILITY_NAMES) {
      const input = document.querySelector(`[data-base-name="${ability}"]`);
      const base = abilityBaseScores.get(ability);
      if (input && document.activeElement !== input) input.value = base ?? '';
      const modifier = document.querySelector(`[data-modifier-name="${ability}"]`);
      if (modifier) modifier.textContent = values.get(rules.ABILITIES[ability].modifier) ?? '—';
      const breakdown = document.querySelector(`[data-breakdown-name="${ability}"]`);
      if (breakdown) {
        const parts = rules.explain(ability, values, checkedFields, structuredOptions());
        breakdown.textContent = parts.map(part => `${part.label} ${rules.signed(Number(part.value))}`).join(' · ');
      }
    }
    for (const element of document.querySelectorAll('[data-derived-name]'))
      element.textContent = values.get(element.dataset.derivedName) ?? '—';
  }

  function recalculateAll() {
    if (!state.autoCalc) return;
    for (const [name, calculated] of rules.calculate(values, checkedFields, structuredOptions())) setAutomatic(name, calculated);
    refreshCalculatedViews();
  }

  function helpFor(name) {
    if (AUTO_HINTS.has(name)) return AUTO_HINTS.get(name);
    const ability = Object.values(rules.ABILITIES).find(item => item.score === name);
    if (ability) return 'Al cambiar el valor base se recalculan el total, el modificador y todos sus resultados dependientes.';
    if (name === 'ClassLevel') return 'Ejemplo: Fighter 5 / Rogue 2. Se usa el nivel total para la competencia.';
    if (name === 'SpellcastingAbility 2') return 'Usá INT, WIS o CHA. El ataque y la dificultad se recalculan.';
    return 'El cambio se guarda automáticamente en este personaje.';
  }

  function quickOptionsFor(name) {
    if (ABILITY_NAMES.has(name)) return ['15', '14', '13', '12', '10', '8'];
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
        updatePdfActiveValue();
      });
      quickValues.appendChild(button);
    }
  }

  function openPdfFieldEditor(field, element) {
    state.activeField = field;
    state.activeElement = element;
    state.initialValue = ABILITY_NAMES.has(field.name)
      ? String(abilityBaseScores.get(field.name) ?? values.get(field.name) ?? '')
      : values.get(field.name) ?? '';
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

  function updatePdfActiveValue() {
    if (!state.activeField || !state.activeInput) return;
    autoManaged.delete(state.activeField.name);
    if (ABILITY_NAMES.has(state.activeField.name)) setBaseValue(state.activeField.name, state.activeInput.value, true);
    else setValue(state.activeField.name, state.activeInput.value, true, true);
  }

  function closePdfEditor(save) {
    if (!state.activeField) return;
    if (!save) {
      if (ABILITY_NAMES.has(state.activeField.name)) setBaseValue(state.activeField.name, state.initialValue, true);
      else setValue(state.activeField.name, state.initialValue, true, true);
    }
    state.activeElement?.classList.remove('editing');
    state.activeField = null;
    state.activeElement = null;
    state.activeInput = null;
    backdrop.hidden = true;
    if (save) scheduleSave(100);
  }

  function stepPdfValue(change) {
    if (!state.activeInput) return;
    const current = rules.parseNumber(state.activeInput.value) ?? 0;
    state.activeInput.value = String(current + change);
    updatePdfActiveValue();
  }

  function focusBehavior(input) {
    input.addEventListener('focus', () => globalThis.setTimeout(() => input.scrollIntoView({ block: 'center', behavior: 'smooth' }), 280));
    input.addEventListener('keydown', event => {
      if (event.key !== 'Enter' || input.tagName === 'TEXTAREA') return;
      event.preventDefault();
      const fields = Array.from(editStage.querySelectorAll('input:not([type="checkbox"]), textarea, select'));
      const next = fields[fields.indexOf(input) + 1];
      if (next) next.focus(); else input.blur();
    });
  }

  function autoGrow(textarea) {
    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(320, Math.max(112, textarea.scrollHeight))}px`;
  }

  function createAbilityCard(name) {
    const info = rules.ABILITIES[name];
    const card = document.createElement('div');
    card.className = 'abilityCard';
    const label = document.createElement('label');
    label.htmlFor = `base-${name}`;
    label.textContent = info.label.toUpperCase();
    const input = document.createElement('input');
    input.id = `base-${name}`;
    input.type = 'number';
    input.min = '1';
    input.max = '30';
    input.inputMode = 'numeric';
    input.enterKeyHint = 'next';
    input.dataset.baseName = name;
    input.value = abilityBaseScores.get(name) ?? '';
    input.setAttribute('aria-label', `${info.label}: valor base`);
    input.addEventListener('input', () => { setBaseValue(name, input.value); scheduleSave(); });
    focusBehavior(input);
    const modifier = document.createElement('strong');
    modifier.className = 'abilityModifier';
    modifier.dataset.modifierName = name;
    modifier.textContent = values.get(info.modifier) ?? '—';
    const breakdown = document.createElement('small');
    breakdown.className = 'abilityBreakdown';
    breakdown.dataset.breakdownName = name;
    card.append(label, input, modifier, breakdown);
    return card;
  }

  function createProficiencyCard(field) {
    const card = document.createElement('div');
    card.className = 'fieldCard wide';
    const title = document.createElement('span');
    title.className = 'fieldLabel';
    title.textContent = field.label;
    const row = document.createElement('div');
    row.className = 'proficiencyRow';
    const selectWrap = document.createElement('div');
    const selectLabel = document.createElement('span');
    selectLabel.className = 'smallLabel';
    selectLabel.textContent = 'Competencia';
    const select = document.createElement('select');
    select.enterKeyHint = 'next';
    select.innerHTML = '<option value="0">Sin competencia</option><option value="1">Competente</option><option value="2">Expertise</option>';
    select.value = String(proficiencyRanks.get(field.name) ?? (field.toggleName && checkedFields.has(field.toggleName) ? 1 : 0));
    select.addEventListener('change', () => { setRank(field.name, select.value); scheduleSave(); });
    focusBehavior(select);
    selectWrap.append(selectLabel, select);
    const bonusWrap = document.createElement('div');
    const bonusLabel = document.createElement('span');
    bonusLabel.className = 'smallLabel';
    bonusLabel.textContent = 'Otro bono';
    const bonus = document.createElement('input');
    bonus.type = 'number';
    bonus.inputMode = 'decimal';
    bonus.enterKeyHint = 'next';
    bonus.value = otherBonuses.get(field.name) ?? '';
    bonus.setAttribute('aria-label', `${field.label}: otra bonificación`);
    bonus.addEventListener('input', () => { setBonus(field.name, bonus.value); scheduleSave(); });
    focusBehavior(bonus);
    bonusWrap.append(bonusLabel, bonus);
    const result = document.createElement('output');
    result.className = 'derivedValue';
    result.dataset.derivedName = field.name;
    result.textContent = values.get(field.name) ?? '—';
    row.append(selectWrap, bonusWrap, result);
    card.append(title, row);
    return card;
  }

  function createDerivedCard(field) {
    const card = document.createElement('div');
    card.className = 'fieldCard';
    const title = document.createElement('span');
    title.className = 'fieldLabel';
    title.textContent = field.label;
    const result = document.createElement('output');
    result.className = 'derivedValue';
    result.dataset.derivedName = field.name;
    result.textContent = values.get(field.name) ?? '—';
    const overrideLabel = document.createElement('span');
    overrideLabel.className = 'smallLabel';
    overrideLabel.textContent = 'Override opcional';
    const override = document.createElement('input');
    override.type = 'text';
    override.inputMode = field.numeric ? 'decimal' : 'text';
    override.enterKeyHint = 'next';
    override.value = overrides.get(field.name) ?? '';
    override.placeholder = 'Usar cálculo automático';
    override.addEventListener('input', () => { setOverride(field.name, override.value); scheduleSave(); });
    focusBehavior(override);
    card.append(title, result, overrideLabel, override);
    return card;
  }

  function createToggleCard(field) {
    const card = document.createElement('div');
    card.className = 'fieldCard';
    const label = document.createElement('label');
    label.className = 'toggleRow';
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.dataset.checkName = field.name;
    input.checked = checkedFields.has(field.name);
    input.addEventListener('change', () => { setChecked(field.name, input.checked, true); scheduleSave(); });
    const text = document.createElement('span');
    text.textContent = field.label;
    label.append(input, text);
    card.append(label);
    return card;
  }

  function stepAdaptiveValue(name, input, change) {
    input.value = String((rules.parseNumber(input.value) ?? 0) + change);
    autoManaged.delete(name);
    setValue(name, input.value, true, true);
    scheduleSave();
  }

  function createStandardField(field) {
    if (RESULT_FIELDS.has(field.name)) return createDerivedCard(field);
    const card = document.createElement('div');
    card.className = `fieldCard${field.kind === 'textarea' ? ' wide' : ''}`;
    const label = document.createElement('label');
    label.textContent = field.label;
    const input = document.createElement(field.kind === 'textarea' ? 'textarea' : 'input');
    input.dataset.fieldName = field.name;
    input.value = values.get(field.name) ?? '';
    input.enterKeyHint = 'next';
    if (input.tagName === 'INPUT') {
      input.type = field.numeric ? 'number' : 'text';
      input.inputMode = field.numeric ? 'decimal' : 'text';
    }
    const update = () => {
      autoManaged.delete(field.name);
      setValue(field.name, input.value, true, true);
      if (input.tagName === 'TEXTAREA') autoGrow(input);
      scheduleSave();
    };
    input.addEventListener('input', update);
    focusBehavior(input);
    label.htmlFor = `field-${field.name.replace(/[^a-z0-9]/gi, '-')}`;
    input.id = label.htmlFor;
    card.append(label);
    if (field.numeric) {
      const editor = document.createElement('div');
      editor.className = 'numericEditor';
      const down = document.createElement('button');
      down.type = 'button'; down.textContent = '−'; down.setAttribute('aria-label', `Restar uno a ${field.label}`);
      const up = document.createElement('button');
      up.type = 'button'; up.textContent = '+'; up.setAttribute('aria-label', `Sumar uno a ${field.label}`);
      down.addEventListener('click', () => stepAdaptiveValue(field.name, input, -1));
      up.addEventListener('click', () => stepAdaptiveValue(field.name, input, 1));
      editor.append(down, input, up);
      card.append(editor);
    } else card.append(input);
    if (input.tagName === 'TEXTAREA') globalThis.setTimeout(() => autoGrow(input), 0);
    return card;
  }

  function createRuleSetCard() {
    const card = document.createElement('div');
    card.className = 'fieldCard wide ruleSetCard';
    const label = document.createElement('label');
    label.textContent = 'Conjunto de reglas';
    const select = document.createElement('select');
    select.innerHTML = '<option value="2014">D&D 5e 2014</option><option value="2024">D&D 2024</option><option value="Homebrew">Homebrew / personalizado</option>';
    select.value = ruleSet;
    select.addEventListener('change', () => {
      ruleSet = select.value;
      send({ type: 'change', name: 'RuleSet', kind: 'ruleset', value: ruleSet });
      scheduleSave();
    });
    const help = document.createElement('small');
    help.className = 'fieldHelp';
    help.textContent = 'La ficha conserva esta elección y no mezcla reglas automáticamente.';
    card.append(label, select, help);
    return card;
  }

  function applyIdentityResult(result) {
    const kind = String(result.kind ?? '');
    if (kind === 'Class') {
      const currentLevel = rules.levelFromClass(values.get('ClassLevel')) ?? 1;
      setValue('ClassLevel', `${result.name} ${currentLevel}`, true, true);
    } else if (kind === 'Subclass') setValue('Digital.Subclass', result.name, true, false);
    else if (kind === 'Race') setValue('Race ', result.name, true, false);
    else if (kind === 'Background') setValue('Background', result.name, true, false);
    scheduleSave(100);
    renderEditSection();
  }

  function createIdentityManager() {
    const panel = document.createElement('section'); panel.className = 'integrationPanel identityManager';
    const heading = document.createElement('div'); heading.className = 'integrationHeading';
    const title = document.createElement('h2'); title.textContent = 'Elegir desde el Compendium'; heading.appendChild(title); panel.appendChild(heading);
    const search = document.createElement('div'); search.className = 'featureSearch';
    const input = document.createElement('input'); input.type = 'search'; input.placeholder = 'Clase, subclase, raza o background'; input.value = identitySearchQuery;
    const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Buscar';
    const run = () => {
      identitySearchQuery = input.value.trim();
      if (identitySearchQuery) send({ type: 'identitySearch', query: identitySearchQuery });
    };
    button.addEventListener('click', run);
    input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); run(); } });
    focusBehavior(input); search.append(input, button); panel.appendChild(search);
    if (identitySearchResults.length) {
      const results = document.createElement('div'); results.className = 'featureResults';
      for (const result of identitySearchResults) {
        const item = document.createElement('article'); item.className = 'featureResult identityResult';
        const name = document.createElement('strong'); name.textContent = result.name;
        const meta = document.createElement('span'); meta.textContent = [result.kind, result.parentClass || result.parent, result.origin].filter(Boolean).join(' · ');
        const snippet = document.createElement('p'); snippet.textContent = result.snippet || '';
        const actions = document.createElement('div'); actions.className = 'resultActions';
        const use = document.createElement('button'); use.type = 'button'; use.textContent = 'Usar'; use.addEventListener('click', () => applyIdentityResult(result));
        const read = document.createElement('button'); read.type = 'button'; read.className = 'secondaryAction'; read.textContent = 'Leer';
        read.disabled = !result.documentId;
        read.addEventListener('click', () => send({ type: 'openFeature', documentId: result.documentId, target: result.target }));
        actions.append(use, read); item.append(name, meta, snippet, actions); results.appendChild(item);
      }
      panel.appendChild(results);
    }
    return panel;
  }

  function createLevelUpCard() {
    const card = document.createElement('div'); card.className = 'fieldCard wide levelUpCard';
    const title = document.createElement('span'); title.className = 'fieldLabel'; title.textContent = 'Level Up';
    const current = values.get('ClassLevel') ?? '';
    const matches = current.match(/\b(?:[1-9]|1\d|20)\b/g) ?? [];
    const help = document.createElement('p'); help.className = 'fieldHelp';
    const button = document.createElement('button'); button.type = 'button'; button.className = 'primaryAction';
    if (matches.length !== 1 || Number(matches[0]) >= 20) {
      help.textContent = matches.length > 1
        ? 'El personaje tiene multiclassing: elegí manualmente qué clase aumenta para no inventar una decisión.'
        : 'Completá una clase y nivel entre 1 y 19 para preparar el siguiente nivel.';
      button.hidden = true;
    } else {
      const previous = Number(matches[0]); const next = previous + 1;
      help.textContent = `Vista previa: nivel total ${previous} → ${next}. Se recalculará competencia y se conservarán HP y elecciones manuales.`;
      button.textContent = `Subir a nivel ${next}`;
      button.addEventListener('click', () => {
        if (!globalThis.confirm(`¿Confirmar Level Up a nivel ${next}? No se elegirán feats ni features automáticamente.`)) return;
        setValue('ClassLevel', current.replace(matches[0], String(next)), true, true);
        scheduleSave(100); renderEditSection();
      });
    }
    card.append(title, help, button); return card;
  }

  function modifierTargets() {
    const names = [
      ...ABILITY_NAMES, 'ProfBonus', 'Initiative', 'Passive', 'SpellSaveDC  2', 'SpellAtkBonus 2',
      ...rules.SAVES.map(item => item[0]), ...rules.SKILLS.map(item => item[0])
    ];
    return Array.from(new Set(names)).map(name => ({ name, label: labels.get(name) ?? name }));
  }

  function sendModifier(modifier) {
    send({ type: 'modifierUpsert', ...modifier });
    state.structuredEditing = true;
    recalculateAll();
    scheduleSave(100);
  }

  function createModifierManager() {
    const manager = document.createElement('section');
    manager.className = 'modifierManager';
    const heading = document.createElement('div');
    heading.className = 'modifierHeading';
    const title = document.createElement('h2');
    title.textContent = 'Modificadores estructurados';
    const help = document.createElement('p');
    help.textContent = 'Solo se aplican los efectos que agregues de forma explícita. La app no interpreta texto de reglas.';
    heading.append(title, help); manager.appendChild(heading);

    const list = document.createElement('div');
    list.className = 'modifierList';
    for (const modifier of modifiers) {
      const row = document.createElement('article');
      row.className = `modifierItem${modifier.isActive ? '' : ' inactive'}`;
      const summary = document.createElement('div');
      const name = document.createElement('strong'); name.textContent = modifier.sourceName || 'Sin fuente';
      const detail = document.createElement('span');
      detail.textContent = `${labels.get(modifier.target) ?? modifier.target}: ${rules.signed(Number(modifier.value) || 0)} · ${modifier.bonusType || 'untyped'}`;
      summary.append(name, detail);
      const actions = document.createElement('div'); actions.className = 'modifierActions';
      const active = document.createElement('button'); active.type = 'button';
      active.textContent = modifier.isActive ? 'Activo' : 'Inactivo';
      active.addEventListener('click', () => {
        modifier.isActive = !modifier.isActive;
        sendModifier(modifier);
        renderEditSection();
      });
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'danger'; remove.textContent = 'Quitar';
      remove.addEventListener('click', () => {
        modifiers = modifiers.filter(item => item.id !== modifier.id);
        send({ type: 'modifierDelete', id: modifier.id });
        state.structuredEditing = true;
        recalculateAll();
        scheduleSave(100);
        renderEditSection();
      });
      actions.append(active, remove); row.append(summary, actions); list.appendChild(row);
    }
    if (!modifiers.length) {
      const empty = document.createElement('p'); empty.className = 'emptyHint';
      empty.textContent = 'Todavía no hay modificadores. Los valores base se conservan separados.';
      list.appendChild(empty);
    }
    manager.appendChild(list);

    const form = document.createElement('div'); form.className = 'modifierForm fieldCard wide';
    const source = document.createElement('input'); source.type = 'text'; source.placeholder = 'Fuente: feat, objeto o efecto';
    source.setAttribute('aria-label', 'Fuente del modificador');
    const target = document.createElement('select'); target.setAttribute('aria-label', 'Estadística afectada');
    for (const optionData of modifierTargets()) {
      const option = document.createElement('option'); option.value = optionData.name; option.textContent = optionData.label; target.appendChild(option);
    }
    const amount = document.createElement('input'); amount.type = 'number'; amount.inputMode = 'decimal'; amount.placeholder = 'Valor'; amount.setAttribute('aria-label', 'Valor del modificador');
    const type = document.createElement('select'); type.setAttribute('aria-label', 'Tipo de bonificación');
    type.innerHTML = '<option value="untyped">Sin tipo</option><option value="racial">Racial</option><option value="class">Clase</option><option value="feat">Feat</option><option value="item">Objeto</option><option value="temporary">Temporal</option><option value="manual">Manual</option>';
    const condition = document.createElement('input'); condition.type = 'text'; condition.placeholder = 'Condición (opcional)';
    const duration = document.createElement('input'); duration.type = 'text'; duration.placeholder = 'Duración (opcional)';
    const add = document.createElement('button'); add.type = 'button'; add.className = 'primaryAction'; add.textContent = 'Agregar modificador';
    add.addEventListener('click', () => {
      const numericValue = rules.parseNumber(amount.value);
      if (!source.value.trim() || numericValue === null) {
        source.focus();
        return;
      }
      const modifier = {
        id: globalThis.crypto?.randomUUID?.().replaceAll('-', '') ?? `${Date.now()}-${Math.random()}`,
        sourceName: source.value.trim(), target: target.value, value: numericValue,
        bonusType: type.value, condition: condition.value.trim(), duration: duration.value.trim(), isActive: true
      };
      modifiers.push(modifier);
      sendModifier(modifier);
      renderEditSection();
    });
    for (const input of [source, target, amount, type, condition, duration]) focusBehavior(input);
    form.append(source, target, amount, type, condition, duration, add);
    manager.appendChild(form);
    return manager;
  }

  function normalizeFeature(item) {
    return {
      id: item?.id ?? item?.Id,
      name: item?.name ?? item?.Name,
      kind: item?.kind ?? item?.Kind,
      sourceKey: item?.sourceKey ?? item?.SourceKey,
      documentId: item?.documentId ?? item?.DocumentId,
      target: item?.target ?? item?.Target,
      level: item?.level ?? item?.Level,
      personalNotes: item?.personalNotes ?? item?.PersonalNotes
    };
  }

  function createFeatureManager() {
    const manager = document.createElement('section'); manager.className = 'integrationPanel featureManager';
    const heading = document.createElement('div'); heading.className = 'integrationHeading';
    const title = document.createElement('h2'); title.textContent = 'Features vinculadas al Compendium';
    heading.appendChild(title); manager.appendChild(heading);
    const search = document.createElement('div'); search.className = 'featureSearch';
    const input = document.createElement('input'); input.type = 'search'; input.placeholder = 'Nombre o texto de la feature';
    input.setAttribute('aria-label', 'Buscar features offline');
    const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Buscar';
    const runSearch = () => { if (input.value.trim()) send({ type: 'featureSearch', query: input.value.trim() }); };
    button.addEventListener('click', runSearch);
    input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); runSearch(); } });
    focusBehavior(input); search.append(input, button); manager.appendChild(search);

    if (features.length) {
      const list = document.createElement('div'); list.className = 'integrationList linkedFeatures';
      for (const feature of features) {
        const item = document.createElement('article'); item.className = 'integrationItem';
        const open = document.createElement('button'); open.type = 'button'; open.className = 'integrationMain';
        const name = document.createElement('strong'); name.textContent = feature.name;
        const detail = document.createElement('span'); detail.textContent = [feature.kind, feature.level ? `Nivel ${feature.level}` : null].filter(Boolean).join(' · ');
        open.append(name, detail); open.addEventListener('click', () => send({ type: 'openFeature', documentId: feature.documentId, target: feature.target }));
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'compactDanger'; remove.textContent = 'Quitar';
        remove.addEventListener('click', () => {
          features = features.filter(item => item.id !== feature.id);
          send({ type: 'featureDelete', id: feature.id }); scheduleSave(100); renderEditSection();
        });
        item.append(open, remove); list.appendChild(item);
      }
      manager.appendChild(list);
    }

    if (featureSearchResults.length) {
      const resultsTitle = document.createElement('h3'); resultsTitle.textContent = 'Resultados offline'; manager.appendChild(resultsTitle);
      const results = document.createElement('div'); results.className = 'featureResults';
      for (const result of featureSearchResults) {
        const item = document.createElement('article'); item.className = 'featureResult';
        const name = document.createElement('strong'); name.textContent = result.name;
        const meta = document.createElement('span'); meta.textContent = [result.kind, result.origin, result.level ? `Nivel ${result.level}` : null].filter(Boolean).join(' · ');
        const snippet = document.createElement('p'); snippet.textContent = result.snippet || '';
        const add = document.createElement('button'); add.type = 'button';
        const already = features.some(feature => feature.id === result.id);
        add.textContent = already ? 'Agregada' : 'Agregar'; add.disabled = already;
        add.addEventListener('click', () => {
          const feature = normalizeFeature(result); features.push(feature);
          send({ type: 'featureAdd', ...feature }); scheduleSave(100); renderEditSection();
        });
        item.append(name, meta, snippet, add); results.appendChild(item);
      }
      manager.appendChild(results);
    }
    return manager;
  }

  function spellSummary(spell) {
    return `${spell.level === 0 ? 'Cantrip' : `Nivel ${spell.level}`} · ${spell.school} · ${spell.castingTime}`;
  }

  function createPreparedSpellsPanel(compact = false) {
    const panel = document.createElement('section');
    panel.className = 'integrationPanel';
    const heading = document.createElement('div'); heading.className = 'integrationHeading';
    const title = document.createElement('h2'); title.textContent = 'Spells preparados';
    const browse = document.createElement('button'); browse.type = 'button'; browse.textContent = 'Buscar y preparar';
    browse.addEventListener('click', () => send({ type: 'openSpells' }));
    heading.append(title, browse); panel.appendChild(heading);
    if (!compact) {
      const search = document.createElement('div'); search.className = 'spellSearch';
      const input = document.createElement('input'); input.type = 'search'; input.placeholder = 'Buscar spell offline'; input.value = spellSearchQuery;
      const level = document.createElement('select');
      level.innerHTML = '<option value="">Todos los niveles</option>' + Array.from({ length: 10 }, (_, index) => `<option value="${index}">${index === 0 ? 'Cantrips' : `Nivel ${index}`}</option>`).join('');
      level.value = spellSearchLevel;
      const run = document.createElement('button'); run.type = 'button'; run.textContent = 'Buscar';
      const searchNow = () => {
        spellSearchQuery = input.value.trim(); spellSearchLevel = level.value;
        send({ type: 'spellSearch', query: spellSearchQuery, level: spellSearchLevel === '' ? null : Number(spellSearchLevel) });
      };
      run.addEventListener('click', searchNow);
      input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); searchNow(); } });
      focusBehavior(input); search.append(input, level, run); panel.appendChild(search);
    }
    if (!preparedSpells.length) {
      const empty = document.createElement('p'); empty.className = 'emptyHint';
      empty.textContent = 'No hay spells preparados para este personaje.'; panel.appendChild(empty);
    } else {
      const list = document.createElement('div'); list.className = 'integrationList';
      for (const spell of preparedSpells) {
        const item = document.createElement('article'); item.className = 'integrationItem';
        const open = document.createElement('button'); open.type = 'button'; open.className = 'integrationMain';
        const name = document.createElement('strong'); name.textContent = spell.name;
        const detail = document.createElement('span'); detail.textContent = spellSummary(spell);
        open.append(name, detail); open.addEventListener('click', () => send({ type: 'openSpell', id: spell.id }));
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'compactDanger'; remove.textContent = 'Quitar';
        remove.addEventListener('click', () => {
          preparedSpells = preparedSpells.filter(item => item.id !== spell.id);
          send({ type: 'setPreparedSpell', id: spell.id, prepared: false });
          compact ? renderPlayMode() : renderEditSection();
        });
        item.append(open, remove); list.appendChild(item);
      }
      panel.appendChild(list);
    }

    if (!compact && knownSpells.length) {
      const knownTitle = document.createElement('h3'); knownTitle.textContent = 'Spells conocidos'; panel.appendChild(knownTitle);
      const knownList = document.createElement('div'); knownList.className = 'integrationList';
      for (const spell of knownSpells) {
        const item = document.createElement('article'); item.className = 'integrationItem';
        const open = document.createElement('button'); open.type = 'button'; open.className = 'integrationMain';
        open.innerHTML = `<strong></strong><span></span>`; open.querySelector('strong').textContent = spell.name; open.querySelector('span').textContent = spellSummary(spell);
        open.addEventListener('click', () => send({ type: 'openSpell', id: spell.id }));
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'compactDanger'; remove.textContent = 'Quitar';
        remove.addEventListener('click', () => { knownSpells = knownSpells.filter(item => item.id !== spell.id); send({ type: 'setKnownSpell', id: spell.id, known: false }); scheduleSave(100); renderEditSection(); });
        item.append(open, remove); knownList.appendChild(item);
      }
      panel.appendChild(knownList);
    }

    if (!compact && spellSearchResults.length) {
      const resultTitle = document.createElement('h3'); resultTitle.textContent = 'Resultados'; panel.appendChild(resultTitle);
      const results = document.createElement('div'); results.className = 'featureResults';
      for (const spell of spellSearchResults) {
        const item = document.createElement('article'); item.className = 'featureResult';
        const name = document.createElement('strong'); name.textContent = spell.name;
        const meta = document.createElement('span'); meta.textContent = `${spellSummary(spell)} · ${spell.range} · ${spell.duration} · ${spell.components || 'Sin componentes'}`;
        const effect = document.createElement('p'); effect.textContent = spell.effect || '';
        const actions = document.createElement('div'); actions.className = 'resultActions';
        const know = document.createElement('button'); know.type = 'button';
        const isKnown = knownSpells.some(item => item.id === spell.id); know.textContent = isKnown ? 'Conocido' : 'Agregar conocido'; know.disabled = isKnown;
        know.addEventListener('click', () => { knownSpells.push(spell); send({ type: 'setKnownSpell', id: spell.id, known: true }); scheduleSave(100); renderEditSection(); });
        const prepare = document.createElement('button'); prepare.type = 'button'; prepare.className = 'secondaryAction';
        const isPrepared = preparedSpells.some(item => item.id === spell.id); prepare.textContent = isPrepared ? 'Preparado' : 'Preparar'; prepare.disabled = isPrepared;
        prepare.addEventListener('click', () => { preparedSpells.push(spell); send({ type: 'setPreparedSpell', id: spell.id, prepared: true }); renderEditSection(); });
        actions.append(know, prepare); item.append(name, meta, effect, actions); results.appendChild(item);
      }
      panel.appendChild(results);
    }
    return panel;
  }

  function adjustResource(resource, delta, rerender) {
    const next = Math.max(0, Math.min(resource.maximum, resource.current + delta));
    const actual = next - resource.current;
    if (!actual) return;
    resource.current = next;
    send({ type: 'adjustResource', id: resource.id, delta: actual });
    if (rerender) rerender();
  }

  function createResourcesPanel() {
    const panel = document.createElement('section'); panel.className = 'playBlock';
    const title = document.createElement('h2'); title.textContent = 'Resources sincronizados'; panel.appendChild(title);
    if (!resources.length) {
      const empty = document.createElement('p'); empty.className = 'emptyHint';
      empty.textContent = 'Agregá spell slots u otros recursos desde Resource Manager.'; panel.appendChild(empty);
      return panel;
    }
    const list = document.createElement('div'); list.className = 'resourceQuickList';
    for (const resource of resources) {
      const row = document.createElement('div'); row.className = 'resourceQuickItem';
      const info = document.createElement('div');
      const name = document.createElement('strong'); name.textContent = resource.name;
      const value = document.createElement('span'); value.textContent = `${resource.current} / ${resource.maximum}${resource.unit ? ` ${resource.unit}` : ''}`;
      info.append(name, value);
      const actions = document.createElement('div'); actions.className = 'quickButtons';
      for (const delta of [-1, 1]) {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'quickAction'; button.textContent = delta < 0 ? '−' : '+';
        button.disabled = delta < 0 ? resource.current <= 0 : resource.current >= resource.maximum;
        button.addEventListener('click', () => adjustResource(resource, delta, renderPlayMode)); actions.appendChild(button);
      }
      row.append(info, actions); list.appendChild(row);
    }
    panel.appendChild(list);
    return panel;
  }

  function renderSectionTabs() {
    sectionTabs.replaceChildren();
    for (const section of editSections) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = section.name;
      button.classList.toggle('active', section.id === state.sectionId);
      button.addEventListener('click', () => {
        state.sectionId = section.id;
        renderEditSection();
        renderSectionTabs();
        editStage.scrollTo({ top: 0, behavior: 'instant' });
      });
      sectionTabs.appendChild(button);
    }
  }

  function renderEditSection() {
    const section = editSections.find(item => item.id === state.sectionId) ?? editSections[0];
    editStage.replaceChildren();
    if (!section) return;
    const wrapper = document.createElement('section');
    wrapper.className = 'editSection';
    const heading = document.createElement('header');
    heading.className = 'sectionHeading';
    const title = document.createElement('h1');
    title.textContent = section.name;
    const help = document.createElement('p');
    help.textContent = section.helpText;
    heading.append(title, help);
    wrapper.appendChild(heading);
    if (section.id === 'overview') {
      wrapper.appendChild(createRuleSetCard());
      wrapper.appendChild(createIdentityManager());
      wrapper.appendChild(createLevelUpCard());
    }
    if (section.id === 'features') {
      wrapper.appendChild(createFeatureManager());
      wrapper.appendChild(createModifierManager());
    }
    if (section.id === 'spells') wrapper.appendChild(createPreparedSpellsPanel());
    const abilityFields = section.fields.filter(field => ABILITY_NAMES.has(field.name));
    if (abilityFields.length) {
      const abilityGrid = document.createElement('div');
      abilityGrid.className = 'abilityGrid';
      for (const field of abilityFields) abilityGrid.appendChild(createAbilityCard(field.name));
      wrapper.appendChild(abilityGrid);
    }
    const grid = document.createElement('div');
    grid.className = 'fieldGrid';
    for (const field of section.fields.filter(item => !ABILITY_NAMES.has(item.name))) {
      const isProficiency = rules.SAVES.some(item => item[0] === field.name) || rules.SKILLS.some(item => item[0] === field.name);
      grid.appendChild(field.kind === 'toggle' ? createToggleCard(field)
        : isProficiency ? createProficiencyCard(field) : createStandardField(field));
    }
    wrapper.appendChild(grid);
    editStage.appendChild(wrapper);
    refreshCalculatedViews();
  }

  function adjustQuickField(name, change) {
    const current = rules.parseNumber(values.get(name)) ?? 0;
    autoManaged.delete(name);
    setValue(name, String(Math.max(0, current + change)), true, true);
    renderPlayMode();
    scheduleSave(120);
  }

  function quickCard(label, name, adjustable = false) {
    const card = document.createElement('div');
    card.className = 'quickCard';
    const value = document.createElement('span');
    value.className = 'value'; value.dataset.valueName = name; value.textContent = values.get(name) ?? '—';
    const caption = document.createElement('span');
    caption.className = 'caption'; caption.textContent = label;
    card.append(value, caption);
    if (adjustable) {
      const buttons = document.createElement('div'); buttons.className = 'quickButtons';
      for (const change of [-1, 1]) {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'quickAction';
        button.textContent = change < 0 ? '−' : '+';
        button.setAttribute('aria-label', `${change < 0 ? 'Restar' : 'Sumar'} uno a ${label}`);
        button.addEventListener('click', () => adjustQuickField(name, change));
        buttons.appendChild(button);
      }
      card.appendChild(buttons);
    }
    return card;
  }

  function renderPlayMode() {
    playStage.replaceChildren();
    const wrapper = document.createElement('section'); wrapper.className = 'editSection';
    const header = document.createElement('header'); header.className = 'playHeader';
    const title = document.createElement('h1'); title.textContent = values.get('CharacterName') || 'Personaje';
    const subtitle = document.createElement('p');
    subtitle.textContent = [values.get('Race '), values.get('ClassLevel'), values.get('Digital.Subclass')].filter(Boolean).join(' · ') || 'Completá los datos en Editar';
    header.append(title, subtitle); wrapper.appendChild(header);
    const abilities = document.createElement('div'); abilities.className = 'abilityGrid';
    for (const ability of ABILITY_NAMES) {
      const card = document.createElement('div'); card.className = 'abilityCard';
      const label = document.createElement('label'); label.textContent = ability;
      const score = document.createElement('span'); score.className = 'value'; score.dataset.valueName = ability; score.textContent = values.get(ability) ?? '—';
      const modifier = document.createElement('strong'); modifier.className = 'abilityModifier'; modifier.textContent = values.get(rules.ABILITIES[ability].modifier) ?? '—';
      card.append(label, score, modifier); abilities.appendChild(card);
    }
    wrapper.appendChild(abilities);
    const quick = document.createElement('div'); quick.className = 'quickGrid';
    quick.append(quickCard('HP actuales', 'HPCurrent', true), quickCard('HP temporales', 'HPTemp', true),
      quickCard('Armor Class', 'AC'), quickCard('Iniciativa', 'Initiative'));
    wrapper.appendChild(quick);
    const checks = document.createElement('div'); checks.className = 'playBlock';
    const checksTitle = document.createElement('h2'); checksTitle.textContent = 'Sesión'; checks.appendChild(checksTitle);
    const inspiration = document.createElement('label'); inspiration.className = 'toggleRow';
    const inspirationInput = document.createElement('input'); inspirationInput.type = 'checkbox'; inspirationInput.checked = Boolean(values.get('Inspiration'));
    inspirationInput.addEventListener('change', () => { setValue('Inspiration', inspirationInput.checked ? '1' : '', true); scheduleSave(); });
    const inspirationText = document.createElement('span'); inspirationText.textContent = 'Inspiración'; inspiration.append(inspirationInput, inspirationText); checks.appendChild(inspiration);
    for (const [name, label] of [['Check Box 12', 'Death save: éxito 1'], ['Check Box 13', 'Death save: éxito 2'], ['Check Box 14', 'Death save: éxito 3'], ['Check Box 15', 'Death save: fallo 1'], ['Check Box 16', 'Death save: fallo 2'], ['Check Box 17', 'Death save: fallo 3']]) {
      const row = document.createElement('label'); row.className = 'toggleRow';
      const input = document.createElement('input'); input.type = 'checkbox'; input.checked = checkedFields.has(name);
      input.addEventListener('change', () => { setChecked(name, input.checked, true); scheduleSave(); });
      const text = document.createElement('span'); text.textContent = label; row.append(input, text); checks.appendChild(row);
    }
    wrapper.appendChild(checks);
    wrapper.appendChild(createResourcesPanel());
    const prepared = createPreparedSpellsPanel(true);
    prepared.classList.add('playBlock');
    wrapper.appendChild(prepared);
    for (const [titleText, field] of [['Features & Traits', 'Features and Traits'], ['Ataques y spellcasting', 'AttacksSpellcasting'], ['Notas', 'Digital.Notes']]) {
      const block = document.createElement('section'); block.className = 'playBlock';
      const heading = document.createElement('h2'); heading.textContent = titleText;
      const text = document.createElement('p'); text.className = 'playText'; text.dataset.valueName = field; text.textContent = values.get(field) || '—';
      block.append(heading, text); wrapper.appendChild(block);
    }
    playStage.appendChild(wrapper);
  }

  function normalizeModifier(item) {
    return {
      id: item?.id ?? item?.Id,
      sourceName: item?.sourceName ?? item?.SourceName,
      sourceId: item?.sourceId ?? item?.SourceId,
      target: item?.target ?? item?.Target,
      value: item?.value ?? item?.Value,
      bonusType: item?.bonusType ?? item?.BonusType,
      condition: item?.condition ?? item?.Condition,
      duration: item?.duration ?? item?.Duration,
      isActive: item?.isActive ?? item?.IsActive ?? true
    };
  }

  function loadData(payload) {
    values.clear(); checkedFields.clear(); labels.clear(); numericFields.clear(); autoManaged.clear();
    abilityBaseScores.clear(); proficiencyRanks.clear(); otherBonuses.clear(); overrides.clear(); fieldDefinitions.clear();
    state.structuredEditing = false;
    const initialValues = payload?.textFields && typeof payload.textFields === 'object' ? payload.textFields : {};
    for (const [name, value] of Object.entries(initialValues)) if (typeof value === 'string' && value) values.set(name, value);
    for (const name of Array.isArray(payload?.checkedFields) ? payload.checkedFields : []) checkedFields.add(name);
    for (const [name, label] of Object.entries(payload?.fieldLabels ?? {})) labels.set(name, String(label));
    for (const name of Array.isArray(payload?.numericFields) ? payload.numericFields : []) numericFields.add(name);
    for (const [name, value] of Object.entries(payload?.abilityBaseScores ?? {})) abilityBaseScores.set(name, Number(value));
    for (const [name, value] of Object.entries(payload?.proficiencyRanks ?? {})) proficiencyRanks.set(name, Number(value));
    for (const [name, value] of Object.entries(payload?.otherBonuses ?? {})) otherBonuses.set(name, Number(value));
    for (const [name, value] of Object.entries(payload?.overrides ?? {})) overrides.set(name, String(value));
    modifiers = Array.isArray(payload?.modifiers) ? payload.modifiers.map(normalizeModifier) : [];
    features = Array.isArray(payload?.features) ? payload.features.map(normalizeFeature) : [];
    preparedSpells = Array.isArray(payload?.preparedSpells) ? payload.preparedSpells : [];
    knownSpells = Array.isArray(payload?.knownSpells) ? payload.knownSpells : [];
    resources = Array.isArray(payload?.resources) ? payload.resources : [];
    ruleSet = payload?.ruleSet === '2024' || payload?.ruleSet === 'Homebrew' ? payload.ruleSet : '2014';
    editSections = Array.isArray(payload?.editSections) ? payload.editSections : [];
    for (const section of editSections) for (const field of section.fields ?? []) fieldDefinitions.set(field.name, field);
    if (!editSections.some(section => section.id === state.sectionId)) state.sectionId = editSections[0]?.id ?? 'overview';
    for (const name of textFields.keys()) {
      displayValue(name);
      textFields.get(name).setAttribute('aria-label', labels.get(name) ?? name);
    }
    for (const name of checkFields.keys()) setChecked(name, checkedFields.has(name), false);
    recognizeAutomaticValues();
    renderSectionTabs();
    renderEditSection();
    renderPlayMode();
    setMode(state.mode);
  }

  function handleHostMessage(message) {
    try {
      const parsed = typeof message === 'string' ? JSON.parse(message) : message;
      if (parsed?.type === 'load') loadData(parsed.payload);
      if (parsed?.type === 'featureResults') {
        featureSearchResults = Array.isArray(parsed.results) ? parsed.results : [];
        if (state.sectionId === 'features') renderEditSection();
      }
      if (parsed?.type === 'identityResults') {
        identitySearchResults = Array.isArray(parsed.results) ? parsed.results : [];
        if (state.sectionId === 'overview') renderEditSection();
      }
      if (parsed?.type === 'spellResults') {
        spellSearchResults = Array.isArray(parsed.results) ? parsed.results : [];
        if (state.sectionId === 'spells') renderEditSection();
      }
    } catch (error) {
      send({ type: 'error', message: error instanceof Error ? error.message : String(error) });
    }
  }

  fieldInput.addEventListener('input', updatePdfActiveValue);
  fieldTextarea.addEventListener('input', updatePdfActiveValue);
  valueDown.addEventListener('click', () => stepPdfValue(-1));
  valueUp.addEventListener('click', () => stepPdfValue(1));
  document.getElementById('edit-cancel').addEventListener('click', () => closePdfEditor(false));
  document.getElementById('edit-clear').addEventListener('click', () => {
    if (!state.activeInput) return;
    state.activeInput.value = '';
    updatePdfActiveValue();
    state.activeInput.focus();
  });
  document.getElementById('edit-done').addEventListener('click', () => closePdfEditor(true));
  autoToggle.addEventListener('click', () => {
    state.autoCalc = !state.autoCalc;
    autoToggle.classList.toggle('active', state.autoCalc);
    autoToggle.textContent = state.autoCalc ? 'Auto: sí' : 'Auto: no';
    if (state.autoCalc) recalculateAll();
  });
  for (const button of modeButtons) button.addEventListener('click', () => setMode(button.dataset.mode));
  for (const button of pageButtons) button.addEventListener('click', () => showPage(Number(button.dataset.page)));
  document.getElementById('zoom-out').addEventListener('click', () => setZoom(state.zoom - 0.25));
  document.getElementById('zoom-fit').addEventListener('click', () => setZoom(1));
  document.getElementById('zoom-in').addEventListener('click', () => setZoom(state.zoom + 0.25));
  globalThis.addEventListener('resize', resizePages);
  globalThis.addEventListener('HybridWebViewMessageReceived', event => handleHostMessage(event?.detail?.message));
  globalThis.addEventListener('pagehide', () => send({ type: 'save' }));

  renderPdfPages();
  showPage(state.currentPage);
  setMode('edit');
  announceReady();
})();
