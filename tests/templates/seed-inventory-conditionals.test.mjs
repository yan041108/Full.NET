import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { buildSeedInventory } from '../../scripts/templates/framework-manifest-utils.mjs';

const modulePath = 'src/Modules/Full.NET.Modules.Identity/IdentityModule.cs';
const seedPath = 'src/Modules/Full.NET.Modules.Identity/Seeding/HostAdministratorSeedContributor.cs';
const registration = 'services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>();';
function inventory(body) {
  const sources = { [modulePath]: `public void AddMigrationServices() {\n${body}\n}`,
    [seedPath]: 'internal sealed class HostAdministratorSeedContributor {}' };
  return buildSeedInventory({ [modulePath]: 'a'.repeat(64), [seedPath]: 'b'.repeat(64) }, path => sources[path], { minimal: ['Identity'] });
}

for (const body of [
  `#if FULLNET_AOT_COMPILE\n RegisterMaterializers();\n#endif\n${registration}`,
  `#if A\n First();\n#elif B\n Second();\n#else\n Third();\n#endif\n${registration}`,
  `#if A\n#if B\n First();\n#endif\n#else\n Second();\n#endif\n${registration}`,
  `#if A\n${registration}\n#elif B\n${registration}\n#else\n${registration}\n#endif`
]) test('seed inventory accepts identical registrations across branches: ' + body.split('\n')[0] + '/' + body.length, () => {
  assert.deepEqual(inventory(body).presets.minimal, [seedPath]);
});

for (const [name, body] of [
  ['missing else', `#if A\n${registration}\n#endif`],
  ['empty else', `#if A\n${registration}\n#else\n Other();\n#endif`],
  ['missing elif branch', `#if A\n${registration}\n#elif B\n Other();\n#else\n${registration}\n#endif`],
  ['nested conditional seed', `#if A\n#if B\n${registration}\n#endif\n#else\n${registration}\n#endif`],
  ['conditional generic argument', `services.AddScoped<IDataSeedContributor,\n#if A\n HostAdministratorSeedContributor\n#else\n MissingSeedContributor\n#endif\n>();`],
  ['unterminated conditional', `#if A\n${registration}`],
  ['orphan else', `#else\n${registration}`],
  ['branch budget', Array.from({ length: 7 }, () => '#if A\n First();\n#endif').join('\n') + '\n' + registration]
]) test('seed inventory rejects ' + name, () => assert.throws(() => inventory(body), /Conditional seed registration/));

test('seed inventory accepts actual Identity AOT materializers with unconditional seeds', () => {
  const source = readFileSync(new URL('../../' + modulePath, import.meta.url), 'utf8');
  const names = [...source.matchAll(/\bIDataSeedContributor\s*,\s*([A-Za-z0-9]+SeedContributor)\s*>/gu)].map(match => match[1]);
  assert.ok(names.length >= 4);
  const files = { [modulePath]: source };
  for (const name of names) {
    const path = `src/Modules/Full.NET.Modules.Identity/Seeding/${name}.cs`;
    files[path] = readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
  }
  const result = buildSeedInventory(Object.fromEntries(Object.keys(files).map(path => [path, 'a'.repeat(64)])), path => files[path], { minimal: ['Identity'] });
  assert.deepEqual(result.contributors.map(value => value.name).sort(), [...names].sort());
});
