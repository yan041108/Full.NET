import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

type JsonObject = Readonly<Record<string, unknown>>;

async function readJson(path: string): Promise<JsonObject> {
  return JSON.parse(
    await readFile(new URL(path, import.meta.url), 'utf8')
  ) as JsonObject;
}

function collectKeys(value: unknown, path = ''): string[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return [path];
  }

  return Object.entries(value).flatMap(([key, child]) =>
    collectKeys(child, path ? `${path}.${key}` : key)
  );
}

describe('uni-app application configuration', () => {
  it('uses the locale settings page as the sole startup page without a native tab bar', async () => {
    const pages = await readJson('../src/pages.json');

    expect(pages.locale).toBe('zh-Hans');
    expect(pages.pages).toEqual([{
      path: 'pages/settings/locale',
      style: {
        navigationBarTitleText: '%settings.title%'
      }
    }]);
    expect(pages).not.toHaveProperty('tabBar');
  });

  it('uses localized application metadata with the same default platform locale', async () => {
    const manifest = await readJson('../src/manifest.json');

    expect(manifest.name).toBe('%app.name%');
    expect(manifest.locale).toBe('zh-Hans');
  });

  it.each([
    [
      'application',
      '../src/locale/zh-Hans.json',
      '../src/locale/en.json',
      ['app.name', 'settings.title']
    ],
    [
      'platform',
      '../src/locale/uni-app.zh-Hans.json',
      '../src/locale/uni-app.en.json',
      ['uni.picker.cancel', 'uni.picker.done', 'uni.showActionSheet.cancel']
    ]
  ])('keeps %s locale resource keys complete and aligned', async (
    _scope,
    zhPath,
    enPath,
    requiredKeys
  ) => {
    const [zhMessages, enMessages] = await Promise.all([
      readJson(zhPath),
      readJson(enPath)
    ]);
    const zhKeys = collectKeys(zhMessages).sort();
    const enKeys = collectKeys(enMessages).sort();

    expect(zhKeys).toEqual(enKeys);
    expect(zhKeys).toEqual(expect.arrayContaining(requiredKeys));
  });

  it('does not embed remote URLs in application configuration or platform resources', async () => {
    const paths = [
      '../src/pages.json',
      '../src/manifest.json',
      '../src/locale/zh-Hans.json',
      '../src/locale/en.json',
      '../src/locale/uni-app.zh-Hans.json',
      '../src/locale/uni-app.en.json'
    ];
    const contents = await Promise.all(paths.map(path =>
      readFile(new URL(path, import.meta.url), 'utf8')
    ));

    expect(contents.join('\n')).not.toMatch(/https?:\/\//i);
  });

  it('provides a local H5 document entry with the required application mount point', async () => {
    const document = await readFile(new URL('../index.html', import.meta.url), 'utf8');

    expect(document).toMatch(/<html\s+lang=["']zh-CN["']/i);
    expect(document).toMatch(/<meta[^>]+name=["']viewport["'][^>]+viewport-fit=cover/i);
    expect(document).toMatch(/<div\s+id=["']app["']/i);
    expect(document).toMatch(/<script\s+type=["']module["']\s+src=["']\/src\/main\.ts["']><\/script>/i);
    expect(document).not.toMatch(/(?:src|href)=["']https?:\/\//i);
  });
});
