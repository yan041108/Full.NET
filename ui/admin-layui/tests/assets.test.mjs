import { describe, expect, it } from 'vitest';
import { readFile } from 'node:fs/promises';

describe('Layui 管理端静态契约', () => {
  it('使用本地 Layui 且不引入 SPA 运行时', async () => {
    const [html, packageText, styles, appScript, navigationScript] = await Promise.all([
      readFile('index.html', 'utf8'),
      readFile('package.json', 'utf8'),
      readFile('css/app.css', 'utf8'),
      readFile('js/app.js', 'utf8'),
      readFile('js/core/navigation.js', 'utf8')
    ]);
    const packageDefinition = JSON.parse(packageText);

    expect(packageDefinition.dependencies.layui).toBe('2.13.8');
    expect(packageDefinition.dependencies.vue).toBeUndefined();
    expect(packageDefinition.dependencies.react).toBeUndefined();
    expect(html).toContain('Full.NET');
    expect(html).toContain('data-testid="load-current-user"');
    expect(styles).toContain('var(--fullnet-color-accent)');
    expect(`${appScript}\n${navigationScript}`).not.toContain('.innerHTML');
    expect(navigationScript).toContain('document.createElement');
    expect(navigationScript).toContain('.textContent');
  });
});
