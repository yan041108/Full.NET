import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { connect } from 'node:net';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('..', import.meta.url));
const profiles = ['frontend', 'backend', 'full'];
const readJson = file => JSON.parse(readFileSync(file, 'utf8'));

export function parseArguments(args) {
  let profile = 'full';
  let json = false;
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--') continue;
    if (args[index] === '--json') json = true;
    else if (args[index] === '--profile') profile = args[++index];
    else throw new Error(`未知参数：${args[index]}`);
  }
  if (!profiles.includes(profile)) throw new Error('profile 必须为 frontend、backend 或 full。');
  return { profile, json };
}

export function assessVersion(actual, expected, majorOnly = false) {
  const version = actual.trim().replace(/^v/, '');
  return majorOnly
    ? /^\d+\.\d+\.\d+(?:[-+].*)?$/.test(version) && version.split('.')[0] === expected
    : version === expected;
}

function resolveCommand(name) {
  const result = process.platform === 'win32'
    ? spawnSync('where.exe', [name], { encoding: 'utf8', timeout: 5_000, windowsHide: true })
    : spawnSync('which', [name], { encoding: 'utf8', timeout: 5_000 });
  return (result.stdout ?? '').trim().split(/\r?\n/)
    .find(candidate => candidate && (process.platform !== 'win32' || /\.(exe|cmd|bat)$/i.test(candidate)));
}

/** Windows 的 cmd 包装器使用 PowerShell 字面量调用，路径和参数不作为脚本插值执行。 */
function probe(name, args, cwd) {
  const executable = resolveCommand(name);
  if (!executable) return { ok: false, executable: null, output: '', reason: 'PATH 中未找到工具' };
  let command = executable;
  let commandArgs = args;
  if (process.platform === 'win32' && /\.(cmd|bat)$/i.test(executable)) {
    const quote = value => `'${value.replaceAll("'", "''")}'`;
    const script = `& ${[executable, ...args].map(quote).join(' ')}; exit $LASTEXITCODE`;
    command = 'powershell.exe';
    commandArgs = ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')];
  }
  const result = spawnSync(command, commandArgs, {
    cwd, encoding: 'utf8', timeout: 10_000, windowsHide: true, maxBuffer: 256 * 1024,
    // Corepack 只读取已有工具缓存；自检不允许触发工具下载或交互式确认。
    env: { ...process.env, COREPACK_ENABLE_NETWORK: '0', COREPACK_ENABLE_DOWNLOAD_PROMPT: '0' }
  });
  return {
    ok: !result.error && result.status === 0,
    executable,
    output: (result.stdout ?? '').trim(),
    reason: result.error?.code === 'ETIMEDOUT' ? '检查超时（10 秒）' : `退出码 ${result.status ?? '未知'}`
  };
}

/** 仅探测回环端口是否监听；不发送 HTTP、不终止进程，也不把空闲端口视为应用健康。 */
export function probePort(host, port) {
  return new Promise(resolve => {
    const socket = connect({ host, port });
    const finish = state => { socket.destroy(); resolve(state); };
    socket.setTimeout(500);
    socket.once('connect', () => finish('listening'));
    socket.once('timeout', () => finish('unknown'));
    socket.once('error', error => finish(error.code === 'ECONNREFUSED' ? 'free' : 'unknown'));
  });
}

