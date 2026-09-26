/**
 * 将官方组合根投影为应用预设的静态模块引用闭包。
 */
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { buildPresetMigrationInventory } from './framework-manifest-utils.mjs';

const COMPOSITION_ROOT = 'src/Composition/Full.NET.Composition';
const CATALOG = `${COMPOSITION_ROOT}/FullNetModuleCatalog.cs`;
const SELECTION = `${COMPOSITION_ROOT}/FullNetModuleSelection.cs`;
const PROJECT = `${COMPOSITION_ROOT}/Full.NET.Composition.csproj`;

function projectCatalog(source, selected) {
  const available = [...source.matchAll(/^\s*new ([A-Za-z0-9]+)Module\(\),\s*$/gm)]
    .map((match) => match[1]);
  for (const module of selected) {
    if (!available.includes(module)) throw new Error('Preset module is absent from Composition: ' + module);
  }
  let result = source.split(/\r?\n/).filter((line) => {
    const using = /^using Full\.NET\.Modules\.([A-Za-z0-9]+);$/.exec(line);
    if (using && !selected.has(using[1])) return false;
    const instance = /^\s*new ([A-Za-z0-9]+)Module\(\),\s*$/.exec(line);
    if (instance && !selected.has(instance[1])) return false;
    return true;
  }).join('\n');
  if (!selected.has('Ai')) {
    result = result.split('\n').filter((line) =>
      !line.startsWith('using Full.NET.AI.')
      && line !== 'using Microsoft.Extensions.DependencyInjection.Extensions;'
      && !line.includes('// Provider 消费 Ai 的请求凭据作用域')).join('\n');
    const registration = /\s*if \((?:apiModules|workerModules)\.Any\(module => module is AiModule\)\)\s*\{\s*AddAiProviderServices\(services, configuration\);\s*\}/g;
    const matches = [...result.matchAll(registration)];
    if (matches.length !== 2) throw new Error('Cannot locate both AI provider registrations');
    result = result.replace(registration, '');
    const start = result.indexOf('    /// <summary>\n    /// 仅在 AiModule');
    const end = result.indexOf('    private static readonly string[] OfficialHostProfiles', start);
    if (start < 0 || end < 0) throw new Error('Cannot locate AI provider service method');
    result = result.slice(0, start) + result.slice(end);
  }
  return result.replace('Composition 项目引用全部实现', 'Composition 项目引用预设实现');
}

function projectSelection(source, selected) {
  const marker = 'public static readonly IReadOnlyList<string> OfficialModuleNames =';
  const start = source.indexOf(marker);
  const end = source.indexOf('    ];', start);
  if (start < 0 || end < 0) throw new Error('Cannot locate official module name list');
  const listStart = start + marker.length;
  const original = source.slice(listStart, end);
  const available = [...original.matchAll(/"([A-Za-z0-9]+)"/g)].map((match) => match[1]);
  for (const module of selected) {
    if (!available.includes(module)) throw new Error('Preset module is absent from official names: ' + module);
  }
  const projected = original.split(/\r?\n/).filter((line) => {
    const name = /"([A-Za-z0-9]+)"/.exec(line)?.[1];
    return !name || selected.has(name);
  }).join('\n');
  return source.slice(0, listStart) + projected + source.slice(end);
}

function projectProject(source, selected) {
  const referenced = new Set();
  const projected = source.split(/\r?\n/).filter((line) => {
    const module = /Full\.NET\.Modules\.([A-Za-z0-9]+)(?:\.Contracts)?[\\/]Full\.NET\.Modules\./.exec(line)?.[1];
    if (module) {
      if (!selected.has(module)) return false;
      if (!line.includes('.Contracts.csproj')) referenced.add(module);
    }
    if (!selected.has('Ai') && line.includes('Full.NET.AI.Providers.')) return false;
    if (!selected.has('EnterpriseRequest') && line.includes('Full.NET.Modules.EnterpriseRequest.csproj')) return false;
    if (selected.has('EnterpriseRequest') && line.includes('Full.NET.Modules.EnterpriseRequest.csproj')) referenced.add('EnterpriseRequest');
    return true;
  }).join('\n');
  for (const module of selected) {
    if (!referenced.has(module)) throw new Error('Preset module has no Composition project reference: ' + module);
  }
  return projected;
}

export function projectCompositionSource({ catalog, selection, project }, modules) {
  const selected = new Set(modules);
  if (selected.size !== modules.length || !selected.has('Identity')) {
    throw new Error('Preset module list is invalid');
  }
  return {
    catalog: projectCatalog(catalog, selected),
    selection: projectSelection(selection, selected),
    project: projectProject(project, selected),
  };
}

export function projectPresetComposition(appRoot, preset, modules) {
  const frameworkRoot = join(appRoot, 'framework', 'fullnet');
  const files = { catalog: CATALOG, selection: SELECTION, project: PROJECT };
  const sources = Object.fromEntries(Object.entries(files).map(([name, relative]) =>
    [name, readFileSync(join(frameworkRoot, relative), 'utf8')]));
  const projected = projectCompositionSource(sources, modules);
  const manifest = JSON.parse(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'));
  for (const [name, relative] of Object.entries(files)) {
    const content = projected[name];
    writeFileSync(join(frameworkRoot, relative), content, 'utf8');
    manifest.managedFiles[relative] = createHash('sha256').update(content).digest('hex');
  }
  manifest.projectedPreset = preset;
  if (!manifest.migrationInventory?.scripts?.length) {
    throw new Error('Framework manifest migration inventory is missing');
  }
  manifest.migrationInventory = buildPresetMigrationInventory(manifest.migrationInventory, preset, modules);
  const encoded = JSON.stringify(manifest, null, 2) + '\n';
  writeFileSync(join(appRoot, 'framework-manifest.json'), encoded, 'utf8');
  writeFileSync(join(frameworkRoot, 'framework-manifest.json'), encoded, 'utf8');
}
