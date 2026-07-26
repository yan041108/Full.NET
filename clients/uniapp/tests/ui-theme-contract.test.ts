import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

async function readSource(path: string): Promise<string> {
  return readFile(new URL(path, import.meta.url), 'utf8').catch(() => '');
}

describe('Full.NET uni-ui theme contract', () => {
  it('maps the required uni-ui variables to approved Full.NET semantic tokens', async () => {
    const [uniTheme, adapterTheme, contract, appShell] = await Promise.all([
      readSource('../src/uni.scss'),
      readSource('../src/styles/fullnet-uni-ui.scss'),
      readSource('../src/ui/fullnet-ui-contract.ts'),
      readSource('../src/App.vue')
    ]);

    expect(uniTheme).toContain('$uni-color-primary: #08736d;');
    expect(uniTheme).toContain('$uni-color-success: #20764f;');
    expect(uniTheme).toContain('$uni-color-warning: #936109;');
    expect(uniTheme).toContain('$uni-color-error: #b83e3e;');
    expect(uniTheme).toContain('$uni-text-color: #17212b;');
    expect(uniTheme).toContain('$uni-text-color-grey: #596670;');
    expect(uniTheme).toContain('$uni-border-color: #dfe4df;');
    expect(uniTheme).toContain('$uni-border-radius-base: 12px;');
    expect(uniTheme).not.toContain('@import');

    expect(adapterTheme).toContain('--fullnet-ui-color-primary: #08736d;');
    expect(adapterTheme).toContain('--fullnet-ui-radius-control: 12px;');
    expect(contract).toContain("primary: '#08736d'");
    expect(contract).toContain('controlRadiusPx: 12');
    expect(appShell).toContain('@use "./styles/fullnet-uni-ui.scss";');
  });

  it('keeps uni-ui implementation selectors out of business pages', async () => {
    const [localePage, appShell] = await Promise.all([
      readSource('../src/pages/settings/locale.vue'),
      readSource('../src/App.vue')
    ]);

    expect(`${localePage}\n${appShell}`).not.toMatch(/\.uni-[a-z0-9_-]+/iu);
  });
});
