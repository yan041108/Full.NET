import { existsSync, readFileSync } from 'node:fs';
import { test } from '@playwright/test';

/** 附着模式未配置 Observability 日志目录时跳过运行日志用例。 */
export function skipObservabilityWhenAttachModeWithoutLogRoot() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP !== '1') {
    return;
  }
  if (process.env.FULLNET_E2E_OBSERVABILITY_LOG_ROOT) {
    return;
  }
  const statePath = new URL('../../.stack-state.json', import.meta.url);
  if (existsSync(statePath)) {
    const state = JSON.parse(readFileSync(statePath, 'utf8'));
    if (state.observabilityLogRoot) {
      return;
    }
  }
  test.skip(
    true,
    '附着 L3 需 bootstrap 的 observabilityLogRoot 或设置 FULLNET_E2E_OBSERVABILITY_LOG_ROOT'
  );
}
