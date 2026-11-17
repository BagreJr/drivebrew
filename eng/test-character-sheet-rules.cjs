'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const sheetPath = path.join(root, 'src/DndCompendium.App/Resources/Raw/wwwroot/character-sheet.html');
const html = fs.readFileSync(sheetPath, 'utf8');

assert.match(html, /<html lang="es">/, 'The supplied Spanish character sheet must be installed');
assert.match(html, /_framework\/hybridwebview\.js/, 'The MAUI bridge must load');
assert.match(html, /const NativeBridge = \(\(\) =>/, 'Native persistence bridge is missing');
assert.match(html, /htmlSheetSave/, 'The full sheet snapshot must be saved by the app');
assert.match(html, /replaceSingle\(raw/, 'Native profiles must own exactly one HTML sheet');
assert.doesNotMatch(html, /https:\/\/(?:fonts\.googleapis|fonts\.gstatic)\.com/, 'The editor must not need online fonts');

for (const page of ['front', 'details', 'spells']) {
  assert.match(html, new RegExp(`data-page=["']${page}["']`), `Missing ${page} sheet page`);
}
for (const field of [
  'info.name', 'info.class', 'info.level',
  'hp.max', 'hp.current', 'features', 'equipment', 'details.age'
]) {
  assert.match(html, new RegExp(`data-bind=["']${field.replace('.', '\\.')}`), `Missing editable field ${field}`);
}
assert.ok(html.includes('data-bind="abilities.${a.k}"'), 'Missing editable ability fields');
for (const feature of [
  'Descanso corto', 'Descanso largo', 'Ataques y lanzamiento de conjuros',
  'Rasgos y atributos', 'Historia del personaje', 'Conjuros'
]) assert.ok(html.includes(feature), `Missing supplied feature: ${feature}`);

const scripts = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/gi)]
  .map(match => match[1]).filter(source => source.trim());
assert.equal(scripts.length, 1, 'Expected one inline application script');
assert.doesNotThrow(() => new Function(scripts[0]), 'The embedded application JavaScript must compile');

console.log('Supplied three-page editable character sheet and native persistence bridge verified.');
