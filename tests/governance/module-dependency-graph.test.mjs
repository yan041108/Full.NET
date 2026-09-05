import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { generateModuleDependencyGraph } from '../../eng/architecture/generate-module-dependency-graph.mjs';

const normalizeLineEndings = (value) => value.replace(/\r\n?/g, '\n');

test('module-dependency-graph.mmd 与 IFullNetModule.Dependencies 一致', async () => {
  const { modules, content, outputPath } = await generateModuleDependencyGraph({ write: false });
  const committedContent = await readFile(outputPath, 'utf8');

  assert.ok(modules.length >= 12, '应解析全部官方模块');
  assert.equal(
    normalizeLineEndings(committedContent),
    normalizeLineEndings(content),
    '已提交 Mermaid 图除平台行尾外必须与生成器输出完全一致，请运行 pnpm generate:module-dependency-graph'
  );

  for (const module of modules) {
    for (const dependency of module.dependencies) {
      assert.match(
        content,
        new RegExp(`${dependency}\\s+-->\\s+${module.name}`),
        `${module.name} 依赖 ${dependency} 必须出现在 Mermaid 图中`
      );
    }
  }
});
