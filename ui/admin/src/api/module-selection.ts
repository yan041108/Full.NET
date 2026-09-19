import {
  type IdentityModuleSelectionIssue,
  type IdentityModuleSelectionModuleState,

  identityGetModuleSelectionRuntime,
  identityValidateModuleSelection,
  isIdentityModuleSelectionAnalysis,
  type IdentityModuleSelectionAnalysis,
  type IdentityModuleSelectionValidateRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 读取当前进程部署期模块启用配置分析结果。 */
export async function getModuleSelectionRuntime(
  signal?: AbortSignal
): Promise<IdentityModuleSelectionAnalysis> {
  const value = await identityGetModuleSelectionRuntime(http, {}, signal);
  if (!isIdentityModuleSelectionAnalysis(value)) {
    throw new Error('client.invalid_module_selection_runtime');
  }

  return value;
}

/** 校验候选模块启用配置是否满足官方 DAG。 */
export async function validateModuleSelection(
  request: IdentityModuleSelectionValidateRequest,
  signal?: AbortSignal
): Promise<IdentityModuleSelectionAnalysis> {
  const value = await identityValidateModuleSelection(
    http,
    { body: { enabled: request.enabled ?? null, preset: request.preset ?? null } },
    signal
  );
  if (!isIdentityModuleSelectionAnalysis(value)) {
    throw new Error('client.invalid_module_selection_validate');
  }

  return value;
}

export type {
  IdentityModuleSelectionAnalysis,
  IdentityModuleSelectionIssue,
  IdentityModuleSelectionModuleState,
  IdentityModuleSelectionValidateRequest
};
