import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

for (const [fixture, source] of [
  ['identity-oidc-clients-v1.json', 'IdentityOidcClientManagementContracts.cs'],
  ['identity-oidc-authorizations-v1.json', 'IdentityOidcAuthorizationManagementContracts.cs'],
  ['identity-oidc-signing-keys-v1.json', 'IdentityOidcSigningKeyManagementContracts.cs']
]) {
  test(`${fixture} 的 Schema 引用完整且字段与服务端一致`, async () => {
    const contract = JSON.parse(await readFile(new URL(`../../contracts/openapi/${fixture}`, import.meta.url), 'utf8'));
    const code = await readFile(new URL(`../../src/Modules/Full.NET.Modules.Identity.Contracts/${source}`, import.meta.url), 'utf8');
    for (const entry of contract.paths) {
      for (const operation of entry.operations) {
        assert.ok(code.includes(`"${operation.permission}"`));
        for (const name of [operation.requestSchema, operation.responseSchema].filter(Boolean)) {
          assert.ok(contract.schemas?.[name], `缺少 Schema：${name}`);
        }
      }
    }
    for (const [name, schema] of Object.entries(contract.schemas)) {
      if (name.endsWith('Page')) {
        assert.deepEqual(schema.properties, ['items', 'page', 'pageSize', 'total']);
        continue;
      }
      const record = code.match(new RegExp(`record ${name}\\(([^;]+)\\);`));
      assert.ok(record, `缺少服务端记录：${name}`);
      const properties = record[1].split(',').map(parameter => {
        const property = parameter.trim().split(/\s+/u).at(-1);
        return property[0].toLowerCase() + property.slice(1);
      });
      assert.deepEqual(schema.properties, properties);
    }
  });
}
