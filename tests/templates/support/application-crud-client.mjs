import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { isMainThread, parentPort, Worker, workerData } from 'node:worker_threads';

const operationIds = ['catalogListProducts', 'catalogCreateProduct', 'catalogGetProduct', 'catalogUpdateProduct', 'catalogDeleteProduct'];
const fileNames = ['guards.generated.ts', 'index.generated.ts', 'models.generated.ts', 'operations.generated.ts'];

export async function verifyApplicationCrudClientProductRead(appRoot, baseUrl, { tenantAccessToken, expectedProduct, logPath }) {
  assert.ok(typeof tenantAccessToken === 'string' && tenantAccessToken.trim(), 'valid tenant credential required');
  for (const key of ['id', 'tenantId', 'name', 'version']) {
    assert.ok(typeof expectedProduct?.[key] === 'string' && expectedProduct[key], 'expected product field missing: ' + key);
  }
  return await runRuntimeWorker({ kind: 'client-runtime', appRoot, baseUrl, logPath,
    hostAccessToken: tenantAccessToken, productRead: expectedProduct });
}

export async function verifyApplicationCrudClientTenantRead(appRoot, baseUrl, { logPath, tenantAccessToken }) {
  assert.ok(typeof tenantAccessToken === 'string' && tenantAccessToken.trim(), 'valid tenant credential required');
  return await runRuntimeWorker({ kind: 'client-runtime', appRoot, baseUrl, logPath, hostAccessToken: tenantAccessToken, tenantRead: true });
}

// 匿名不注入凭据，Host 凭据只在内存中传递；拒绝证据不证明允许业务操作或页面行为。
export async function verifyApplicationCrudClientRuntime(appRoot, baseUrl, { logPath, hostAccessToken }) {
  assert.ok(hostAccessToken === undefined || (typeof hostAccessToken === 'string' && hostAccessToken.trim()), 'valid Host credential required');
  return await runRuntimeWorker({ kind: 'client-runtime', appRoot, baseUrl, logPath, hostAccessToken });
}

async function runRuntimeWorker(data) {
  // Worker 隔离 fetch 观测，避免修改并行验收或宿主线程的全局网络实现。
  return await new Promise((resolve, reject) => {
    const worker = new Worker(new URL(import.meta.url), { workerData: data });
    let result;
    worker.on('message', (message) => { result = message; });
    worker.on('error', reject);
    worker.on('exit', (code) => {
      if (code !== 0 || !result) reject(new Error('generated client runtime worker failed'));
      else if (result.error) reject(new Error(result.error));
      else resolve(result.value);
    });
  });
}

