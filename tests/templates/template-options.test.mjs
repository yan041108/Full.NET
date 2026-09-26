import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';

const TEMPLATE_JSON = resolve('templates/fullnet-app/.template.config/template.json');
const REQUIRED_SYMBOLS = ['owner-key', 'database', 'preset', 'http-port'];

test('template.json exposes required symbols', () => {
  const template = JSON.parse(readFileSync(TEMPLATE_JSON, 'utf8'));
  assert.equal(template.shortName, 'fullnet-app');
  assert.equal(template.sourceName, 'FullNetAppNameToken');

  for (const symbol of REQUIRED_SYMBOLS) {
    assert.ok(template.symbols?.[symbol], 'missing symbol: ' + symbol);
  }

  const databaseChoices = template.symbols.database.choices.map((choice) => choice.choice);
  assert.deepEqual(databaseChoices, ['sqlserver', 'mysql']);

  const presetChoices = template.symbols.preset.choices.map((choice) => choice.choice);
  assert.deepEqual(presetChoices, ['minimal', 'platform', 'saas', 'enterprise']);
});
