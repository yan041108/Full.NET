import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';

const imageIdPattern = /^sha256:[a-f0-9]{64}$/;
const rateLimitPattern = /\b(?:toomanyrequests|rate exceeded)\b/iu;

// 仅下载限额允许转向同标签 Docker 官方源；权限、网络和后续核对错误保持失败。
async function pullOfficialImage(target, run, wait) {
  const sources = [`public.ecr.aws/docker/library/${target}`, `docker.io/library/${target}`];
  for (const [index, source] of sources.entries()) {
    for (let attempt = 0; ; attempt++) {
      try { run(['pull', source]); return source; }
      catch (error) {
        const message = String(error?.message ?? error);
        if (!rateLimitPattern.test(message)) throw error;
        // 带宽配额耗尽不会因短退避恢复；短时请求限流仍沿用两次有界退避。
        if (/\bdata limit exceeded\b/iu.test(message) || attempt >= 2) {
          if (index === sources.length - 1) throw error;
          console.warn(`测试镜像源限额耗尽，切换同标签 Docker 官方源：${target}`);
          break;
        }
        const milliseconds = [5000, 15000][attempt];
        console.warn(`测试镜像下载限流，${milliseconds / 1000}秒后重试：${target}（${attempt + 2}/3）`);
        await wait(milliseconds);
      }
    }
  }
}

/** 从官方公共镜像源预拉取同版本标签，并验证别名指向实际选中的镜像；失败不能继续测试。 */
export async function prepareCiTestImages({ redisOnly = false, includeMySql84 = false, run = docker, wait = delay } = {}) {
  if (redisOnly && includeMySql84) throw new Error('Redis-only 不能同时准备 MySQL。');
  const images = [...(redisOnly ? [] : ['mysql:8.0', ...(includeMySql84 ? ['mysql:8.4'] : [])]), 'redis:8.6'];
  for (const target of images) {
    const source = await pullOfficialImage(target, run, wait);
    const sourceId = run(['image', 'inspect', '--format', '{{.Id}}', source]).trim();
    if (!imageIdPattern.test(sourceId)) throw new Error(`镜像 ID 无效：${source}`);
    // Testcontainers 的缺失时拉取策略复用原有标签，测试夹具无需改变版本或凭据。
    run(['tag', source, target]);
    const targetId = run(['image', 'inspect', '--format', '{{.Id}}', target]).trim();
    if (targetId !== sourceId) throw new Error(`测试镜像别名不匹配：${target}`);
  }
  return images;
}

function docker(args) {
  const result = spawnSync('docker', args, {
    encoding: 'utf8', env: testRunEnvironment(), windowsHide: true,
    timeout: 5 * 60_000, maxBuffer: 4 * 1024 * 1024,
  });
  if (result.error || result.status !== 0) {
    throw new Error(`docker ${args.join(' ')} 失败：${result.error?.message ?? result.stderr ?? result.stdout}`);
  }
  return result.stdout;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const args = process.argv.slice(2);
  if (args.some(arg => !['--redis-only', '--include-mysql-8.4'].includes(arg))) {
    throw new Error('只支持 --redis-only 与 --include-mysql-8.4。');
  }
  await withTestRun({ cwd: process.cwd(), heavy: true }, async () => {
    const images = await prepareCiTestImages({ redisOnly: args.includes('--redis-only'),
      includeMySql84: args.includes('--include-mysql-8.4') });
    console.log(`官方测试镜像已准备并核对：${images.join(', ')}`);
  });
}
