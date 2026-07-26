import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

import enUS from '../src/i18n/messages.en-US.json';
import zhCN from '../src/i18n/messages.zh-CN.json';

async function readSource(path: string): Promise<string> {
  return readFile(new URL(path, import.meta.url), 'utf8').catch(() => '');
}

describe('uni-ui component smoke contract', () => {
  it('registers a kebab-case smoke route with the approved component set', async () => {
    const [pagesSource, smokePage] = await Promise.all([
      readSource('../src/pages.json'),
      readSource('../src/pages/ui/component-smoke.vue')
    ]);
    const pages = JSON.parse(pagesSource) as {
      readonly pages: readonly { readonly path: string }[];
    };

    expect(pages.pages.map(page => page.path)).toContain('pages/ui/component-smoke');
    for (const component of [
      'uni-section',
      'uni-list',
      'uni-list-item',
      'uni-forms',
      'uni-easyinput',
      'uni-popup'
    ]) {
      expect(smokePage).toContain(`<${component}`);
    }
    expect(smokePage).toContain('import.meta.env.DEV');
  });

  it('provides complete Chinese and English smoke-page messages', () => {
    const zhSmoke = (zhCN as { readonly ui?: { readonly smoke?: unknown } }).ui?.smoke;
    const enSmoke = (enUS as { readonly ui?: { readonly smoke?: unknown } }).ui?.smoke;

    expect(zhSmoke).toEqual(expect.objectContaining({
      title: expect.any(String),
      inputLabel: expect.any(String),
      required: expect.any(String),
      openPopup: expect.any(String)
    }));
    expect(enSmoke).toEqual(expect.objectContaining({
      title: expect.any(String),
      inputLabel: expect.any(String),
      required: expect.any(String),
      openPopup: expect.any(String)
    }));
  });

  it('uses uni-ui presentation components without moving locale persistence into the page', async () => {
    const localePage = await readSource('../src/pages/settings/locale.vue');

    for (const component of [
      'uni-section',
      'uni-list',
      'uni-list-item',
      'uni-forms',
      'uni-forms-item',
      'uni-notice-bar'
    ]) {
      expect(localePage).toContain(`<${component}`);
    }
    expect(localePage).toContain("from '../../ui/fullnet-ui-contract'");
    expect(localePage).toContain('createLocaleSettingsModel');
    expect(localePage).not.toContain('uni.request');
  });
});
