(() => {
  'use strict';
  const api = globalThis.DrivebrewSheet3;
  const h = (...args) => api.h(...args);
  const legacyRender = api.renderCharacter;
  const val = (item, name) => api.model.valueOf(item, name);
  const tabs = [['front', 'Hoja'], ['details', 'Detalles'], ['spells', 'Conjuros']];

  function legacy(section, className = '') {
    const current = api.state.section;
    api.state.section = section;
    const node = legacyRender();
    api.state.section = current;
    node.classList.add('legacyPanel', className || `legacy-${section}`);
    node.querySelector('.sectionHeader')?.remove();
    return node;
  }

  function boundField(label, name, value, options = {}) {
    const input = h(options.multiline ? 'textarea' : 'input', {
      type: options.type || 'text', inputmode: options.type === 'number' ? 'numeric' : null,
      value: value ?? '', placeholder: options.placeholder || ''
    });
    input.addEventListener('change', () => {
      const next = options.type === 'number' && input.value !== '' ? String(Number(input.value) || 0) : input.value;
      api.setField(name, next, options.kind || 'text');
      api.notify();
    });
    return h('label', { class: `sheetField${options.wide ? ' wide' : ''}` }, input, h('span', { text: label }));
  }

  function identity(values) {
    return h('section', { class: 'identitySheet' },
      h('div', { class: 'nameRibbon' }, boundField('Nombre del personaje', 'CharacterName', values.CharacterName, { placeholder: 'Sin nombre' })),
      h('div', { class: 'identityGrid' },
        boundField('Clase y nivel', 'ClassLevel', values.ClassLevel),
        boundField('Trasfondo', 'Background', values.Background),
        boundField('Nombre del jugador', 'PlayerName', values.PlayerName),
        boundField('Especie / raza', 'Race ', values['Race ']),
        boundField('Alineamiento', 'Alignment', values.Alignment),
        boundField('Puntos de experiencia', 'XP', values.XP, { type: 'number' })));
  }

  function rankMark(name, rank, max = 2) {
    return h('button', {
      type: 'button', class: `rankMark rank-${rank}`, 'aria-label': `Cambiar competencia en ${name}`,
      onclick: () => api.setRank(name, (rank + 1) % (max + 1))
    });
  }

  function abilitySheet(values) {
    const rules = globalThis.DRIVEBREW_CHARACTER_RULES;
    const body = h('section', { class: 'abilitySheet panelGrey' });
    const inspired = !['', '0', 'false', 'off'].includes(String(values.Inspiration || '').trim().toLowerCase());
    body.append(h('div', { class: 'proficiencyBar' },
      h('strong', { text: values.ProfBonus || '+0' }),
      h('span', { text: 'Bonificador de competencia' }),
      h('button', { type: 'button', class: `inspirationStar${inspired ? ' active' : ''}`, text: '☆',
        'aria-label': 'Inspiración', onclick: () => { api.setField('Inspiration', inspired ? '' : '1'); api.notify(); } })));
    for (const [ability, definition] of Object.entries(rules.ABILITIES)) {
      const score = api.state.payload.abilityBaseScores?.[ability] ?? api.state.text[ability] ?? 10;
      const scoreInput = h('input', { type: 'number', inputmode: 'numeric', min: 1, max: 30, value: score });
      scoreInput.addEventListener('change', () => { api.setBase(ability, Math.max(1, Math.min(30, Number(scoreInput.value) || 10))); api.notify(); });
      const rows = h('div', { class: 'abilityRows' });
      const save = rules.SAVES.find(([, source]) => source === ability);
      if (save) {
        const rank = Number(api.state.payload.proficiencyRanks?.[save[0]] || 0);
        rows.append(h('div', { class: 'abilityRow saveRow' }, rankMark(save[0], rank, 1), h('b', { text: values[save[0]] || '+0' }), h('span', { text: 'Salvaciones' })));
      }
      for (const [skill, source] of rules.SKILLS.filter(([, source]) => source === ability)) {
        const rank = Number(api.state.payload.proficiencyRanks?.[skill] || 0);
        rows.append(h('div', { class: 'abilityRow' }, rankMark(skill, rank), h('b', { text: values[skill] || '+0' }), h('span', { text: skill.trim() })));
      }
      body.append(h('article', { class: 'abilityClassic' },
        h('div', { class: 'abilityScore' }, scoreInput, h('strong', { text: values[definition.modifier] || '+0' }), h('span', { text: definition.label })), rows));
    }
    return body;
  }

  function statusBar(values) {
    const hp = Number(values.HPCurrent || 0), maximum = Number(values.HPMax || 0);
    const percent = maximum > 0 ? Math.max(0, Math.min(100, hp / maximum * 100)) : 0;
    return h('section', { class: 'sheetHud' },
      h('div', {}, h('span', { text: 'PG' }), h('strong', { text: `${hp} / ${maximum || '—'}` }), h('i', {}, h('b', { style: `width:${percent}%` }))),
      h('div', {}, h('span', { text: 'CA' }), h('strong', { text: values.AC || '—' })),
      h('div', {}, h('span', { text: 'INICIATIVA' }), h('strong', { text: values.Initiative || '+0' })),
      h('div', {}, h('span', { text: 'VELOCIDAD' }), h('strong', { text: values.Speed || '—' })));
  }

  function frontPage() {
    const values = api.calculated();
    const overview = legacy('overview');
    overview.querySelector('.hero')?.closest('.card')?.remove();
    const combat = legacy('combat');
    combat.querySelector(':scope > .statGrid')?.remove();
    return h('main', { class: 'referencePage pageFront' }, statusBar(values), identity(values),
      h('div', { class: 'sheetColumns' },
        h('div', { class: 'sheetColumn abilityColumn' }, abilitySheet(values)),
        h('div', { class: 'sheetColumn playColumn' }, overview, combat)));
  }

  function detailsPage() {
    return h('main', { class: 'referencePage pageDetails' },
      h('header', { class: 'pageHeading' }, h('h2', { text: api.state.text.CharacterName || api.state.payload.characterName || 'Personaje' }), h('span', { text: 'Detalles del personaje' })),
      h('div', { class: 'detailColumns' }, legacy('details'), legacy('features'), legacy('inventory')));
  }

  function spellsPage() {
    const page = legacy('spells');
    page.classList.add('referencePage', 'pageSpells');
    return page;
  }

  api.renderCharacter = () => {
    if (api.state.section === 'front') return frontPage();
    if (api.state.section === 'details') return detailsPage();
    if (api.state.section === 'spells') return spellsPage();
    return legacy(api.state.section, 'advancedPage');
  };

  api.renderNav = () => h('nav', { class: 'sheetTabs', 'aria-label': 'Páginas de la hoja' },
    ...tabs.map(([id, label]) => h('button', { type: 'button', class: api.state.section === id ? 'active' : '', text: label, onclick: () => api.changeSection(id) })));

  api.openSheetTools = () => {
    const body = h('div', { class: 'toolMenu' });
    const items = [['front', 'Hoja principal'], ['abilities', 'Características y skills'], ['combat', 'Combate'], ['features', 'Features y progresión'], ['inventory', 'Inventario'], ['details', 'Historia y Compendium'], ['spells', 'Conjuros']];
    for (const [id, label] of items) body.append(api.button(label, () => { body.closest('.modalBackdrop')?.remove(); api.changeSection(id); }, id === api.state.section ? 'primary' : 'ghost'));
    api.modal('Herramientas de la hoja', body);
  };

  function applyTheme(theme) {
    document.documentElement.dataset.sheetTheme = theme;
    try { globalThis.localStorage.setItem('drivebrew.sheet.theme', theme); } catch { }
  }
  api.toggleSheetTheme = () => applyTheme(document.documentElement.dataset.sheetTheme === 'dark' ? 'light' : 'dark');
  let stored = 'light';
  try { stored = globalThis.localStorage.getItem('drivebrew.sheet.theme') || 'light'; } catch { }
  applyTheme(stored);
})();
