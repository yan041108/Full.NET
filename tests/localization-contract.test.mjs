import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const localeCatalogUrl = new URL('../localization/locales.json', import.meta.url);
const localeSchemaUrl = new URL(
  '../localization/schemas/locale-catalog.schema.json',
  import.meta.url
);
const glossaryUrl = new URL('../localization/glossary.json', import.meta.url);
const adminLocaleSourceUrl = new URL(
  '../packages/admin-i18n/src/locale.ts',
  import.meta.url
);

async function readJson(url) {
  return JSON.parse(await readFile(url, 'utf8'));
}

function assertNoFallbackCycles(supportedLocales) {
  const localeByTag = new Map(
    supportedLocales.map(locale => [locale.tag, locale])
  );

  function visit(tag, path) {
    assert.ok(!path.has(tag), `语言回退存在循环：${[...path, tag].join(' -> ')}`);

    const locale = localeByTag.get(tag);
    if (!locale) {
      return;
    }

    const nextPath = new Set(path).add(tag);
    for (const fallback of locale.fallbacks) {
      visit(fallback, nextPath);
    }
  }

  for (const locale of supportedLocales) {
    visit(locale.tag, new Set());
  }
}

function assertGlossaryMatchesLocales(glossary, supportedLocales) {
  const expectedLocaleTags = supportedLocales.map(locale => locale.tag).sort();
  const termsByName = new Map();

  for (const entry of glossary.terms) {
    assert.ok(
      !termsByName.has(entry.term),
      `术语表存在重复 term：${entry.term}`
    );
    assert.deepEqual(
      Object.keys(entry.display).sort(),
      expectedLocaleTags,
      `${entry.term} 的显示语言必须与规范语言完全一致`
    );
    termsByName.set(entry.term, entry);
  }

  return termsByName;
}

test('语言清单定义首期规范语言和完整平台映射', async () => {
  const catalog = await readJson(localeCatalogUrl);

  assert.equal(catalog.schemaVersion, 1);
  assert.equal(catalog.defaultLocale, 'zh-CN');
  assert.deepEqual(
    catalog.supportedLocales.map(item => item.tag),
    ['zh-CN', 'en-US']
  );
  assert.ok(
    catalog.supportedLocales.some(item => item.tag === catalog.defaultLocale),
    '默认语言必须存在于 supportedLocales'
  );

  const tags = catalog.supportedLocales.map(item => item.tag);
  assert.equal(new Set(tags).size, tags.length, '规范语言标签必须唯一');

  for (const item of catalog.supportedLocales) {
    assert.deepEqual(Intl.getCanonicalLocales(item.tag), [item.tag]);
    assert.ok(['ltr', 'rtl'].includes(item.direction));
    assert.ok(Array.isArray(item.fallbacks));
    assert.ok(item.platformMappings.dotnet);
    assert.ok(item.platformMappings.web);
    assert.ok(item.platformMappings.uniapp);
    assert.ok(item.platformMappings.flutter);
  }

  assertNoFallbackCycles(catalog.supportedLocales);
});

test('管理端支持语言与仓库规范语言保持一致', async () => {
  const [catalog, localeSource] = await Promise.all([
    readJson(localeCatalogUrl),
    readFile(adminLocaleSourceUrl, 'utf8')
  ]);
  const declaration = localeSource.match(
    /supportedLocales\s*=\s*\[([^\]]+)]\s*as\s+const/
  );

  assert.ok(declaration, '未找到 admin-i18n 的 supportedLocales 声明');
  const adminLocales = [...declaration[1].matchAll(/['"]([^'"]+)['"]/g)].map(
    match => match[1]
  );
  assert.deepEqual(
    adminLocales,
    catalog.supportedLocales.map(item => item.tag)
  );
});

test('语言清单 Schema 固定治理契约的必填字段', async () => {
  const schema = await readJson(localeSchemaUrl);

  assert.equal(schema.$schema, 'https://json-schema.org/draft/2020-12/schema');
  assert.deepEqual(schema.required, [
    'schemaVersion',
    'defaultLocale',
    'supportedLocales'
  ]);
  assert.deepEqual(schema.$defs.locale.required, [
    'tag',
    'fallbacks',
    'direction',
    'platformMappings'
  ]);
  assert.deepEqual(schema.$defs.platformMappings.required, [
    'dotnet',
    'web',
    'uniapp',
    'flutter'
  ]);
});

test('术语表固定不可翻译术语的中英文显示', async () => {
  const [catalog, glossary] = await Promise.all([
    readJson(localeCatalogUrl),
    readJson(glossaryUrl)
  ]);
  const requiredTerms = [
    'Full.NET',
    'Host',
    'Tenant',
    'TraceId',
    'Access Token',
    'Refresh Token',
    'ProblemDetails',
    'SignalR',
    'Agent',
    'Tool',
    'MCP'
  ];
  const termsByName = assertGlossaryMatchesLocales(
    glossary,
    catalog.supportedLocales
  );

  assert.equal(glossary.schemaVersion, 1);
  for (const term of requiredTerms) {
    const entry = termsByName.get(term);
    assert.ok(entry, `术语表缺少 ${term}`);
    assert.equal(entry.translate, false, `${term} 必须声明 translate=false`);
    assert.ok(entry.display['zh-CN']);
    assert.ok(entry.display['en-US']);
  }
});

test('术语表拒绝缺少规范语言的显示值', () => {
  const glossary = {
    terms: [
      {
        term: 'Tenant',
        display: { 'zh-CN': '租户' }
      }
    ]
  };

  assert.throws(
    () =>
      assertGlossaryMatchesLocales(glossary, [
        { tag: 'zh-CN' },
        { tag: 'en-US' }
      ]),
    /Tenant 的显示语言必须与规范语言完全一致/
  );
});

test('术语表拒绝规范清单以外的显示语言', () => {
  const glossary = {
    terms: [
      {
        term: 'Tenant',
        display: {
          'zh-CN': '租户',
          'en-US': 'Tenant',
          'fr-FR': 'Locataire'
        }
      }
    ]
  };

  assert.throws(
    () =>
      assertGlossaryMatchesLocales(glossary, [
        { tag: 'zh-CN' },
        { tag: 'en-US' }
      ]),
    /Tenant 的显示语言必须与规范语言完全一致/
  );
});

test('术语表拒绝重复术语', () => {
  const glossary = {
    terms: [
      {
        term: 'Tenant',
        display: { 'zh-CN': '租户', 'en-US': 'Tenant' }
      },
      {
        term: 'Tenant',
        display: { 'zh-CN': '租户', 'en-US': 'Tenant' }
      }
    ]
  };

  assert.throws(
    () =>
      assertGlossaryMatchesLocales(glossary, [
        { tag: 'zh-CN' },
        { tag: 'en-US' }
      ]),
    /术语表存在重复 term：Tenant/
  );
});
