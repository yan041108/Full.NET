import {
  isDiagnosticPolicy,
  settingsGetHostDiagnosticPolicy,
  settingsRestoreHostDiagnosticPolicy,
  settingsUpdateHostDiagnosticPolicy,
  type DiagnosticPolicy,
  type DiagnosticPolicyRule
} from '@fullnet/client-contracts';
import { http } from './http';

/** 导出诊断策略与规则模型，供策略页编辑器和压力态预览共用同一契约。 */
export type { DiagnosticPolicy, DiagnosticPolicyRule };

/** 读取 Host 诊断策略，并对响应结构做失败关闭校验。 */
export async function getDiagnosticPolicy(
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsGetHostDiagnosticPolicy(http, {}, signal);
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}

/** 更新 Host 诊断策略规则集，并携带配置项版本维持乐观并发。 */
export async function updateDiagnosticPolicy(
  pressureState: string,
  rules: DiagnosticPolicyRule[],
  configEntryVersion: number,
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsUpdateHostDiagnosticPolicy(
    http,
    {
      body: {
        pressureState,
        rules,
        configEntryVersion
      }
    },
    signal
  );
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}

/** 恢复 Host 诊断策略到默认状态。 */
export async function restoreDiagnosticPolicy(
  configEntryVersion: number,
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsRestoreHostDiagnosticPolicy(
    http,
    { body: { configEntryVersion } },
    signal
  );
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}
