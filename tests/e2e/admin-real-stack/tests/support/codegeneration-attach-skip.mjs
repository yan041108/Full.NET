import { existsSync } from 'node:fs';
import { test } from '@playwright/test';

/** 附着模式无 bootstrap 工作区时跳过 CodeGen 真实栈用例。 */
export function skipCodegenWhenAttachModeWithoutWorkspace() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP !== '1') {
    return;
  }
  if (process.env.FULLNET_E2E_CODEGEN_WORKSPACE) {
    return;
  }
  const statePath = new URL('../../.stack-state.json', import.meta.url);
  if (existsSync(statePath)) {
    return;
  }
  test.skip(
    true,
    '附着 L3 需要 bootstrap 工作区或设置 FULLNET_E2E_CODEGEN_WORKSPACE（见 host-admin-l3-full 说明）'
  );
}
