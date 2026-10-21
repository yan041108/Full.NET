import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const uniRequire = createRequire(new URL('../clients/uniapp/package.json', import.meta.url));
const wxRequire = createRequire(uniRequire.resolve('@dcloudio/uni-mp-weixin/package.json'));
const cliRequire = createRequire(uniRequire.resolve('@dcloudio/uni-cli-shared/package.json'));
const e2eRequire = createRequire(new URL('../tests/e2e/admin-real-stack/package.json', import.meta.url));
const containerRequire = createRequire(e2eRequire.resolve('testcontainers/package.json'));
const dockerRequire = createRequire(containerRequire.resolve('dockerode/package.json'));

test('Weixin Jimp named API decodes PNG buffers and local HTTP images', async t => {
  const { Jimp, JimpMime } = wxRequire('jimp');
  const image = new Jimp({ width: 2, height: 3, color: 0xff0000ff });
  const png = await image.getBuffer(JimpMime.png);
  const server = createServer((request, response) => {
    response.writeHead(200, { 'Content-Type': 'image/png' });
    response.end(png);
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(async () => {
    server.closeAllConnections();
    await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  });
  for (const source of [png, `http://127.0.0.1:${server.address().port}/sprite.png`]) {
    const decoded = await Jimp.read(source);
    assert.equal(decoded.bitmap.width, 2);
    assert.equal(decoded.bitmap.height, 3);
    assert.equal(decoded.getPixelColor(0, 0), 0xff0000ff);
  }
});

test('DCloud compiler dependencies retain TypeScript and selector parsing behavior', () => {
  const esbuild = cliRequire('esbuild');
  const result = esbuild.transformSync('export const count: number = 7;', { loader: 'ts', format: 'cjs' });
  const module = { exports: {} };
  new Function('module', 'exports', result.code)(module, module.exports);
  assert.equal(module.exports.count, 7);
  const parser = cliRequire('postcss-selector-parser');
  const selector = '.a'.repeat(2_000);
  let classes = 0;
  const parsed = parser(root => root.walkClasses(() => classes++)).processSync(selector);
  assert.equal(parsed, selector);
  assert.equal(classes, 2_000);
});

test('Dockerode session consumer retains CommonJS UUID v4 generation', () => {
  assert.equal(typeof dockerRequire('./lib/session'), 'function');
  const uuid = dockerRequire('uuid');
  const id = uuid.v4();
  assert.equal(uuid.validate(id), true);
  assert.equal(uuid.version(id), 4);
});

test('uni-app i18n translates through the patched JIT runtime without dynamic code generation', async () => {
  const { default: configure } = await import('../clients/uniapp/vite.config.ts');
  const config = configure({ mode: 'test' });
  const runtime = config.resolve.alias.find(alias => alias.find.test('vue-i18n')).replacement;
  const bundle = cliRequire('esbuild').buildSync({
    entryPoints: [runtime], bundle: true, write: false, platform: 'browser', format: 'cjs',
    define: { ...Object.fromEntries(Object.entries(config.define).map(([key, value]) => [key, JSON.stringify(value)])),
      'process.env.NODE_ENV': '"production"', __INTLIFY_PROD_DEVTOOLS__: 'false' }
  });
  const module = { exports: {} };
  // options 与运行时处于同一 Realm，避免纯对象校验把测试宿主对象当成异域输入。
  const results = runInNewContext(bundle.outputFiles[0].text + `
    (() => {
      const i18n = module.exports.createI18n({ legacy: false, locale: 'zh-CN', fallbackLocale: 'en-US', messages: {
        'zh-CN': { greeting: '你好，{name}' }, 'en-US': { greeting: 'Hello, {name}', fallback: 'Fallback' }
      } });
      const results = [i18n.global.t('greeting', { name: 'Full.NET' }), i18n.global.t('fallback')];
      i18n.global.locale.value = 'en-US';
      results.push(i18n.global.t('greeting', { name: 'Full.NET' }));
      i18n.dispose();
      return JSON.stringify(results);
    })();`, { module, exports: module.exports }, {
    timeout: 2_000, contextCodeGeneration: { strings: false, wasm: false }
  });
  assert.deepEqual(JSON.parse(results), ['你好，Full.NET', 'Fallback', 'Hello, Full.NET']);
});
