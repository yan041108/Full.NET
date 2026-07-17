import { describe, expect, it } from 'vitest';
import { readFile } from 'node:fs/promises';

describe('Vue 管理端静态资源', () => {
  it('入口显式引用仓库内的 SVG 图标', async () => {
    const [html, favicon] = await Promise.all([
      readFile('index.html', 'utf8'),
      readFile('public/favicon.svg', 'utf8')
    ]);

    expect(html).toContain('href="/favicon.svg"');
    expect(favicon).toContain('<svg');
    expect(favicon).toContain('#42b9a6');
  });
});
