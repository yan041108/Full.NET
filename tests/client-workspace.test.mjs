import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [workspace, rootPackage, tokens, notices, localeCatalog, glossary, uniappPackage] = await Promise.all([
  readFile('pnpm-workspace.yaml', 'utf8'),
  readFile('package.json', 'utf8'),
  readFile('packages/design-tokens/src/tokens.css', 'utf8'),
  readFile('THIRD-PARTY-NOTICES', 'utf8'),
  readFile('localization/locales.json', 'utf8'),
  readFile('localization/glossary.json', 'utf8'),
  readFile('clients/uniapp/package.json', 'utf8')
]);

assert.match(workspace, /packages\/\*/);
assert.match(workspace, /ui\/\*/);
assert.match(workspace, /clients\/\*/);

const packageDefinition = JSON.parse(rootPackage);
assert.equal(packageDefinition.private, true);
assert.match(packageDefinition.packageManager, /^pnpm@10\./);
assert.equal(packageDefinition.engines.node, '>=24 <25');
assert.equal(
  packageDefinition.scripts['test:localization'],
  'node --test tests/localization-contract.test.mjs'
);

const uniappDefinition = JSON.parse(uniappPackage);
assert.equal(uniappDefinition.name, '@fullnet/uniapp');
assert.equal(uniappDefinition.private, true);
assert.equal(uniappDefinition.type, 'module');
assert.equal(uniappDefinition.scripts.test, 'vitest run');
assert.equal(uniappDefinition.scripts.typecheck, 'vue-tsc --noEmit -p tsconfig.json');
assert.equal(uniappDefinition.scripts['build:h5'], 'uni build -p h5');
assert.equal(uniappDefinition.scripts['build:mp-weixin'], 'uni build -p mp-weixin');
assert.equal(uniappDefinition.scripts['build:mp-alipay'], 'uni build -p mp-alipay');

const catalog = JSON.parse(localeCatalog);
assert.equal(catalog.defaultLocale, 'zh-CN');
assert.deepEqual(
  catalog.supportedLocales.map(locale => locale.tag),
  ['zh-CN', 'en-US']
);
assert.ok(
  catalog.supportedLocales.every(locale =>
    ['dotnet', 'web', 'uniapp', 'flutter'].every(
      platform => locale.platformMappings[platform]
    )
  )
);

const glossaryDefinition = JSON.parse(glossary);
assert.ok(Array.isArray(glossaryDefinition.terms));
assert.ok(glossaryDefinition.terms.length > 0);

assert.match(tokens, /--fullnet-color-accent:/);
assert.match(tokens, /--fullnet-shell-sidebar-width:/);
assert.match(tokens, /--fullnet-font-sans:/);

assert.match(notices, /Layui/i);
assert.match(notices, /MIT/i);
assert.match(notices, /uni-app\/DCloud/i);
assert.match(notices, /Apache-2\.0/i);
assert.match(notices, /Vue I18n/i);
