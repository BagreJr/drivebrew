(() => {
  'use strict';

  const ABILITIES = Object.freeze({
    STR: { score: 'STR', modifier: 'STRmod', label: 'Fuerza' },
    DEX: { score: 'DEX', modifier: 'DEXmod ', label: 'Destreza' },
    CON: { score: 'CON', modifier: 'CONmod', label: 'Constitución' },
    INT: { score: 'INT', modifier: 'INTmod', label: 'Inteligencia' },
    WIS: { score: 'WIS', modifier: 'WISmod', label: 'Sabiduría' },
    CHA: { score: 'CHA', modifier: 'CHamod', label: 'Carisma' }
  });

  const SAVES = Object.freeze([
    ['ST Strength', 'STR', 'Check Box 11'], ['ST Dexterity', 'DEX', 'Check Box 18'],
    ['ST Constitution', 'CON', 'Check Box 19'], ['ST Intelligence', 'INT', 'Check Box 20'],
    ['ST Wisdom', 'WIS', 'Check Box 21'], ['ST Charisma', 'CHA', 'Check Box 22']
  ]);

  const SKILLS = Object.freeze([
    ['Acrobatics', 'DEX', 'Check Box 23'], ['Animal', 'WIS', 'Check Box 24'],
    ['Arcana', 'INT', 'Check Box 25'], ['Athletics', 'STR', 'Check Box 26'],
    ['Deception ', 'CHA', 'Check Box 27'], ['History ', 'INT', 'Check Box 28'],
    ['Insight', 'WIS', 'Check Box 29'], ['Intimidation', 'CHA', 'Check Box 30'],
    ['Investigation ', 'INT', 'Check Box 31'], ['Medicine', 'WIS', 'Check Box 32'],
    ['Nature', 'INT', 'Check Box 33'], ['Perception ', 'WIS', 'Check Box 34'],
    ['Performance', 'CHA', 'Check Box 35'], ['Persuasion', 'CHA', 'Check Box 36'],
    ['Religion', 'INT', 'Check Box 37'], ['SleightofHand', 'DEX', 'Check Box 38'],
    ['Stealth ', 'DEX', 'Check Box 39'], ['Survival', 'WIS', 'Check Box 40']
  ]);

  function parseNumber(value) {
    const match = String(value ?? '').replace(',', '.').match(/[+-]?\d+(?:\.\d+)?/);
    return match ? Number(match[0]) : null;
  }

  function signed(value) {
    return value >= 0 ? `+${value}` : String(value);
  }

  function levelFromClass(value) {
    const matches = String(value ?? '').match(/\b(?:[1-9]|1\d|20)\b/g) ?? [];
    if (!matches.length) return null;
    const total = matches.map(Number).reduce((sum, number) => sum + number, 0);
    return Math.max(1, Math.min(20, total));
  }

  function resolveSpellAbility(value) {
    const normalized = String(value ?? '').trim().toUpperCase();
    const names = {
      STRENGTH: 'STR', FUERZA: 'STR', DEXTERITY: 'DEX', DESTREZA: 'DEX',
      CONSTITUTION: 'CON', 'CONSTITUCIÓN': 'CON', CONSTITUCION: 'CON',
      INTELLIGENCE: 'INT', INTELIGENCIA: 'INT', WISDOM: 'WIS', SABIDURIA: 'WIS', SABIDURÍA: 'WIS',
      CHARISMA: 'CHA', CARISMA: 'CHA'
    };
    return ABILITIES[normalized] ? normalized : names[normalized] ?? null;
  }

  function inferSpellAbility(value) {
    const text = String(value ?? '').toLowerCase();
    const matches = new Set();
    if (/artificer|wizard|blood hunter/.test(text)) matches.add('INT');
    if (/cleric|druid|ranger/.test(text)) matches.add('WIS');
    if (/bard|paladin|sorcerer|warlock/.test(text)) matches.add('CHA');
    return matches.size === 1 ? Array.from(matches)[0] : null;
  }

  function calculate(sourceValues, sourceChecked) {
    const values = sourceValues instanceof Map ? sourceValues : new Map(Object.entries(sourceValues ?? {}));
    const checked = sourceChecked instanceof Set ? sourceChecked : new Set(sourceChecked ?? []);
    const updates = new Map();
    const numberFor = name => parseNumber(updates.has(name) ? updates.get(name) : values.get(name));
    const modifierFor = ability => {
      const score = numberFor(ABILITIES[ability].score);
      return score === null ? null : Math.floor((score - 10) / 2);
    };

    for (const ability of Object.keys(ABILITIES)) {
      const modifier = modifierFor(ability);
      if (modifier !== null) updates.set(ABILITIES[ability].modifier, signed(modifier));
    }

    const level = levelFromClass(values.get('ClassLevel'));
    if (level !== null) updates.set('ProfBonus', signed(2 + Math.floor((level - 1) / 4)));
    const proficiency = numberFor('ProfBonus') ?? 0;

    for (const [field, ability, checkbox] of [...SAVES, ...SKILLS]) {
      const modifier = modifierFor(ability);
      if (modifier !== null) updates.set(field, signed(modifier + (checked.has(checkbox) ? proficiency : 0)));
    }

    const dexterity = modifierFor('DEX');
    if (dexterity !== null) updates.set('Initiative', signed(dexterity));
    const perception = numberFor('Perception ');
    if (perception !== null) updates.set('Passive', String(10 + perception));

    let spellAbility = resolveSpellAbility(values.get('SpellcastingAbility 2'));
    if (!spellAbility) {
      const inferred = inferSpellAbility(values.get('ClassLevel'));
      if (inferred) {
        updates.set('SpellcastingAbility 2', inferred);
        spellAbility = inferred;
      }
    }
    if (spellAbility) {
      const modifier = modifierFor(spellAbility);
      if (modifier !== null) {
        updates.set('SpellSaveDC  2', String(8 + proficiency + modifier));
        updates.set('SpellAtkBonus 2', signed(proficiency + modifier));
      }
    }
    return updates;
  }

  const api = Object.freeze({ ABILITIES, SAVES, SKILLS, parseNumber, signed, levelFromClass, calculate });
  globalThis.DRIVEBREW_CHARACTER_RULES = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})();
