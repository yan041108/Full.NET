import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const generatedNames = ['guards.generated.ts', 'index.generated.ts', 'models.generated.ts', 'operations.generated.ts'];
const vueSources = new Map([
  ['clients/vue/products-page.generated.ts', 'ui/admin/src/views/products-page.generated.ts'],
  ['clients/vue/products.generated.ts', 'ui/admin/src/views/products.generated.ts'],
  ['clients/vue/productsView.vue', 'ui/admin/src/views/CatalogProductsView.vue'],
]);
const clientRoot = 'packages/client-contracts/src/application-generated/catalog-product';
const operationExport = `export {
  catalogCreateProduct,
  catalogDeleteProduct,
  catalogGetProduct,
  catalogListProducts,
  catalogUpdateProduct
} from './application-generated/catalog-product/operations.generated.js';\n`;
const modelExport = `export type {
  CreateProductRequest,
  DeleteProductRequest,
  PagedResultOfProductResponse,
  ProductResponse,
  UpdateProductRequest
} from './application-generated/catalog-product/models.generated.js';\n`;

// 独立应用拥有客户端包与管理端骨架；业务生成文件只写入应用专属路径。
export function prepareApplicationCrudVue(appRoot) {
  const clientIndex = join(appRoot, 'packages/client-contracts/src/index.ts');
  const moduleTarget = join(appRoot, 'verification/CrudGeneration/module-target.json');
  const vueTarget = join(appRoot, 'verification/CrudGeneration/vue-target.json');
  const sources = [
    ...generatedNames.map((name) => [`verification/ClientGeneration/generated/${name}`, `${clientRoot}/${name}`]),
    ...vueSources,
  ];
  for (const path of [clientIndex, moduleTarget, join(appRoot, 'ui/admin/src/router/index.ts'),
    ...sources.map(([source]) => join(appRoot, source))]) {
    assert.equal(existsSync(path), true, 'application Vue source missing: ' + path);
  }
  for (const path of [vueTarget, ...sources.map(([, destination]) => join(appRoot, destination))]) {
    assert.equal(existsSync(path), false, 'Vue adoption requires an unused application path: ' + path);
  }
  const originalIndex = readFileSync(clientIndex, 'utf8');
  assert.equal(originalIndex.includes('catalogCreateProduct'), false, 'application client product operation already exported');
  const target = JSON.parse(readFileSync(moduleTarget, 'utf8'));
  assert.equal(Object.hasOwn(target, 'clientRoute'), false, 'module target already contains a client route');
  for (const [, destination] of sources) mkdirSync(join(appRoot, destination, '..'), { recursive: true });
  for (const [source, destination] of sources) copyFileSync(join(appRoot, source), join(appRoot, destination));
  writeFileSync(clientIndex, originalIndex + (originalIndex.endsWith('\n') ? '\n' : '\n\n') + operationExport + '\n' + modelExport);
  writeFileSync(vueTarget, JSON.stringify({ ...target, clientRoute: {
    routePath: '/catalog/products', vueRouteName: 'catalog-products',
    vueComponentPath: 'ui/admin/src/views/CatalogProductsView.vue',
  } }, null, 2));
  return { clientFiles: 4, vueFiles: 3, routePath: '/catalog/products' };
}

export function verifyApplicationCrudVue(appRoot, {
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-vue'), run = spawnSync,
} = {}) {
  const adopted = prepareApplicationCrudVue(appRoot);
  mkdirSync(reportDirectory, { recursive: true });
  const router = join(appRoot, 'ui/admin/src/router/index.ts');
  const index = join(appRoot, 'packages/client-contracts/src/index.ts');
  const target = join(appRoot, 'verification/CrudGeneration/vue-target.json');
  const protectedPaths = [index, target,
    ...generatedNames.map((name) => join(appRoot, 'verification/ClientGeneration/generated', name)),
    ...generatedNames.map((name) => join(appRoot, clientRoot, name)),
    ...[...vueSources.values()].map((path) => join(appRoot, path))];
  const snapshot = () => new Map(protectedPaths.map((path) => [path, readFileSync(path)]));
  const installed = snapshot();
  const execute = (stage, command, args, options = {}) => {
    const result = run(command, args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000,
      windowsHide: true, shell: process.platform === 'win32' && command === 'pnpm', ...options });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ status: result.status,
      error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, stage + ' failed: ' + (result.stderr ?? result.stdout ?? ''));
    return result.stdout ?? '';
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const routeArgs = ['exec', cli, 'apply-client-route-integration', '--schema',
    join(appRoot, 'verification/CrudGeneration/schema.json'), '--repository', appRoot, '--target', target];
  assert.match(execute('route', 'dotnet', routeArgs), /Update ui\/admin\/src\/router\/index\.ts/u);
  const routed = readFileSync(router);
  assert.match(routed.toString('utf8'), /name: 'catalog-products'/u);
  assert.match(routed.toString('utf8'), /path: '\/catalog\/products'/u);
  assert.match(execute('route-repeat', 'dotnet', routeArgs), /Unchanged ui\/admin\/src\/router\/index\.ts/u);
  assert.deepEqual(readFileSync(router), routed, 'repeat route integration changed the router');
  execute('install', 'pnpm', ['install', '--frozen-lockfile']);
  execute('build', 'pnpm', ['--filter', '@fullnet/admin', 'build']);
  assert.deepEqual(snapshot(), installed, 'Vue build changed adopted application sources');
  assert.deepEqual(readFileSync(router), routed, 'Vue build changed the route');
  const result = { ...adopted, routeIntegrated: true, repeatUnchanged: true, vueBuilt: true, sourcesPreserved: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
