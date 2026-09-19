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