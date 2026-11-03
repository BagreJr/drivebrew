(() => {
  'use strict';
  const api = globalThis.DrivebrewSheet3;
  const rules = globalThis.DRIVEBREW_CHARACTER_RULES;
  const nameOf = item => item?.name ?? item?.Name ?? '';
  const valueOf = (item, name) => item?.[name[0].toLowerCase() + name.slice(1)] ?? item?.[name];

  api.totalLevel = payload => {
    const classes = payload?.classLevels || [];
    if (classes.length) return classes.reduce((sum, item) => sum + (Number(valueOf(item, 'Level')) || 0), 0);
    return rules.levelFromClass(payload?.textFields?.ClassLevel) || 1;
  };
  api.calculated = () => {
    const state = api.state;
    const values = new Map(Object.entries(state.text));
    const updates = rules.calculate(values, state.checked, {
      abilityBaseScores: state.payload.abilityBaseScores,
      proficiencyRanks: state.payload.proficiencyRanks,
      otherBonuses: state.payload.otherBonuses,
      overrides: state.payload.overrides,
      modifiers: (state.payload.modifiers || []).map(item => ({
        target: valueOf(item, 'Target'), value: valueOf(item, 'Value'), isActive: valueOf(item, 'IsActive')
      }))
    });
    return Object.assign({}, state.text, Object.fromEntries(updates));
  };
  api.breakdown = name => rules.explain(name, new Map(Object.entries(api.state.text)), api.state.checked, {
    abilityBaseScores: api.state.payload.abilityBaseScores,
    proficiencyRanks: api.state.payload.proficiencyRanks,
    otherBonuses: api.state.payload.otherBonuses,
    overrides: api.state.payload.overrides,
    modifiers: (api.state.payload.modifiers || []).map(item => ({
      target: valueOf(item, 'Target'), value: valueOf(item, 'Value'), isActive: valueOf(item, 'IsActive')
    }))
  });
  api.model = { nameOf, valueOf };
})();
