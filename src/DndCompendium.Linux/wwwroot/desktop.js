'use strict';

const sessionKey = 'drivebrew-session';
const token = /^[A-F0-9]{64}$/.test(location.hash.slice(1)) ? location.hash.slice(1) : sessionStorage.getItem(sessionKey);
if (token && location.hash) {
  sessionStorage.setItem(sessionKey, token);
  history.replaceState(null, '', '/');
}

const status = document.querySelector('#status');
const panel = document.querySelector('#panel');
const navigation = document.querySelector('#navigation');
const characterName = document.querySelector('#character-name');
const sections = [
  ['library', 'Biblioteca'], ['search', 'Buscar'], ['spells', 'Spells'], ['exploits', 'Exploits'],
  ['prepared', 'Preparados'], ['favorite', 'Favoritos'], ['characters', 'Personajes'],
  ['resources', 'Recursos'], ['settings', 'Configuración']
];
const catalog = new Map();
const labels = { Spell: 'Spell', Exploit: 'Exploit', Class: 'Clase', Subclass: 'Subclase', ClassFeature: 'Rasgo de clase',
  SubclassFeature: 'Rasgo de subclase', Race: 'Raza', RaceFeature: 'Rasgo de raza', Background: 'Trasfondo',
  Feat: 'Dote', Equipment: 'Equipo', Document: 'Documento', Other: 'Sección' };
const fixedClasses = { Knacks: 'Ranger', Techniques: 'Monk', 'Blood Rites': 'Blood Hunter', Maneuvers: 'Vagabond' };
const exploitClasses = ['Barbarian', 'Fighter', 'Gunslinger', 'Rogue', 'Warden', 'Warlord'];
const encode = encodeURIComponent;
let currentSection = '';
let flushSheet = null;
let heartbeat;

