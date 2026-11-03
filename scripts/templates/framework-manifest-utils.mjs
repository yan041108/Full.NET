/**
 * framework-manifest.json 升级比较辅助函数（F15 将扩展完整升级流程）。
 */
export { buildPresetMigrationInventory, inferMigrationModuleOwner } from './migration-script-modules.mjs';

export function compareFrameworkVersions(left, right) {
  const parse = (value) => {
    const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(value);
    if (!match) {
      throw new Error('Invalid framework version: ' + value);
    }
    return [Number(match[1]), Number(match[2]), Number(match[3])];
  };

  const [lMajor, lMinor, lPatch] = parse(left);
  const [rMajor, rMinor, rPatch] = parse(right);

  if (lMajor !== rMajor) return lMajor - rMajor;
  if (lMinor !== rMinor) return lMinor - rMinor;
  return lPatch - rPatch;
}

export function isUpgradeAllowed(currentVersion, targetVersion) {
  return compareFrameworkVersions(targetVersion, currentVersion) >= 0;
}

/**
 * 按脚本相同文件名配对双库迁移；归属未审计时仅登记，不参与预设过滤。
 */
export function buildMigrationInventory(managedFiles) {
  const prefix = 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/';
  const scripts = new Map();
  for (const [relativePath, digest] of Object.entries(managedFiles)) {
    if (!relativePath.startsWith(prefix)) continue;
    const match = /^(SqlServer|MySql)\/([^/]+\.sql)$/.exec(relativePath.slice(prefix.length));
    if (!match) throw new Error('Unexpected migration resource path: ' + relativePath);
    const [, provider, name] = match;
    const providers = scripts.get(name) ?? {};
    providers[provider] = digest;
    scripts.set(name, providers);
  }
  if (scripts.size === 0) throw new Error('Source bundle has no migration scripts');
  const paired = [...scripts.entries()].sort(([left], [right]) => left.localeCompare(right, 'en'))
    .map(([name, providers]) => {
      for (const provider of ['SqlServer', 'MySql']) {
        if (!providers[provider]) throw new Error(`Migration ${name} is missing ${provider} counterpart`);
      }
      return { name, providers };
    });
  return { selectionStatus: 'unscoped', scripts: paired };
}

/** 枚举有限的编译分支；不推测符号取值，所有可能分支都必须具有相同种子清单。 */
function seedSourceVariants(source, modulePath) {
  const lines = source.split(/\r?\n/u);
  let index = 0;
  const reject = () => new Error(`Conditional seed registration requires explicit review: ${modulePath}`);
  function read(depth) {
    if (depth > 16) throw reject();
    let variants = [''];
    while (index < lines.length) {
      const directive = /^\s*#(if|elif|else|endif)\b/u.exec(lines[index])?.[1];
      if (directive && directive !== 'if') return { variants, stop: directive };
      if (!directive) {
        const line = lines[index++] + '\n';
        variants = variants.map(value => value + line);
        continue;
      }
      index++;
      let branch = read(depth + 1);
      const choices = [...branch.variants];
      while (branch.stop === 'elif') {
        index++; branch = read(depth + 1); choices.push(...branch.variants);
        if (choices.length > 64) throw reject();
      }
      if (branch.stop === 'else') {
        index++; branch = read(depth + 1); choices.push(...branch.variants);
      } else choices.push('');
      if (branch.stop !== 'endif' || variants.length * choices.length > 64) throw reject();
      index++;
      variants = variants.flatMap(prefix => choices.map(choice => prefix + choice));
    }
    return { variants, stop: undefined };
  }
  const result = read(0);
  if (result.stop) throw reject();
  return result.variants;
}

/** 从受管源码和 Migrator 注册点建立种子闭包；未注册或缺文件时拒绝发布。 */
export function buildSeedInventory(managedFiles, readSource, presetModules) {
  const prefix = 'src/Modules/Full.NET.Modules.';
  const activeSource = (path) => readSource(path).replace(/\/\*[\s\S]*?\*\/|\/\/[^\r\n]*/g, '');
  const migrationRegistrations = (modulePath) => {
    const source = activeSource(modulePath);
    const method = /\bAddMigrationServices\s*\([^)]*\)\s*\{/.exec(source);
    if (!method) throw new Error(`Module migration registration method is missing: ${modulePath}`);
    let depth = 1;
    let end = method.index + method[0].length;
    for (; end < source.length && depth > 0; end += 1) {
      if (source[end] === '{') depth += 1;
      if (source[end] === '}') depth -= 1;
    }
    if (depth !== 0) throw new Error(`Module migration registration method is incomplete: ${modulePath}`);
    const body = source.slice(method.index + method[0].length, end - 1);
    const registrations = seedSourceVariants(body, modulePath).map(variant =>
      [...variant.matchAll(/\bIDataSeedContributor\s*,\s*([A-Za-z0-9]+SeedContributor)\s*>/g)].map(match => match[1]));
    const expected = JSON.stringify(registrations[0]);
    if (registrations.some(value => JSON.stringify(value) !== expected)) {
      throw new Error(`Conditional seed registration requires explicit review: ${modulePath}`);
    }
    return registrations[0];
  };
  const contributors = [];
  const byModule = new Map();
  for (const path of Object.keys(managedFiles).sort()) {
    if (!path.startsWith(prefix)) continue;
    const match = /^src\/Modules\/Full\.NET\.Modules\.([A-Za-z0-9]+)\/Seeding\/([A-Za-z0-9]+SeedContributor)\.cs$/.exec(path);
    if (!match) continue;
    const [, module, name] = match;
    const modulePath = `${prefix}${module}/${module}Module.cs`;
    if (!managedFiles[modulePath]) throw new Error(`Seed contributor ${path} has no module registration source`);
    const declaration = new RegExp(`\\bclass\\s+${name}\\b`);
    if (!declaration.test(activeSource(path))) throw new Error(`Seed contributor declaration is missing: ${path}`);
    if (!migrationRegistrations(modulePath).includes(name)) throw new Error(`Seed contributor is not registered: ${path}`);
    contributors.push({ module, name, path });
    const entries = byModule.get(module) ?? [];
    entries.push(path);
    byModule.set(module, entries);
  }
  if (contributors.length === 0) throw new Error('Source bundle has no seed contributors');
  const presets = {};
  for (const [preset, modules] of Object.entries(presetModules)) {
    presets[preset] = modules.flatMap((module) => byModule.get(module) ?? []).sort();
  }
  for (const { module, path } of contributors) {
    if (!Object.values(presetModules).some((modules) => modules.includes(module))) {
      throw new Error(`Seed contributor ${path} is not selected by any preset`);
    }
  }
  for (const modulePath of Object.keys(managedFiles).filter((path) =>
    /^src\/Modules\/Full\.NET\.Modules\.([A-Za-z0-9]+)\/\1Module\.cs$/.test(path)
    && /\bIDataSeedContributor\s*,/.test(activeSource(path)))) {
    const module = /^src\/Modules\/Full\.NET\.Modules\.([A-Za-z0-9]+)\//.exec(modulePath)[1];
    for (const name of migrationRegistrations(modulePath)) {
      if (!contributors.some((entry) => entry.module === module && entry.name === name)) {
        throw new Error(`Registered seed contributor has no managed source: ${module}/${name}`);
      }
    }
  }
  return { contributors, presets };
}
