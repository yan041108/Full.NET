import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

test('Invoke-FullNetRelease.ps1 enforces migrator -> worker -> api order', async () => {
  assert.equal(await exists('eng/deploy/Invoke-FullNetRelease.ps1'), true);
  const script = await read('eng/deploy/Invoke-FullNetRelease.ps1');
  const migrator = script.indexOf('fullnet-migrator');
  const worker = script.indexOf('fullnet-worker');
  const api = script.indexOf('fullnet-api');
  assert.ok(migrator >= 0 && worker > migrator && api > worker);
  assert.match(script, /Stage 1\/3/);
  assert.match(script, /Stage 2\/3/);
  assert.match(script, /Stage 3\/3/);
  assert.match(script, /Expand\/Contract/);
  assert.match(script, /Capacity-not-verified/);
  assert.match(script, /\$ErrorActionPreference\s*=\s*'Stop'/);
});

test('production role overlays enable exactly one role each', async () => {
  const api = await read('deploy/helm/fullnet/ci/values-role-api.yaml');
  const worker = await read('deploy/helm/fullnet/ci/values-role-worker.yaml');
  const migrator = await read(
    'deploy/helm/fullnet/ci/values-role-migrator.yaml'
  );
  assert.match(api, /api:\s*true/);
  assert.match(api, /worker:\s*false/);
  assert.match(worker, /worker:\s*true/);
  assert.match(worker, /api:\s*false/);
  assert.match(migrator, /migrator:\s*true/);
  assert.match(migrator, /api:\s*false/);
});

test('Helm wires the notification delivery pause into the Worker Pod environment', async () => {
  const values = await read('deploy/helm/fullnet/values.yaml');
  const schema = JSON.parse(await read('deploy/helm/fullnet/values.schema.json'));
  const worker = await read('deploy/helm/fullnet/templates/worker-deployment.yaml');
  const api = await read('deploy/helm/fullnet/templates/api-deployment.yaml');
  assert.match(values, /^worker:\r?\n(?:^  [^\r\n]+\r?\n)*?^  notificationDeliveryEnabled: true$/m);
  assert.equal(schema.properties.worker.properties.notificationDeliveryEnabled.type, 'boolean');
  assert.match(worker, /- name: Notifications__Worker__Enabled\s+value: {{ \.Values\.worker\.notificationDeliveryEnabled \| quote }}/);
  assert.doesNotMatch(api, /Notifications__Worker__Enabled/);
});
