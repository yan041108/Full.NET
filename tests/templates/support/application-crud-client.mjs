import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const operationIds = ['catalogListProducts', 'catalogCreateProduct', 'catalogGetProduct', 'catalogUpdateProduct', 'catalogDeleteProduct'];
const fileNames = ['guards.generated.ts', 'index.generated.ts', 'models.generated.ts', 'operations.generated.ts'];

// 只使用应用工具和业务契约；编译器由验收环境提供，不改写共享客户端基线。
export function verifyApplicationCrudClient(appRoot, {
  reportDirectory,
  run = spawnSync,
  compilerPath = fileURLToPath(new URL('../../../packages/client-contracts/node_modules/typescript/bin/tsc', import.meta.url)),
} = {}) {
  const verificationRoot = join(appRoot, 'verification/ClientGeneration');
  assert.equal(existsSync(verificationRoot), false, 'requires unused application path: ' + verificationRoot);
  const inputPath = join(appRoot, 'contracts/openapi/products.generated.openapi.json');
  const sharedRoot = join(appRoot, 'packages/client-contracts/src');
  const preservedPaths = [inputPath, join(sharedRoot, 'http.ts'), ...fileNames.map((name) => join(sharedRoot, 'generated', name))];
  const preserved = new Map(preservedPaths.map((name) => [name, readFileSync(name)]));
  const document = JSON.parse(preserved.get(inputPath).toString('utf8'));
  const actualIds = Object.values(document.paths).flatMap((item) => Object.entries(item)
    .filter(([method]) => ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'].includes(method))
    .map(([, operation]) => operation.operationId));
  assert.deepEqual(actualIds.sort(), [...operationIds].sort(), 'business operation set changed');
  mkdirSync(verificationRoot, { recursive: true });
  mkdirSync(reportDirectory, { recursive: true });
  const manifestPath = join(verificationRoot, 'manifest.json');
  const outputDirectory = join(verificationRoot, 'generated');
  writeFileSync(manifestPath, JSON.stringify({ publicOperationIds: [] }));
  const execute = (stage, args) => {
    const result = run(process.execPath, args, { cwd: verificationRoot, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ status: result.status, error: result.error?.message,
      stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stdout ?? ''}\n${result.stderr ?? ''}`);
  };
  const args = [join(appRoot, '.fullnet-tools/openapi/generate-fullnet-client.mjs'), '--input', inputPath,
    '--manifest', manifestPath, '--output', outputDirectory, '--http-module', '@fullnet/client-contracts'];
  execute('generate', args);
  assert.deepEqual(readdirSync(outputDirectory).sort(), [...fileNames].sort(), 'unexpected generated files');
  const operations = readFileSync(join(outputDirectory, 'operations.generated.ts'), 'utf8');
  for (const id of operationIds) assert.ok(operations.includes('export async function ' + id + '('), 'missing operation: ' + id);
  const configPath = join(verificationRoot, 'tsconfig.json');
  writeFileSync(configPath, JSON.stringify({ compilerOptions: { strict: true, noEmit: true, target: 'ES2022',
    module: 'ESNext', moduleResolution: 'Bundler', lib: ['ES2022', 'DOM'],
    paths: { '@fullnet/client-contracts': [join(sharedRoot, 'http.ts')] } }, include: ['generated/*.ts'] }));
  execute('compile', [compilerPath, '-p', configPath]);
  execute('check', [...args, '--check']);
  for (const [name, bytes] of preserved) assert.ok(readFileSync(name).equals(bytes), 'client verification changed input: ' + name);
  const result = { operations: 5, generatedFiles: 4, compiled: true, zeroDrift: true, inputsUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
