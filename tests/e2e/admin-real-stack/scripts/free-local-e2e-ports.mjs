import { execSync } from 'node:child_process';

/** Playwright webServer 占用的本地端口；全量 L3 前释放避免孤儿 Vite/OIDC 进程阻塞启动。 */
const E2E_WEB_PORTS = [25173, 25175, 5173, 5174];

function lineListensOnPort(line, port) {
  const portToken = String(port);
  return (
    line.includes(`:${portToken} `)
    || line.includes(`:${portToken}\t`)
    || line.includes(`]:${portToken} `)
    || line.includes(`]:${portToken}\t`)
  );
}

function killWindowsListenersOnPort(port) {
  let out = '';
  try {
    out = execSync('netstat -ano | findstr "LISTENING"', {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore']
    });
  } catch {
    return;
  }
  for (const line of out.split(/\r?\n/)) {
    if (!lineListensOnPort(line, port)) {
      continue;
    }
    const match = line.trim().match(/\s(\d+)\s*$/);
    if (!match) {
      continue;
    }
    const pid = Number.parseInt(match[1], 10);
    if (!Number.isFinite(pid) || pid <= 0) {
      continue;
    }
    try {
      execSync(`taskkill /PID ${pid} /F`, { stdio: 'ignore' });
    } catch {
      // 进程可能已退出
    }
  }
}

/**
 * 释放本机 E2E Web 端口（Windows netstat/taskkill）。非 Windows 环境跳过。
 */
export function freeLocalE2ePorts() {
  if (process.platform !== 'win32') {
    return;
  }
  for (const port of E2E_WEB_PORTS) {
    killWindowsListenersOnPort(port);
  }
  try {
    execSync('ping -n 2 127.0.0.1 >nul', { stdio: 'ignore' });
  } catch {
    // ignore
  }
  for (const port of E2E_WEB_PORTS) {
    killWindowsListenersOnPort(port);
  }
}
