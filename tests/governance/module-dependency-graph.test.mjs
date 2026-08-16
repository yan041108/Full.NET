import assert from 'node:assert/strict';
import test from 'node:test';
import { generateModuleDependencyGraph } from '../../eng/architecture/generate-module-dependency-graph.mjs';

test('module-dependency-graph.mmd 与 IFullNetModule.Dependencies 一致', async () => {
  const { modules, content } = await generateModuleDependencyGraph({ write: false });

  assert.ok(modules.length >= 12, '应解析全部官方模块');

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
