import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';

const imageIdPattern = /^sha256:[a-f0-9]{64}$/;
const rateLimitPattern = /\b(?:toomanyrequests|rate exceeded)\b/iu;
const bandwidthLimitPattern = /\bdata limit exceeded\b/iu;
const registryTimeoutPattern = /\bClient\.Timeout exceeded while awaiting headers\b/u;

// 下载限额和已知的仓库响应头超时允许切换同标签官方源；其余失败直接报告。
async function pullOfficialImage(target, run, wait) {
  const sources = [`public.ecr.aws/docker/library/${target}`, `docker.io/library/${target}`];
  for (const [index, source] of sources.entries()) {
    for (let attempt = 0; ; attempt++) {
      try { run(['pull', source]); return source; }
      catch (error) {
        const message = String(error?.message ?? error);
        const bandwidthExhausted = bandwidthLimitPattern.test(message);
        const registryTimedOut = registryTimeoutPattern.test(message);
        if (!bandwidthExhausted && !registryTimedOut && !rateLimitPattern.test(message)) throw error;
        // 带宽配额耗尽直接切换源。短时限流和响应头超时沿用两次有界退避。
        if (bandwidthExhausted || attempt >= 2) {
          if (index === sources.length - 1) throw error;
          console.warn(`测试镜像源下载失败，切换同标签 Docker 官方源：${source}`);
          break;
        }
        const milliseconds = [5000, 15000][attempt];
        const reason = registryTimedOut ? '等待响应头超时' : '限流';
        console.warn(`测试镜像下载${reason}，${milliseconds / 1000}秒后重试：${source}（${attempt + 2}/3）`);
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
