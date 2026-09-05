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
    expect(findWorkflowBusinessDetailRoute('data_approval.serial_rule.update')).toEqual({
      routeName: 'data-approval-requests',
      idQueryKey: 'requestId'
    });
    expect(findWorkflowBusinessDetailRoute('purchase')).toBeUndefined();
    expect(findWorkflowBusinessDetailRoute('')).toBeUndefined();
  });
});