function node(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key.startsWith('aria-') || key === 'role') element.setAttribute(key, value);
    else element[key] = value;
  }
  element.append(...children.flat().filter(child => child !== null && child !== undefined));
  return element;
}
const text = (tag, content, className = '') => node(tag, { className }, content || '');
const actions = (...buttons) => node('div', { className: 'actions' }, buttons);
const fold = value => String(value ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
const distinct = values => [...new Set(values.filter(value => value !== '' && value != null))]
  .sort((a, b) => String(a).localeCompare(String(b), undefined, { numeric: true }));

function notice(message = '', error = false) {
  status.textContent = message;
  status.classList.toggle('error', error);
}

async function api(path, method = 'GET', value) {
  const headers = { 'X-Drivebrew-Session': token || '' };
  if (value !== undefined && !(value instanceof Blob)) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api${path}`, { method, headers,
    body: value === undefined ? undefined : value instanceof Blob ? value : JSON.stringify(value),
    cache: 'no-store'
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.error || (response.status === 403 ? 'Abrí Drivebrew desde su ejecutable para iniciar la sesión.' : 'No se pudo completar la operación.'));
  }
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') || '';
  if (response.headers.has('content-disposition')) return response.blob();
  return contentType.includes('json') ? response.json() : response.text();
}

function button(label, action, primary = false) {
  return node('button', { type: 'button', className: primary ? 'primary' : '', textContent: label,
    onclick: async event => {
      const target = event.currentTarget;
      if (target.disabled) return;
      target.disabled = true;
      try { await action(target); } catch (error) { notice(error.message, true); }
      finally { target.disabled = false; }
    }
  });
}

function select(label, values, update) {
  const control = node('select', { onchange: update });
  const field = node('label', {}, label, control);
  function replace(items, selected = '') {
    control.replaceChildren(...items.map(option => {
      const [value, caption, unavailable] = Array.isArray(option) ? option : [option, option, false];
      return node('option', { value, disabled: !!unavailable }, caption);
    }));
    if ([...control.options].some(option => option.value === selected && !option.disabled)) control.value = selected;
  }
  replace(values);
  return { field, control, replace };
}

function makePane(section, title) {
  currentSection = section;
  const pane = node('div', {}, text('h1', title));
  panel.replaceChildren(pane);
  for (const item of navigation.children) {
    if (item.dataset.section === section) item.setAttribute('aria-current', 'page');
    else item.removeAttribute('aria-current');
  }
  return pane;
}

async function show(section) {
  if (flushSheet) await flushSheet();
  flushSheet = null;
  notice();
  const pane = makePane(section, sections.find(item => item[0] === section)?.[1] || 'Drivebrew');
  switch (section) {
    case 'library': await libraryView(pane); break;
    case 'search': searchView(pane); break;
    case 'spells': case 'exploits': case 'prepared': case 'favorite': await catalogView(pane, section); break;
    case 'characters': await charactersView(pane); break;
    case 'resources': await resourcesView(pane); break;
    case 'settings': settingsView(pane); break;
  }
}

async function refreshCharacter() {
  const profiles = await api('/characters');
  characterName.textContent = profiles.active.name;
  return profiles;
}

async function libraryView(pane) {
  const { documents, recent } = await api('/library');
  const query = node('input', { type: 'search', placeholder: 'Buscar documento', 'aria-label': 'Buscar documento' });
  const cards = node('div', { className: 'cards' });
  const summary = text('p', '', 'muted');
  const download = async (path, message) => {
    notice(message);
    try { await api(path, 'POST'); await show('library'); notice('Biblioteca actualizada.'); }
    catch (error) { await show('library'); throw error; }
  };
  pane.append(node('div', { className: 'toolbar' }, query,
    button('Descargar todo', () => download('/library/download', 'Descargando la biblioteca…'), true),
    button('Buscar actualizaciones', () => download('/library/update', 'Comprobando documentos en Drive…'))));
  const file = node('input', { type: 'file', accept: '.txt,text/plain', 'aria-label': 'Elegir TXT de Homebrewery' });
  pane.append(node('details', {}, node('summary', {}, 'Importar TXT local'),
    node('div', { className: 'toolbar' }, file, button('Importar', async () => {
      if (!file.files[0]) return;
      const item = await api(`/library/import?name=${encode(file.files[0].name)}`, 'POST', file.files[0]);
      await openReader(item.id);
    }))));
  const reading = recent.filter(item => documents.some(doc => doc.id === item.documentId && doc.hasLocalCopy));
  if (reading.length) {
    pane.append(text('p', 'Lecturas recientes', 'meta'),
      actions(...reading.map(item => button(item.title, () => openReader(item.documentId)))));
  }
  pane.append(summary, cards);
  const display = () => {
    const term = fold(query.value.trim());
    const visible = documents.filter(item => fold(`${item.title} ${item.category}`).includes(term));
    summary.textContent = `${visible.length} documentos`;
    cards.replaceChildren(...visible.map(item => node('article', { className: 'card' },
      text('h2', item.title), text('div', item.category, 'meta'), text('p', item.description, 'muted'),
      actions(
        item.hasLocalCopy ? button('Leer', () => openReader(item.id), true) : null,
        !item.hasLocalCopy || item.downloadState === 'UpdateAvailable' ? button(item.hasLocalCopy ? 'Actualizar' : 'Descargar',
          () => download(`/library/${encode(item.id)}/download`, `Descargando ${item.title}…`)) : null
      ))));
  };
  query.oninput = display;
  display();
}

function frameFor(path) {
  const frame = node('iframe', { className: 'viewer', title: 'Contenido offline' });
  const ready = new Promise((resolve, reject) => {
    frame.onload = () => resolve(frame.contentWindow);
    frame.onerror = () => reject(new Error('No se pudo iniciar el visor.'));
  });
  frame.src = path;
  return [frame, ready];
}

async function openReader(id, target) {
  const item = await api(`/reader/${encode(id)}`);
  notice('Abriendo documento offline…');
  const pane = makePane('library', item.title);
  const [frame, ready] = frameFor('/render/index.html');
  let reader;
  let zoom = 1;
  let font = 1;
  let page = 1;
  let pages = 1;
  const pageLabel = text('span', '');
  const readMode = ['book', 'mobile'].includes(sessionStorage.getItem('reader-mode')) ? sessionStorage.getItem('reader-mode') : 'mobile';
  const updatePage = number => { page = number; pageLabel.textContent = `${page} / ${pages}`; };
  const changeMode = mode => {
    reader?.setMode(mode, zoom, font);
    sessionStorage.setItem('reader-mode', mode);
  };
  pane.append(actions(button('Volver', () => show('library')), button('Móvil', () => changeMode('mobile')),
    button('Libro', () => changeMode('book')), button('− Zoom', () => reader?.setZoom(zoom = Math.max(.35, zoom - .1))),
    button('+ Zoom', () => reader?.setZoom(zoom = Math.min(2.5, zoom + .1))),
    button('− Texto', () => reader?.setFontScale(font = Math.max(.75, font - .1))),
    button('+ Texto', () => reader?.setFontScale(font = Math.min(1.8, font + .1))),
    button('Anterior', () => reader?.scrollTo(Math.max(1, page - 1))),
    button('Siguiente', () => reader?.scrollTo(Math.min(pages, page + 1))), pageLabel), frame);
  const win = await ready;
  reader = win.DndReader;
  if (!reader) throw new Error('El lector no pudo iniciarse.');
  win.HybridWebView = { SendRawMessage(raw) {
    const message = JSON.parse(raw);
    if (message.type === 'pageChanged') { pages = message.pageCount || pages; updatePage(message.page); }
    if (message.type === 'link' && /^https?:\/\//i.test(message.href)
      && confirm('¿Abrir el enlace externo en otra pestaña?')) window.open(message.href, '_blank', 'noopener,noreferrer');
  } };
  for (const resource of item.resources) {
    if (!pane.isConnected) return;
    try {
      reader.registerResource(await api(`/reader/${encode(id)}/resource/${resource.index}?sha=${encode(resource.sha)}`));
    } catch { /* Si un recurso cambió, se conserva la lectura del documento. */ }
  }
  if (!pane.isConnected) return;
  const rendered = await reader.renderDocument({ id, title: item.title, content: item.content, mode: readMode });
  pages = rendered?.pageCount || 1;
  updatePage(1);
  if (target) reader.scrollTo(target);
  if (pane.isConnected) notice(item.state === 'Complete' && !rendered?.resourceIssues?.length ? 'Disponible offline.' : 'Lectura offline con algunos recursos visuales pendientes.');
}

function searchView(pane) {
  const query = node('input', { type: 'search', placeholder: 'Nombre, regla o efecto', maxLength: 250, 'aria-label': 'Buscar contenido' });
  const kind = select('Categoría', [['', 'Todas'], ...Object.entries(labels)], () => {});
  const results = node('div', { className: 'cards' });
  const search = async () => {
    if (!query.value.trim()) return;
    notice('Buscando en la copia local…');
    const matches = await api(`/search?q=${encode(query.value)}&kind=${encode(kind.control.value)}`.replace(/&kind=$/, ''));
    results.replaceChildren(...matches.map(({ entry, snippet, isFavorite }) => node('article', { className: 'card' },
      text('h2', entry.name), text('div', [labels[entry.kind], entry.origin, isFavorite ? '★' : ''].filter(Boolean).join(' · '), 'meta'),
      text('p', snippet, 'description'),
      entry.documentId ? button('Abrir sección', () => openReader(entry.documentId, entry.target))
        : entry.referenceId && ['Spell', 'Exploit'].includes(entry.kind)
          ? button('Ver detalle', async () => {
            const type = entry.kind === 'Spell' ? 'spells' : 'exploits';
            const found = (await loadCatalog(type)).find(item => item.id === entry.referenceId);
            if (found) catalogDetail(found, entry.kind);
          }) : null)));
    notice(`${matches.length} resultados.`);
  };
  const submit = button('Buscar', search, true);
  query.onkeydown = event => { if (event.key === 'Enter') submit.click(); };
  pane.append(node('div', { className: 'toolbar' }, query, kind.field, submit), results);
}

async function loadCatalog(type) {
  if (!catalog.has(type)) catalog.set(type, await api(`/${type}`));
  return catalog.get(type);
}

function spellTime(value) {
  const source = fold(value);
  if (/bonus action|accion adicional/.test(source)) return 'Bonus Action';
  if (/reaction|reaccion/.test(source)) return 'Reaction';
  return /action|accion/.test(source) ? 'Action' : 'Otro';
}

function catalogDetail(item, kind) {
  const dialog = node('dialog', {}, text('h1', item.name), text('div', `Nivel ${item.level} · ${item.school || item.type}`, 'meta'));
  const details = kind === 'Spell' ? [
    ['Clase', item.classes.join(', ')], ['Origen', item.origins.join(', ')], ['Casteo', item.castingTime],
    ['Rango', item.range], ['Duración', item.duration], ['Concentración', item.isConcentration ? 'Sí' : 'No'],
    ['Ritual', item.isRitual ? 'Sí' : 'No'], ['Componentes', [
      item.hasVerbalComponent && 'V', item.hasSomaticComponent && 'S', item.hasMaterialComponent && 'M'
    ].filter(Boolean).join(', ')], ['Materiales', item.materialDescription], ['Nivel superior', item.higherLevels]
  ] : [['Clase', item.classes.join(', ')], ['Requisitos', item.prerequisite]];
  dialog.append(text('p', item.effect, 'detail-body description'),
    ...details.filter(([, value]) => value).map(([label, value]) => node('p', {}, node('strong', {}, `${label} · `), value)),
    button('Cerrar', () => dialog.close()));
  dialog.onclose = () => dialog.remove();
  document.body.append(dialog);
  dialog.showModal();
}

async function catalogView(pane, section) {
  const savedView = section === 'favorite' || section === 'prepared';
  let content = section === 'exploits' ? 'Exploit' : 'Spell';
  let data = [];
  let saved = await api('/saved');
  let offset = 0;
  const limit = 40;
  const filters = node('div', { className: 'filters' });
  const cards = node('div', { className: 'cards' });
  const pagination = node('div', { className: 'actions paging' });
  const query = node('input', { type: 'search', placeholder: 'Filtrar por nombre', 'aria-label': 'Filtrar por nombre' });
  const sortChoices = [['', savedView ? 'Manual' : 'A–Z'], ['az', 'A–Z'], ['za', 'Z–A'], ['low', 'Nivel menor a mayor'], ['high', 'Nivel mayor a menor']];
  if (!savedView) sortChoices.splice(1, 1);
  const sorter = select('Orden', sortChoices, apply);
  const selection = select('Contenido', [['Spell', 'Spells'], ['Exploit', 'Exploits']], async () => {
    content = selection.control.value;
    await rebuild();
  });
  const update = button('Descargar o actualizar catálogo', async () => {
    const type = content === 'Spell' ? 'spells' : 'exploits';
    notice('Descargando catálogo de Drive…');
    catalog.set(type, await api(`/${type}/update`, 'POST'));
    await rebuild();
  }, true);
  pane.append(node('div', { className: 'toolbar' }, query, savedView ? selection.field : null, sorter.field, update), filters, cards, pagination);
  query.oninput = apply;
  let controls = {};

  async function rebuild() {
    const current = content;
    const type = current === 'Spell' ? 'spells' : 'exploits';
    try { data = await loadCatalog(type); notice(); }
    catch (error) { data = []; notice(error.message, true); }
    if (current !== content || !pane.isConnected) return;
    controls = {};
    filters.replaceChildren();
    const add = (key, label, choices, callback = apply) => {
      const control = select(label, [['', 'Todos'], ...choices], callback);
      controls[key] = control;
      filters.append(control.field);
      return control;
    };
    if (content === 'Spell') {
      add('level', 'Nivel', distinct(data.map(item => item.level)).map(level => [level, level === 0 ? 'Truco' : `Nivel ${level}`]));
      add('class', 'Clase', distinct(data.flatMap(item => item.classes)));
      add('origin', 'Origen', distinct(data.flatMap(item => item.origins)));
      add('school', 'Escuela', distinct(data.map(item => item.school)));
      add('concentration', 'Concentración', [['yes', 'Sí'], ['no', 'No']]);
      add('ritual', 'Ritual', [['yes', 'Sí'], ['no', 'No']]);
      add('casting', 'Casteo', ['Action', 'Bonus Action', 'Reaction', 'Otro']);
      add('component', 'Componente', [['hasVerbalComponent', 'Verbal'], ['hasSomaticComponent', 'Somático'], ['hasMaterialComponent', 'Material']]);
    } else {
      add('type', 'Tipo', distinct(data.map(item => item.type)), () => { updateExploitFilters(true); apply(); });
      add('class', 'Clase', [], () => { updateExploitFilters(false); apply(); });
      add('level', 'Nivel', [], () => { updateExploitFilters(false); apply(); });
      updateExploitFilters(true);
    }
    apply();
  }

  function updateExploitFilters(reset) {
    const type = controls.type.control.value;
    const options = data.filter(item => item.type === type);
    const fixed = fixedClasses[type];
    const level = reset ? '' : controls.level.control.value;
    const currentClass = reset ? '' : controls.class.control.value;
    const matchingClass = fixed || currentClass;
    controls.class.control.disabled = type !== 'Exploit';
    controls.level.control.disabled = !type;
    controls.class.replace(fixed ? [[fixed, fixed]] : [['', 'Todas'],
      ...(type === 'Exploit' ? exploitClasses : []).map(name => [name, name,
        !options.some(item => (!level || String(item.level) === level) && item.classes.includes(name))])
    ], matchingClass);
    controls.level.replace([['', 'Todos'], ...distinct(options.map(item => item.level)).map(value =>
      [value, `Nivel ${value}`, !options.some(item => item.level === value && (!matchingClass || item.classes.includes(matchingClass)))])
    ], level);
  }

  function apply() { offset = 0; display(); }

  function display() {
    const val = key => controls[key]?.control.value || '';
    const include = (key, values) => !val(key) || values.includes(val(key));
    const bool = (key, value) => !val(key) || value === (val(key) === 'yes');
    const savedOrder = savedView ? saved[section].filter(item => item.contentKind === content) : [];
    const positions = new Map(savedOrder.map((item, index) => [item.itemId, index]));
    const visible = data.filter(item => (!savedView || positions.has(item.id)) &&
      fold(item.name).includes(fold(query.value.trim())) &&
      include('level', [String(item.level)]) && include('class', item.classes) && (
        content === 'Spell'
          ? include('origin', item.origins) && include('school', [item.school]) &&
            bool('concentration', item.isConcentration) && bool('ritual', item.isRitual) &&
            include('casting', [spellTime(item.castingTime)]) && (!val('component') || item[val('component')])
          : include('type', [item.type])
      ));
    visible.sort((a, b) => {
      const name = a.name.localeCompare(b.name);
      switch (sorter.control.value) {
        case 'za': return -name;
        case 'low': return a.level - b.level || name;
        case 'high': return b.level - a.level || name;
        case 'az': return name;
        default: return savedView ? positions.get(a.id) - positions.get(b.id) : name;
      }
    });
    offset = Math.min(offset, Math.max(0, Math.floor((visible.length - 1) / limit) * limit));
    const current = visible.slice(offset, offset + limit);
    cards.replaceChildren(...current.map(item => {
      const isSaved = state => saved[state].some(reference => reference.contentKind === content && reference.itemId === item.id);
      const toggle = async (state, control) => {
        const enabled = !isSaved(state);
        await api(`/saved/${content}/${state}/${encode(item.id)}`, 'PUT', { enabled });
        saved = await api('/saved');
        if (savedView && section === state) display();
        else control.textContent = state === 'favorite'
          ? (enabled ? '★ Favorito' : '☆ Favorito') : (enabled ? '✓ Preparado' : 'Preparar');
      };
      const move = async delta => {
        const index = visible.indexOf(item);
        const other = visible[index + delta];
        if (!other) return;
        const order = [...saved[section]];
        const first = order.findIndex(reference => reference.contentKind === content && reference.itemId === item.id);
        const second = order.findIndex(reference => reference.contentKind === content && reference.itemId === other.id);
        [order[first], order[second]] = [order[second], order[first]];
        await api(`/saved/${section}/order`, 'PUT', order);
        saved = await api('/saved');
        display();
      };
      const manual = savedView && sorter.control.value === '';
      return node('article', { className: 'card' }, text('h2', item.name),
        text('div', `Nivel ${item.level} · ${item.school || item.type}`, 'meta'),
        actions(button('Ver detalle', () => catalogDetail(item, content)),
          button(isSaved('favorite') ? '★ Favorito' : '☆ Favorito', control => toggle('favorite', control)),
          button(isSaved('prepared') ? '✓ Preparado' : 'Preparar', control => toggle('prepared', control)),
          manual && visible.indexOf(item) > 0 ? button('Subir', () => move(-1)) : null,
          manual && visible.indexOf(item) < visible.length - 1 ? button('Bajar', () => move(1)) : null));
    }));
    pagination.replaceChildren(
      offset > 0 ? button('Anterior', () => { offset -= limit; display(); }) : '',
      text('span', visible.length ? `${offset + 1}–${Math.min(offset + limit, visible.length)} de ${visible.length}` : 'Sin resultados.', 'muted'),
      offset + limit < visible.length ? button('Siguiente', () => { offset += limit; display(); }) : ''
    );
  }
  await rebuild();
}

async function charactersView(pane) {
  const profiles = await refreshCharacter();
  const name = node('input', { placeholder: 'Nombre del personaje', maxLength: 60, 'aria-label': 'Nombre del personaje' });
  const create = button('Crear personaje', async () => {
    if (!name.value.trim()) return;
    await api('/characters', 'POST', { name: name.value.trim() });
    await show('characters');
  }, true);
  name.onkeydown = event => { if (event.key === 'Enter') create.click(); };
  pane.append(node('div', { className: 'toolbar' }, name, create), node('div', { className: 'cards' }, profiles.items.map(item =>
    node('article', { className: 'card' }, text('h2', item.name),
      item.id === profiles.active.id ? text('div', 'Personaje activo', 'meta') : null,
      actions(button('Abrir hoja', () => openSheet(item), true),
        item.id !== profiles.active.id ? button('Activar', async () => {
          await api(`/characters/${encode(item.id)}/active`, 'PUT'); await show('characters');
        }) : null,
        button('Cambiar nombre', async () => {
          const value = prompt('Nombre del personaje', item.name)?.trim();
          if (!value) return;
          await api(`/characters/${encode(item.id)}/name`, 'PUT', { name: value }); await show('characters');
        }),
        profiles.items.length > 1 ? button('Eliminar', async () => {
          if (!confirm(`¿Eliminar a ${item.name} de este equipo?`)) return;
          await api(`/characters/${encode(item.id)}`, 'DELETE'); await show('characters');
        }) : null)
    ))));
}

async function openSheet(character) {
  await api(`/characters/${encode(character.id)}/active`, 'PUT');
  await refreshCharacter();
  const path = `/characters/${encode(character.id)}/sheet`;
  const data = await api(path);
  const pane = makePane('characters', character.name);
  const [frame, ready] = frameFor('/render/character-sheet.html');
  pane.append(actions(button('Volver a personajes', () => show('characters')),
    button('Imprimir / guardar PDF', () => frame.contentWindow.print())), frame);
  const win = await ready;
  let pending = Promise.resolve();
  let lastSaved = '';
  let latest = '';
  const persist = snapshot => {
    pending = pending.catch(() => {}).then(async () => {
      if (snapshot === lastSaved) return;
      await api(path, 'PUT', { sheetJson: snapshot });
      lastSaved = snapshot;
      if (pane.isConnected) notice('Cambios guardados.');
    });
    pending.catch(error => notice(error.message, true));
    return pending;
  };
  flushSheet = async () => { if (latest && latest !== lastSaved) await persist(latest); else await pending; };
  win.HybridWebView = { SendRawMessage(raw) {
    const message = JSON.parse(raw);
    if (message.type === 'htmlSheetSave') { latest = message.sheetJson; void persist(latest); }
    if (message.type === 'error') notice(message.message, true);
    if (message.type === 'closeSheet') void show('characters').catch(error => notice(error.message, true));
  } };
  win.dispatchEvent(new win.CustomEvent('HybridWebViewMessageReceived',
    { detail: { message: JSON.stringify({ type: 'load', payload: data }) } }));
}

async function resourcesView(pane) {
  const resources = await api('/resources');
  const resourceName = node('input', { placeholder: 'Nombre del recurso', maxLength: 80, 'aria-label': 'Nombre del recurso' });
  const max = node('input', { type: 'number', min: '1', max: '999', value: '1', 'aria-label': 'Máximo' });
  const recovery = select('Se recupera', [['None', 'Manual'], ['ShortRest', 'Descanso corto'],
    ['LongRest', 'Descanso largo'], ['ShortAndLongRest', 'Cualquier descanso']], () => {});
  pane.append(node('div', { className: 'toolbar' }, resourceName, max, recovery.field,
    button('Agregar', async () => {
      const maximum = Number.parseInt(max.value, 10);
      if (!resourceName.value.trim() || maximum < 1 || !Number.isFinite(maximum)) return;
      await api('/resources', 'PUT', { id: '', name: resourceName.value.trim(), maximum, current: maximum, recovery: recovery.control.value });
      await show('resources');
    }, true)), actions(...[['Short', 'Descanso corto'], ['Long', 'Descanso largo']].map(([kind, caption]) =>
    button(caption, async () => { if (confirm(`¿Aplicar ${caption.toLowerCase()}?`)) {
      await api(`/resources/rest/${kind}`, 'POST'); await show('resources');
    } }))), node('div', { className: 'cards paging' }, resources.map(item => {
    const path = `/resources/${encode(item.id)}`;
    const count = text('div', `${item.current} / ${item.maximum}`, 'meta');
    const adjust = async delta => {
      const next = await api(`${path}/change`, 'POST', { delta });
      count.textContent = `${next.current} / ${next.maximum}`;
    };
    return node('article', { className: 'card' }, text('h2', item.name), count,
      actions(button('Gastar 1', () => adjust(-1)), button('Recuperar 1', () => adjust(1)),
        button('Eliminar', async () => {
          if (!confirm(`¿Eliminar el recurso ${item.name}?`)) return;
          await api(path, 'DELETE'); await show('resources');
        })));
  })));
}

function settingsView(pane) {
  const download = async (path, name) => {
    const url = URL.createObjectURL(await api(path));
    const anchor = node('a', { href: url, download: name });
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
  };
  const file = node('input', { type: 'file', accept: '.json,application/json', 'aria-label': 'Elegir copia de personaje' });
  pane.append(text('h2', 'Transferir personaje'),
    text('p', 'Mové el personaje activo entre Linux, Windows y Android con una copia JSON.'),
    node('div', { className: 'toolbar' },
      button('Exportar personaje', () => download('/characters/export', 'Drivebrew-personaje.json'), true),
      button('Guardar respaldo anterior', () => download('/characters/backup', 'Drivebrew-respaldo-anterior.json'))),
    node('div', { className: 'toolbar' }, file, button('Importar personaje', async () => {
      if (!file.files[0] || !confirm('¿Importar esta copia? Si el personaje ya existe, se guardará un respaldo anterior.')) return;
      await api('/characters/import', 'POST', file.files[0]);
      await refreshCharacter();
      notice('Personaje importado.');
    })), node('div', { className: 'toolbar' },
      node('a', { className: 'button', href: 'https://github.com/BagreJr/drivebrew/releases', target: '_blank', rel: 'noopener noreferrer' }, 'Ver versiones en GitHub'),
      button('Cerrar Drivebrew', async () => {
        clearInterval(heartbeat);
        notice('Drivebrew se cerró. Podés cerrar esta pestaña.');
        await api('/exit', 'POST');
        navigation.replaceChildren();
        pane.replaceChildren();
      })));
}

async function start() {
  if (!token) { notice('Abrí esta versión desde el ejecutable Drivebrew para iniciar la sesión local.', true); return; }
  for (const [id, label] of sections) {
    const item = button(label, () => show(id));
    item.dataset.section = id;
    navigation.append(item);
  }
  await refreshCharacter();
  await show('library');
  heartbeat = setInterval(() => void api('/ping').catch(() => {}), 45_000);
}

void start().catch(error => notice(error.message, true));
