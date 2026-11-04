import { describe, expect, it } from 'vitest';
import {
  findWorkflowBusinessDetailRoute,
  formatWorkflowBusinessLabel
} from './workflowBusinessDetail';

describe('workflowBusinessDetail', () => {
  it('formats explicit business title when present', () => {
    expect(formatWorkflowBusinessLabel('规则变更 #42', 'data_approval.serial_rule.update', 'req-1'))
      .toBe('规则变更 #42');
  });

  it('falls back to business type and id when title is blank', () => {
    expect(formatWorkflowBusinessLabel(null, 'purchase', 'PO-001'))
      .toBe('purchase · PO-001');
  });

  it('resolves whitelisted business detail routes only', () => {
    expect(findWorkflowBusinessDetailRoute('data_approval.serial_rule.update', () => true)).toEqual({
      routeName: 'data-approval-requests',
      idQueryKey: 'requestId'
    });
    expect(findWorkflowBusinessDetailRoute('purchase', () => true)).toBeUndefined();
    expect(findWorkflowBusinessDetailRoute('', () => true)).toBeUndefined();
  });

  it('业务类型已知也必须具备目标单据全部精确读取权限', () => {
    const permissions = new Set(['enterprise_request.enterprise_requests.read']);
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', key => permissions.has(key))).toBeUndefined();
    permissions.add('enterprise_request.enterprise_request_lines.read');
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', key => permissions.has(key))).toEqual({ routeName: 'enterprise-requests', idQueryKey: 'requestId' });
    expect(findWorkflowBusinessDetailRoute('data_approval.serial_rule.update', () => false)).toBeUndefined();
  });
  it('原型属性和任意 URL 不能成为业务详情路由', () => {
    for (const type of ['__proto__', 'constructor', 'toString', 'https://example.com'])
      expect(findWorkflowBusinessDetailRoute(type, () => true)).toBeUndefined();
  });

  it('resolves the enterprise request route and rejects object prototype names', () => {
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', () => true)).toEqual({
      routeName: 'enterprise-requests', idQueryKey: 'requestId'
    });
    for (const key of ['__proto__', 'constructor', 'toString', 'https://example.com']) {
      expect(findWorkflowBusinessDetailRoute(key, () => true)).toBeUndefined();
    }
  });
  it('requires the precise business read permission when used by a protected view', () => {
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', () => false)).toBeUndefined();
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', permission => permission === 'workflow.todos.read')).toBeUndefined();
    expect(findWorkflowBusinessDetailRoute('demo.enterprise_request', permission => ['enterprise_request.enterprise_requests.read', 'enterprise_request.enterprise_request_lines.read'].includes(permission))?.routeName).toBe('enterprise-requests');
    expect(findWorkflowBusinessDetailRoute('data_approval.serial_rule.update', permission => permission === 'data_approvals.requests.read')?.routeName).toBe('data-approval-requests');
  });
});
