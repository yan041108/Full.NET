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

test('Dockerfile exposes api/worker/migrator final targets from one SDK build', async () => {
  assert.equal(await exists('eng/docker/Dockerfile'), true);
  const dockerfile = await read('eng/docker/Dockerfile');
  assert.match(dockerfile, /AS build/);
  assert.match(dockerfile, /AS api/);
  assert.match(dockerfile, /AS worker/);
  assert.match(dockerfile, /AS migrator/);
  assert.match(dockerfile, /USER \$APP_UID/);
  assert.match(dockerfile, /Full\.NET\.Host\.Api\.dll/);
  assert.match(dockerfile, /Full\.NET\.Host\.Worker\.dll/);
  assert.match(dockerfile, /Full\.NET\.Host\.Migrator\.dll/);
  assert.match(dockerfile, /SOURCE_COMMIT/);
  assert.match(dockerfile, /mcr\.microsoft\.com\/dotnet\/sdk:\$\{DOTNET_VERSION\}/);
  assert.match(dockerfile, /mcr\.microsoft\.com\/dotnet\/aspnet:\$\{DOTNET_VERSION\}/);
});

test('container image contract runner exists for post-build verification', async () => {
  assert.equal(
    await exists('scripts/testing/run-container-image-contracts.mjs'),
    true
  );
  const script = await read(
    'scripts/testing/run-container-image-contracts.mjs'
  );
  assert.match(script, /Config\.User/);
  assert.match(script, /dotnet --info|--info/);
  assert.match(script, /fullnet-api:/);
  assert.match(script, /fullnet-worker:/);
  assert.match(script, /fullnet-migrator:/);
});
