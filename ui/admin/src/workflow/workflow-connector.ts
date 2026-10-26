export interface WorkflowConnectorConfiguration {
  fieldPolicies: Record<string, string>;
  connectorPolicy: { connectorKey: string; connectorVersion: number; inputFields: Record<string, string>; outputFields: Record<string, string> };
  boundConnectorHash?: string;
}
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(value) && !['__proto__', 'prototype', 'constructor'].includes(value);
const invalid = (): never => { throw new TypeError('client.invalid_workflow_connector'); };

/** 普通设计器只提交受控目录键和字段映射，不接受地址、认证、脚本或嵌套响应路径。 */
export function readWorkflowConnector(value: unknown): WorkflowConnectorConfiguration {
  if (!object(value) || !object(value.connectorPolicy) || !object(value.fieldPolicies)) return invalid();
  const policy = value.connectorPolicy;
  if (Object.keys(policy).some(name => !['connectorKey', 'connectorVersion', 'inputFields', 'outputFields'].includes(name)) ||
    !key(policy.connectorKey) || policy.connectorKey.length > 64 || !Number.isSafeInteger(policy.connectorVersion) || Number(policy.connectorVersion) < 1) return invalid();
  const access: Record<string, string> = {};
  for (const [field, mode] of Object.entries(value.fieldPolicies)) {
    if (!key(field) || typeof mode !== 'string' || !['hidden', 'readOnly', 'editable', 'required'].includes(mode)) return invalid();
    access[field] = mode;
  }
  const mapping = (source: unknown, output: boolean): Record<string, string> => {
    if (!object(source) || Object.keys(source).length > 16) return invalid();
    const result: Record<string, string> = {};
    for (const [target, field] of Object.entries(source)) {
      if (!key(target) || !key(field) || (output ? !['editable', 'required'].includes(access[target] ?? '') : !access[field] || access[field] === 'hidden')) return invalid();
      result[target] = field;
    }
    return result;
  };
  const result: WorkflowConnectorConfiguration = { fieldPolicies: access, connectorPolicy: { connectorKey: policy.connectorKey,
    connectorVersion: Number(policy.connectorVersion), inputFields: mapping(policy.inputFields, false), outputFields: mapping(policy.outputFields, true) } };
  if (value.boundConnectorHash !== undefined) {
    if (typeof value.boundConnectorHash !== 'string' || !/^[0-9a-f]{64}$/u.test(value.boundConnectorHash)) return invalid();
    result.boundConnectorHash = value.boundConnectorHash;
  }
  return result;
}
