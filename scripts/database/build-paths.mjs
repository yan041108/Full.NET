import { createHash } from 'node:crypto';
import { homedir } from 'node:os';
import path from 'node:path';

/** 按仓库绝对路径隔离可复用工具输出，不复制源码，也不争用业务项目的 bin/obj。 */
export function diagnosticsBuildPaths(root) {
  const source = process.platform === 'win32' ? path.resolve(root).toLowerCase() : path.resolve(root);
  const key = createHash('sha256').update(source).digest('hex').slice(0, 16);
  const cache = process.platform === 'win32'
    ? process.env.LOCALAPPDATA ?? path.join(homedir(), 'AppData/Local')
    : process.env.XDG_CACHE_HOME ?? path.join(homedir(), '.cache');
  const output = path.join(cache, 'Full.NET/database-diagnostics', key);
  return { output, dll: path.join(output, 'bin/Full.NET.DatabaseDiagnostics.Cli/release/Full.NET.DatabaseDiagnostics.Cli.dll') };
}
