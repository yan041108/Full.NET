/** 模块加载顺序不能改变发布摘要；按包、版本、许可文件和原文作跨机器稳定排序。 */
export function renderRuntimeNotices(sourceLicense, entries, bundledLicense) {
  const ordered = [...entries].sort((a, b) => {
    const left = `${a.name}\n${a.version}\n${a.file}\n${a.text}`;
    const right = `${b.name}\n${b.version}\n${b.file}\n${b.text}`;
    return left < right ? -1 : left > right ? 1 : 0;
  });
  return [sourceLicense, ...ordered.map(entry => `${entry.name}@${entry.version}\n${entry.text}`), bundledLicense].join('\n\n');
}