async function runClientRuntime(appRoot, baseUrl, logPath, hostAccessToken, tenantRead = false, productRead) {
  const emittedRoot = join(appRoot, 'verification/ClientGeneration/emitted');
  const host = hostAccessToken !== undefined;
  const expectedStatus = host ? 403 : 401;
  const expectedCode = host ? 'authorization.permission_denied' : 'identity.session_not_active';
  const redact = (text) => host ? text.replaceAll(hostAccessToken, '[REDACTED]') : text;
  const evidence = { completed: false, subject: productRead ? 'tenant-product-reader' : tenantRead ? 'tenant-reader' : host ? 'host-admin' : 'anonymous', responses: [] };
  const originalFetch = globalThis.fetch;
  const transports = [];
  globalThis.fetch = async (...args) => {
    const response = await originalFetch(...args);
    transports.push(response.status);
    return response;
  };
  try {
    const { createHttpClient } = await import(pathToFileURL(join(emittedRoot, 'packages/client-contracts/src/http.js')).href);
    const operations = await import(pathToFileURL(join(emittedRoot, 'verification/ClientGeneration/generated/operations.generated.js')).href);
    const http = createHttpClient(baseUrl);
    if (host) http.configureAuthentication({ getAccessToken: () => hostAccessToken, refresh: async () => false });
    if (productRead) {
      const entry = { operationId: 'catalogGetProduct' };
      evidence.responses.push(entry);
      let value;
      try { value = await operations.catalogGetProduct(http, { productId: productRead.id }, AbortSignal.timeout(15_000), { retryUnauthorized: false }); }
      finally { entry.httpStatus = transports[0]; }
      assert.equal(transports.length, 1, 'generated product read unexpected transport count');
      assert.equal(entry.httpStatus, 200, 'generated product read HTTP status mismatch');
      assert.equal(value.id, productRead.id, 'generated product id mismatch');
      assert.equal(value.tenantId, productRead.tenantId, 'generated product tenant mismatch');
      assert.equal(value.name, productRead.name, 'generated product name mismatch');
      assert.equal(value.version, productRead.version, 'generated product version mismatch');
      evidence.completed = true;
      return { requests: 1, productRead: 1 };
    }
    if (tenantRead) {
      const entry = { operationId: 'catalogListProducts' };
      evidence.responses.push(entry);
      let page;
      try { page = await operations.catalogListProducts(http, { page: 1, pageSize: 5 }, AbortSignal.timeout(15_000), { retryUnauthorized: false }); }
      finally { entry.httpStatus = transports[0]; }
      assert.equal(transports.length, 1, 'generated tenant read unexpected transport count');
      assert.equal(entry.httpStatus, 200, 'generated tenant read HTTP status mismatch');
      assert.equal(page.page, 1, 'generated tenant read page mismatch');
      assert.equal(page.pageSize, 5, 'generated tenant read page size mismatch');
      assert.ok(Array.isArray(page.items), 'generated tenant read items mismatch');
      entry.items = page.items.length;
      evidence.completed = true;
      return { requests: 1, readSucceeded: 1 };
    }
    const productId = '01900000-0000-7000-8000-000000000001';
    const parameters = [{ page: 1, pageSize: 5 }, { body: { name: 'Denied application product' } },
      { productId }, { productId, body: { name: 'Denied update', version: '1' } }, { productId, body: { version: '1' } }];
    for (let index = 0; index < operationIds.length; index += 1) {
      const operationId = operationIds[index];
      const entry = { operationId };
      evidence.responses.push(entry);
      let rejected = false;
      try {
        await operations[operationId](http, parameters[index], AbortSignal.timeout(15_000), { retryUnauthorized: false });
      } catch (problem) {
        rejected = true;
        assert.equal(transports.length, index + 1, 'generated client unexpected transport count');
        entry.httpStatus = transports[index];
        entry.status = problem?.status;
        entry.code = problem?.code;
        assert.equal(entry.httpStatus, expectedStatus, 'generated client HTTP status mismatch');
        assert.equal(entry.status, expectedStatus, 'generated client decoded status mismatch');
        assert.equal(entry.code, expectedCode, 'generated client authorization machine code mismatch');
      }
      assert.equal(rejected, true, 'generated client unexpectedly allowed denied operation');
    }
    evidence.completed = true;
    return host ? { requests: 5, hostDenied: 5 } : { requests: 5, anonymousDenied: 5 };
  } finally {
    globalThis.fetch = originalFetch;
    writeFileSync(logPath, redact(JSON.stringify(evidence, null, 2)));
  }
}

if (!isMainThread && workerData?.kind === 'client-runtime') {
  try { parentPort.postMessage({ value: await runClientRuntime(workerData.appRoot, workerData.baseUrl, workerData.logPath, workerData.hostAccessToken, workerData.tenantRead, workerData.productRead) }); }
  catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    parentPort.postMessage({ error: workerData.hostAccessToken === undefined ? message : message.replaceAll(workerData.hostAccessToken, '[REDACTED]') });
  }
}

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
  const emittedRoot = join(verificationRoot, 'emitted');
  writeFileSync(configPath, JSON.stringify({ compilerOptions: { strict: true, target: 'ES2022',
    rootDir: appRoot, outDir: emittedRoot,
    module: 'ESNext', moduleResolution: 'Bundler', lib: ['ES2022', 'DOM'],
    paths: { '@fullnet/client-contracts': [join(sharedRoot, 'http.ts')] } }, include: ['generated/*.ts'] }));
  execute('compile', [compilerPath, '-p', configPath]);
  writeFileSync(join(emittedRoot, 'package.json'), JSON.stringify({ type: 'module' }));
  execute('check', [...args, '--check']);
  for (const [name, bytes] of preserved) assert.ok(readFileSync(name).equals(bytes), 'client verification changed input: ' + name);
  const result = { operations: 5, generatedFiles: 4, compiled: true, zeroDrift: true, inputsUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