export async function inspectEnvironment(root, profile, options = {}) {
  if (!profiles.includes(profile)) throw new Error('不支持的环境检查范围。');
  const run = options.probe ?? probe;
  const checkPort = options.probePort ?? probePort;
  const platform = options.platform ?? process.platform;
  const checks = [];
  const add = (name, status, detail, hint = '') => checks.push({ name, status, detail, hint });
  const pkg = readJson(path.join(root, 'package.json'));
  const nodeMajor = readFileSync(path.join(root, '.nvmrc'), 'utf8').trim().replace(/^v/, '');
  add('node', assessVersion(process.version, nodeMajor, true) ? 'pass' : 'fail',
    `${process.version}; ${process.execPath}; 要求主版本 ${nodeMajor}`, '按 .nvmrc 选择 Node，再重新打开终端。');
  const pnpm = run('pnpm', ['--version'], root);
  const pnpmVersion = pnpm.output.split(/\r?\n/).find(line => /^\d+\.\d+\.\d+$/.test(line)) ?? '';
  const expectedPnpm = pkg.packageManager.replace(/^pnpm@/, '').split('+')[0];
  add('pnpm', pnpm.ok && assessVersion(pnpmVersion, expectedPnpm) ? 'pass' : 'fail',
    `${pnpm.executable ?? '缺失'}; 实际 ${pnpmVersion || pnpm.reason}; 要求 ${expectedPnpm}`,
    '检查 PATH 的工具优先级；用项目声明的 pnpm 版本，不自动修改锁文件。');
  const git = run('git', ['--version'], root);
  add('git', git.ok ? 'pass' : 'fail', `${git.executable ?? '缺失'}; ${git.ok ? git.output : git.reason}`);

  if (profile !== 'frontend') {
    const sdk = readJson(path.join(root, 'global.json')).sdk;
    // SDK 选择规则由 dotnet 在仓库 cwd 读取 global.json 执行，避免重复实现 rollForward。
    const dotnet = run('dotnet', ['--version'], root);
    add('dotnet-sdk', dotnet.ok ? 'pass' : 'fail',
      `${dotnet.executable ?? '缺失'}; ${dotnet.ok ? dotnet.output : dotnet.reason}; global.json=${sdk.version}/${sdk.rollForward ?? '默认'}`,
      '安装或选择 global.json 允许的 SDK；此检查不执行 restore/build。');
    const docker = run('docker', ['info', '--format', '{{json .ServerVersion}}'], root);
    add('docker-engine', docker.ok ? 'pass' : 'fail',
      `${docker.executable ?? '缺失'}; ${docker.ok ? docker.output : docker.reason}`,
      '启动并等待 Docker Engine 就绪；此检查不创建容器或连接数据库。');
  }

  if (profile !== 'backend') {
    for (const [relative, dependency] of [
      ['ui/admin/package.json', 'vite'],
      ['tests/e2e/admin-parity/package.json', '@playwright/test'],
      ['tests/e2e/admin-real-stack/package.json', '@playwright/test']
    ]) {
      try {
        const manifest = path.join(root, relative);
        const require = createRequire(manifest);
        const resolved = require.resolve(`${dependency}/package.json`);
        const expected = readJson(manifest).devDependencies[dependency];
        const actual = readJson(resolved).version;
        add(`${relative}:${dependency}`, actual === expected ? 'pass' : 'fail', `${actual}; ${resolved}`,
          '使用匹配的 pnpm 执行 pnpm install --frozen-lockfile。');
      } catch {
        add(`${relative}:${dependency}`, 'fail', '无法从工作区解析依赖', '使用匹配的 pnpm 执行 pnpm install --frozen-lockfile。');
      }
    }
    const configs = ['tests/e2e/admin-parity/playwright.config.mjs'];
    if (profile === 'full') configs.push('tests/e2e/admin-real-stack/playwright.config.mjs');
    const urls = new Set();
    for (const config of configs) {
      const content = readFileSync(path.join(root, config), 'utf8');
      for (const match of content.matchAll(/url:\s*['"](http:\/\/(?:localhost|127\.0\.0\.1):\d+)['"]/g)) urls.add(match[1]);
    }
    for (const url of urls) {
      const parsed = new URL(url);
      const state = await checkPort(parsed.hostname, Number(parsed.port));
      add(`port:${url}`, state === 'free' ? 'pass' : 'warn', state,
        '监听中不等于冲突或服务就绪；检查现有服务归属及 Playwright reuseExistingServer 配置，不自动结束进程。');
    }
    try {
      const require = createRequire(path.join(root, 'tests/e2e/admin-parity/package.json'));
      const { chromium } = require('@playwright/test');
      const usesEdge = platform === 'win32' && !process.env.GITHUB_ACTIONS;
      const browserPath = usesEdge
        ? [process.env['ProgramFiles(x86)'], process.env.ProgramFiles, process.env.LOCALAPPDATA]
          .filter(Boolean).map(base => path.join(base, 'Microsoft/Edge/Application/msedge.exe')).find(existsSync)
        : chromium.executablePath();
      add('browser-binary', browserPath && existsSync(browserPath) ? 'pass' : 'fail', browserPath ?? '未发现 Edge',
        usesEdge ? '安装系统 Edge；这里只验证文件存在，不代表浏览器启动或页面验收通过。'
          : '核对 Playwright channel 配置与系统平台；安装相应浏览器后另做启动验证。');
      if (platform !== 'win32' && !process.env.GITHUB_ACTIONS) {
        add('browser-channel', 'warn', '现有本地配置使用 msedge，请核对非 Windows 环境的 Edge 安装。');
      }
    } catch {
      add('browser-binary', 'fail', '无法加载 Playwright', '先恢复工作区依赖，再检查浏览器。');
    }
  }
  return { profile, ok: checks.every(check => check.status !== 'fail'), checks };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const args = parseArguments(process.argv.slice(2));
    const report = await inspectEnvironment(repositoryRoot, args.profile);
    if (args.json) console.log(JSON.stringify(report, null, 2));
    else {
      for (const item of report.checks) {
        console.log(`[${item.status.toUpperCase()}] ${item.name}: ${item.detail}`);
        if (item.status !== 'pass' && item.hint) console.log(`  ${item.hint}`);
      }
      console.log(`环境自检：${report.ok ? '通过（警告需按任务核对）' : '存在阻塞项'}。不代表业务测试通过。`);
    }
    process.exitCode = report.ok ? 0 : 1;
  } catch (error) {
    console.error(`环境自检失败：${error.message}`);
    process.exitCode = 2;
  }
}
