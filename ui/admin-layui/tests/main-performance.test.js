import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

describe('Layui 生产入口性能边界', () => {
  it('单体运行库保持动态导入而不阻塞主应用入口', () => {
    const filePath = path.resolve(
      path.dirname(fileURLToPath(import.meta.url)),
      '../js/main.js'
    );
    const source = readFileSync(filePath, 'utf8');

    expect(source).not.toContain("import 'layui/dist/layui.js'");
    expect(source).toContain("import('layui/dist/layui.js')");
  });
});
