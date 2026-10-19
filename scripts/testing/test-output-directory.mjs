import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

/** 本地验收只使用独立临时目录；CI 保持上传产物所需的固定路径。 */
export function testOutputDirectory(repositoryRoot, relativePath) {
  if (process.env.CI && process.env.CI !== 'false' && process.env.CI !== '0') {
    return path.join(repositoryRoot, 'artifacts', relativePath);
  }

  const temporaryRoot = mkdtempSync(path.join(tmpdir(), 'fullnet-test-output-'));
  process.once('exit', (code) => {
    // 成功后立即回收；失败仅保留定位问题所需的原始结果，不保留构建副本。
    if (code === 0) rmSync(temporaryRoot, { recursive: true, force: true });
    else process.stderr.write(`本轮失败结果保存在临时目录：${temporaryRoot}\n`);
  });
  return path.join(temporaryRoot, relativePath);
}
