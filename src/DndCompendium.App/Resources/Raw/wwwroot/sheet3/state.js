(() => {
  'use strict';
  const api = globalThis.DrivebrewSheet3 = globalThis.DrivebrewSheet3 || {};
  const listeners = new Set();
  let saveTimer = 0;
  const state = api.state = {
    loaded: false, section: 'overview', payload: {}, text: {}, checked: new Set(),
    identityResults: [], featureResults: [], spellResults: [], saving: false,
    identityPickerKind: 'Race', identityPickerQuery: ''
  };

  function bridge() {
    const candidate = globalThis.HybridWebView;
    return candidate && typeof candidate.SendRawMessage === 'function' ? candidate : null;
  }
  api.send = message => {
    const current = bridge();
    if (!current) return false;
    try {
      current.SendRawMessage(JSON.stringify(message));
      return true;
    } catch {
      return false;
    }
  };
  api.uuid = () => globalThis.crypto?.randomUUID?.().replaceAll('-', '') || `${Date.now()}${Math.random()}`;
  api.subscribe = listener => { listeners.add(listener); return () => listeners.delete(listener); };
  api.notify = () => listeners.forEach(listener => listener(state));
  api.scheduleSave = (delay = 600) => {
    state.saving = true;
    globalThis.clearTimeout(saveTimer);
    saveTimer = globalThis.setTimeout(() => { api.send({ type: 'save' }); state.saving = false; api.notify(); }, delay);
  };
  api.setField = (name, value, kind = 'text') => {
    const normalized = value == null ? '' : String(value);
    if (normalized) state.text[name] = normalized; else delete state.text[name];
    api.send({ type: 'change', name, kind, value: normalized, checked: false });
    api.scheduleSave();
  };
  api.setBase = (name, value) => {
    state.payload.abilityBaseScores ||= {};
    state.payload.abilityBaseScores[name] = Number(value);
    api.setField(name, String(value), 'base');
  };
  api.setRank = (name, rank) => {
    state.payload.proficiencyRanks ||= {};
    state.payload.proficiencyRanks[name] = Number(rank);
    api.send({ type: 'change', name, kind: 'proficiency', value: String(rank), checked: rank > 0 });
    api.scheduleSave(); api.notify();
  };
  api.setBonus = (name, value) => {
    state.payload.otherBonuses ||= {};
    const parsed = Number(value) || 0;
    if (parsed) state.payload.otherBonuses[name] = parsed; else delete state.payload.otherBonuses[name];
    api.send({ type: 'change', name, kind: 'bonus', value: parsed ? String(parsed) : '', checked: false });
    api.scheduleSave(); api.notify();
  };
  api.setChecked = (name, checked) => {
    checked ? state.checked.add(name) : state.checked.delete(name);
    api.send({ type: 'change', name, kind: 'checkbox', value: '', checked }); api.scheduleSave();
  };
  api.changeSection = section => { state.section = section; api.notify(); globalThis.scrollTo({ top: 0, behavior: 'smooth' }); };
  api.openReference = item => item?.documentId && api.send({ type: 'openFeature', documentId: item.documentId, target: item.target });
  api.toast = message => {
    const region = document.getElementById('toast-region');
    const toast = document.createElement('div'); toast.className = 'toast'; toast.textContent = message;
    region.appendChild(toast); globalThis.setTimeout(() => toast.remove(), 2800);
  };

  function normalizePayload(payload) {
    state.payload = payload || {};
    state.text = { ...(payload?.textFields || {}) };
    state.checked = new Set(payload?.checkedFields || []);
    for (const key of ['selections','effects','features','classLevels','conditions','attacks','inventory','preparedSpells','knownSpells','resources','modifiers'])
      state.payload[key] = Array.isArray(state.payload[key]) ? state.payload[key] : [];
    state.loaded = true;
  }
  api.receive = message => {
    try {
      const parsed = typeof message === 'string' ? JSON.parse(message) : message;
      if (parsed?.type === 'load') {
        normalizePayload(parsed.payload);
        api.send({ type: 'loaded', editor: 'character-sheet-3', version: 4 });
      }
      else if (parsed?.type === 'identityResults') state.identityResults = parsed.results || [];
      else if (parsed?.type === 'featureResults') state.featureResults = parsed.results || [];
      else if (parsed?.type === 'spellResults') state.spellResults = parsed.results || [];
      else if (parsed?.type === 'levelUpPreview') api.showLevelUpPreview?.(parsed);
      api.notify();
    } catch (error) { api.send({ type: 'error', message: String(error?.message || error) }); }
  };
  globalThis.addEventListener('HybridWebViewMessageReceived', event => api.receive(event?.detail?.message));
  globalThis.addEventListener('pagehide', () => api.send({ type: 'save' }));
})();
