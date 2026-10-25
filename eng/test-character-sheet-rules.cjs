'use strict';

const assert = require('node:assert/strict');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const rules = require(path.join(root, 'src/DndCompendium.App/Resources/Raw/wwwroot/sheet/character-sheet-rules.js'));
require(path.join(root, 'src/DndCompendium.App/Resources/Raw/wwwroot/sheet/layout.js'));

const values = new Map(Object.entries({
  STR: '15', DEX: '14', CON: '13', INT: '16', WIS: '12', CHA: '8',
  ClassLevel: 'Wizard 5', 'SpellcastingAbility 2': 'INT'
}));
const checked = new Set(['Check Box 11', 'Check Box 34']);
const calculated = rules.calculate(values, checked);
const expected = {
  STRmod: '+2', 'DEXmod ': '+2', CONmod: '+1', INTmod: '+3', WISmod: '+1', CHamod: '-1',
  ProfBonus: '+3', 'ST Strength': '+5', Acrobatics: '+2', 'Perception ': '+4', Passive: '14',
  Initiative: '+2', 'SpellSaveDC  2': '14', 'SpellAtkBonus 2': '+6'
};
for (const [name, value] of Object.entries(expected)) assert.equal(calculated.get(name), value, name);

assert.equal(rules.calculate(new Map([['ClassLevel', 'Fighter 3 / Rogue 2']]), new Set()).get('ProfBonus'), '+3');
assert.equal(rules.calculate(new Map([['ClassLevel', 'Paladin 9']]), new Set()).get('SpellcastingAbility 2'), 'CHA');
assert.equal(rules.calculate(new Map([['ClassLevel', 'Cleric 17']]), new Set()).get('ProfBonus'), '+6');

const structured = rules.calculate(values, checked, {
  abilityBaseScores: new Map([['STR', 15], ['DEX', 14], ['CON', 13], ['INT', 16], ['WIS', 12], ['CHA', 8]]),
  proficiencyRanks: new Map([['Perception ', 2], ['ST Strength', 1]]),
  otherBonuses: new Map([['Perception ', 1], ['Initiative', 2], ['SpellSaveDC  2', 1]]),
  modifiers: [{ target: 'STR', value: 1, sourceName: 'Feat', isActive: true }]
});
assert.equal(structured.get('STR'), '16', 'structured ability total');
assert.equal(structured.get('STRmod'), '+3', 'modifier from structured total');
assert.equal(structured.get('ST Strength'), '+6', 'proficient saving throw');
assert.equal(structured.get('Perception '), '+8', 'expertise plus other bonus');
assert.equal(structured.get('Passive'), '18', 'passive perception from structured result');
assert.equal(structured.get('Initiative'), '+4', 'initiative other bonus');
assert.equal(structured.get('SpellSaveDC  2'), '15', 'spell DC other bonus');

const overridden = rules.calculate(values, checked, {
  proficiencyRanks: { Athletics: 2 },
  otherBonuses: { Athletics: -1 },
  overrides: { Athletics: '+99' }
});
assert.equal(overridden.get('Athletics'), '+99', 'manual override wins without deleting inputs');
assert.deepEqual(rules.explain('Athletics', values, checked, {
  proficiencyRanks: { Athletics: 2 }, otherBonuses: { Athletics: -1 }
}).map(part => part.label), ['Fuerza', 'Expertise', 'Otros']);

const layout = globalThis.DRIVEBREW_SHEET_LAYOUT;
assert.equal(layout.pages.length, 3);
assert.equal(layout.fields.length, 332);
const names = new Set(layout.fields.map(field => field.name));
for (const name of [...Object.values(rules.ABILITIES).flatMap(item => [item.score, item.modifier]), ...expectedKeys()]) {
  assert.ok(names.has(name), `Missing PDF field: ${name}`);
}

function expectedKeys() {
  return ['ClassLevel', 'ProfBonus', 'Initiative', 'Passive', 'SpellcastingAbility 2', 'SpellSaveDC  2', 'SpellAtkBonus 2'];
}

console.log('Character Sheet rules and 332-field layout verified.');
