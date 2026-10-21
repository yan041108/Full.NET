import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../../', import.meta.url));
const readXml = file => readFileSync(path.join(root, file), 'utf8')
  .replace(/<!--[\s\S]*?-->/gu, '');
const normalize = file => path.relative(root, path.resolve(root, file))
  .replaceAll('\\', '/');

test('solution includes every repository project referenced by its build graph', () => {
  const projects = [...readXml('Full.NET.slnx').matchAll(/<Project\b[^>]*\bPath="([^"]+)"[^>]*>/gu)]
    // Website 项是解决方案中的文档目录，不是可解析 ProjectReference 的 MSBuild 项目。
    .filter(match => !/\bType="Website"/u.test(match[0]))
    .map(match => normalize(match[1]));
  assert.ok(projects.length > 0, '解决方案没有项目，不能验证构建依赖。');
  const included = new Set(projects);
  const missing = [];

  for (const project of projects) {
    assert.ok(existsSync(path.join(root, project)), `解决方案项目不存在：${project}`);
    for (const match of readXml(project).matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]+)"/gu)) {
      // 仓库项目使用显式相对引用；构建时生成的 MSBuild 属性路径不属于静态列表。
      if (match[1].includes('$(')) continue;
      const referenced = normalize(path.join(path.dirname(project), match[1].replaceAll('\\', '/')));
      assert.ok(existsSync(path.join(root, referenced)), `项目引用不存在：${project} -> ${referenced}`);
      if (!included.has(referenced)) missing.push(`${project} -> ${referenced}`);
    }
  }

  // VS 的解决方案调度需要完整项目列表，不能靠命令行递归构建掩盖遗漏。
  assert.deepEqual(missing, [], `解决方案遗漏构建依赖：\n${missing.join('\n')}`);
});
