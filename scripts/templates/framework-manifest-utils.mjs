/**
 * framework-manifest.json 升级比较辅助函数（F15 将扩展完整升级流程）。
 */
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
