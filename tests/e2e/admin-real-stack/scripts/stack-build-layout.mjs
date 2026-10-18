import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import path from 'node:path';

const hosts = new Set(['Migrator', 'Api', 'Worker']);

/** 构建与启动共用同一布局，避免实际入口再次落回 Debug 或源码目录。 */
export function resolveRealStackBuildLayout(repoRoot, {
  provider = 'SqlServer', env = process.env, temporaryRoot = tmpdir()
} = {}) {
  const workspace = path.resolve(repoRoot);
  const key = createHash('sha256')
    .update(process.platform === 'win32' ? workspace.toLowerCase() : workspace)
    .digest('hex').slice(0, 16);
  const providerKey = provider.toLowerCase();
  if (!['sqlserver', 'mysql'].includes(providerKey)) throw new Error('Unknown real-stack database provider');
  // 按工作区稳定复用编译缓存，按数据库隔离日志；默认产物不进入源码目录。
  const outputRoot = path.join(temporaryRoot, 'fullnet-real-stack', key);
  const resolveOption = (name, fallback) => {
    const value = env[name] || fallback;
    // Playwright 与宿主进程的工作目录不同，显式路径必须保持相同含义。
    if (typeof value !== 'string' || !path.isAbsolute(value)) throw new Error(`${name} must be an absolute path`);
    return path.resolve(value);
  };
  const artifactsPath = resolveOption('FULLNET_E2E_BUILD_ROOT', path.join(outputRoot, 'build'));
  const logRoot = resolveOption('FULLNET_E2E_LOG_ROOT', path.join(outputRoot, providerKey));
  const hostName = host => {
    if (!hosts.has(host)) throw new Error('Unknown real-stack Host');
    return `Full.NET.Host.${host}`;
  };
  return {
    configuration: 'Release', artifactsPath, logRoot,
    buildArgs: ['-c', 'Release', '-p:UseArtifactsOutput=true', `-p:ArtifactsPath=${artifactsPath}`,
      '-m:1', '-nodeReuse:false'],
    assemblyPath: host => path.join(artifactsPath, 'bin', hostName(host), 'release', `${hostName(host)}.dll`),
    logPath: host => { hostName(host); return path.join(logRoot, `${host.toLowerCase()}.log`); }
  };
}
