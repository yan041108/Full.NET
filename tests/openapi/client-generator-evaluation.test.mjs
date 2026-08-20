import assert from 'node:assert/strict';
import { access, mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const snapshotPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'fullnet-client-v1.openapi.json'
);
const expectedFileNames = [
  'guards.generated.ts',
  'index.generated.ts',
  'models.generated.ts',
  'operations.generated.ts'
];

test('生成器只产生 Full.NET models、guards、operations 与公开入口', async () => {
  const { generateFullNetClient } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-generator-'));
  try {
    const firstOutput = path.join(temporaryRoot, 'first');
    const secondOutput = path.join(temporaryRoot, 'second');
    await generateFullNetClient({ inputPath: snapshotPath, outputDirectory: firstOutput });
    await generateFullNetClient({ inputPath: snapshotPath, outputDirectory: secondOutput });

    assert.deepEqual((await readdir(firstOutput)).sort(), expectedFileNames);
    assert.deepEqual(await readGeneratedFiles(firstOutput), await readGeneratedFiles(secondOutput));

    const files = await readGeneratedFiles(firstOutput);
    const combined = Object.values(files).join('\n');
    assert.doesNotMatch(
      combined,
      /\b(?:Configuration|BaseAPI|fetch|axios|localStorage)\b/u
    );
    assert.equal(Object.keys(files).every(fileName => fileName.endsWith('.generated.ts')), true);
    assert.match(files['models.generated.ts'], /export interface HostUserResponse/u);
    assert.match(files['guards.generated.ts'], /export function readHostUserResponse/u);
    assert.match(files['guards.generated.ts'], /client\.invalid_host_user_response/u);
    assert.match(files['operations.generated.ts'], /http\.request<unknown>/u);
    assert.match(files['operations.generated.ts'], /return readHostUserResponse\(value\)/u);
    assert.match(files['operations.generated.ts'], /http\.requestBlob\(/u);
    assert.match(files['operations.generated.ts'], /http\.request<void>\(/u);
    assert.match(
      files['operations.generated.ts'],
      /body\.append\('file', parameters\.file\)/u
    );
    assert.doesNotMatch(
      files['operations.generated.ts'],
      /body\.append\('file', String\(parameters\.file\)\)/u
    );
    assert.doesNotMatch(files['operations.generated.ts'], /requestBlob[\s\S]*readStream/u);
    assert.match(
      files['index.generated.ts'],
      /^export \* from '\.\/(?:models|guards|operations)\.generated\.js';$/mu
    );
    assert.doesNotMatch(
      files['index.generated.ts'],
      /(?:Configuration|BaseAPI|runtime|http\.js)/u
    );
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('候选停止后只保留零外部依赖的仓库内生成实现', async () => {
  const packageJson = JSON.parse(await readFile(path.join(repositoryRoot, 'package.json'), 'utf8'));
  const evaluation = JSON.parse(await readFile(path.join(
    repositoryRoot,
    'eng',
    'openapi-generator',
    'openapi-generator-config.json'
  ), 'utf8'));

  assert.equal(
    packageJson.scripts['openapi:client:generate'],
    'node scripts/openapi/generate-fullnet-client.mjs'
  );
  assert.equal(
    packageJson.devDependencies?.['@openapitools/openapi-generator-cli'],
    undefined
  );
  assert.equal(evaluation.implementation, 'repository-node');
  assert.equal(evaluation.candidate.status, 'rejected');
  assert.deepEqual(evaluation.selected.dependencies, []);
  await assert.rejects(
    access(path.join(repositoryRoot, 'openapitools.json')),
    error => error?.code === 'ENOENT'
  );
});

test('三个 Vue 试点 API 只保留生成 Operation 薄适配层', async () => {
  const adapters = await Promise.all([
    'users.ts',
    'host-files.ts',
    'config-entries.ts'
  ].map(fileName => readFile(path.join(
    repositoryRoot,
    'ui',
    'admin',
    'src',
    'api',
    fileName
  ), 'utf8')));
  const combined = adapters.join('\n');

  assert.doesNotMatch(combined, /\/api\/v1\//u);
  assert.doesNotMatch(combined, /\brequest(?:Blob)?\s*(?:<|\()/u);
  assert.match(combined, /identityListHostUsers\(http,/u);
  assert.match(combined, /filesUploadHostFile\(http,/u);
  assert.match(combined, /filesDownloadHostFileContent\(http,/u);
  assert.match(combined, /settingsDeleteHostConfigEntry\(/u);
  assert.match(combined, /settingsBatchDeleteHostConfigEntries\(/u);
});

test('CRUD Golden OpenAPI 通过就绪门禁并进入同一客户端生成入口', async () => {
  const fixturePath = path.join(
    repositoryRoot,
    'tests',
    'Full.NET.UnitTests',
    'CodeGeneration',
    'Fixtures',
    'CatalogProduct',
    'contracts',
    'openapi',
    'products.generated.openapi.json'
  );
  const document = JSON.parse(await readFile(fixturePath, 'utf8'));
  const { validateClientGenerationReadiness } = await import(
    '../../scripts/openapi/validate-client-generation-readiness.mjs'
  );
  const { generateFullNetClient } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  assert.deepEqual(validateClientGenerationReadiness(document), []);

  const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-crud-openapi-'));
  try {
    await generateFullNetClient({
      inputPath: fixturePath,
      outputDirectory: temporaryRoot
    });
    const files = await readGeneratedFiles(temporaryRoot);
    assert.match(files['models.generated.ts'], /export interface ProductResponse/u);
    assert.match(files['guards.generated.ts'], /export function readProductResponse/u);
    assert.match(files['operations.generated.ts'], /export async function catalogListProducts/u);
    assert.match(files['operations.generated.ts'], /export async function catalogCreateProduct/u);
    assert.match(files['operations.generated.ts'], /export async function catalogUpdateProduct/u);
    assert.match(files['operations.generated.ts'], /export async function catalogDisableProduct/u);
    assert.doesNotMatch(files['operations.generated.ts'], /request<ProductResponse>/u);
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

async function readGeneratedFiles(directory) {
  const entries = await Promise.all(expectedFileNames.map(async fileName => [
    fileName,
    await readFile(path.join(directory, fileName), 'utf8')
  ]));
  return Object.fromEntries(entries);
}
