#!/usr/bin/env node
/**
 * 从 IFullNetModule 实现提取 Dependencies 并生成 Mermaid 依赖图。
 * 输出：docs/operations/module-dependency-graph.mmd
 */
import { readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const modulesRoot = path.join(repositoryRoot, 'src/Modules');
const outputPath = path.join(
  repositoryRoot,
  'docs/operations/module-dependency-graph.mmd'
);

const namePattern = /public\s+string\s+Name\s*=>\s*"([^"]+)"/;
const dependenciesBlockPattern =
  /public\s+IReadOnlyCollection<string>\s+Dependencies\s*=>\s*\[([\s\S]*?)\];/;

function parseDependencies(block) {
  return [...block.matchAll(/"([^"]+)"/g)].map(match => match[1]);
}

async function loadModuleGraph() {
  const entries = await readdir(modulesRoot, { withFileTypes: true });
  const modules = [];

  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }

    const moduleFileName = `${entry.name.replace('Full.NET.Modules.', '')}Module.cs`;
    const candidates = [
      path.join(modulesRoot, entry.name, moduleFileName),
      path.join(modulesRoot, entry.name, `${entry.name.split('.').at(-1)}Module.cs`),
    ];

    let source = null;
    for (const candidate of candidates) {
      try {
        source = await readFile(candidate, 'utf8');
        break;
      } catch {
        // try next candidate
      }
    }

    if (!source) {
      const files = await readdir(path.join(modulesRoot, entry.name));
      const moduleFile = files.find(file => file.endsWith('Module.cs'));
      if (!moduleFile) {
        continue;
      }

      source = await readFile(path.join(modulesRoot, entry.name, moduleFile), 'utf8');
    }

    const nameMatch = source.match(namePattern);
    const dependenciesMatch = source.match(dependenciesBlockPattern);
    if (!nameMatch) {
      continue;
    }

    modules.push({
      name: nameMatch[1],
      dependencies: dependenciesMatch ? parseDependencies(dependenciesMatch[1]) : [],
    });
  }

  modules.sort((left, right) => left.name.localeCompare(right.name));
  return modules;
}

function renderMermaid(modules) {
  const lines = [
    '%% 自动生成：pnpm run generate:module-dependency-graph',
    '%% 源：src/Modules/*Module.cs IFullNetModule.Dependencies',
    'flowchart LR',
  ];

  for (const module of modules) {
    for (const dependency of module.dependencies) {
      lines.push(`  ${dependency} --> ${module.name}`);
    }
  }

  return `${lines.join('\n')}\n`;
}

export async function generateModuleDependencyGraph({ write = true } = {}) {
  const modules = await loadModuleGraph();
  const content = renderMermaid(modules);

  if (write) {
    await writeFile(outputPath, content, 'utf8');
  }

  return { modules, content, outputPath };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  generateModuleDependencyGraph()
    .then(result => {
      console.log(`Wrote ${result.outputPath} (${result.modules.length} modules)`);
    })
    .catch(error => {
      console.error(error);
      process.exit(1);
    });
}
