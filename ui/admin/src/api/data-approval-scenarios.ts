import {
  dataApprovalsGetScenario,
  dataApprovalsListScenarios,
  dataApprovalsUpdateScenarioBinding,
  type DataApprovalScenarioResponse,
  type UpdateDataApprovalScenarioBindingBody
} from '@fullnet/client-contracts';
import { http } from './http';

/** 列出当前作用域内已登记的数据审批场景及其绑定状态。 */
export function listDataApprovalScenarios(
  signal?: AbortSignal
): Promise<DataApprovalScenarioResponse[]> {
  return dataApprovalsListScenarios(http, {}, signal);
}

/** 读取单个数据审批场景的绑定状态。 */
export function getDataApprovalScenario(
  scenarioKey: string,
  signal?: AbortSignal
): Promise<DataApprovalScenarioResponse> {
  return dataApprovalsGetScenario(http, { scenarioKey }, signal);
}

/** 更新数据审批场景的启停状态与工作流版本绑定。 */
export function updateDataApprovalScenarioBinding(
  scenarioKey: string,
  body: UpdateDataApprovalScenarioBindingBody,
  signal?: AbortSignal
): Promise<DataApprovalScenarioResponse> {
  return dataApprovalsUpdateScenarioBinding(http, { scenarioKey, body }, signal);
}

export type { DataApprovalScenarioResponse, UpdateDataApprovalScenarioBindingBody };
