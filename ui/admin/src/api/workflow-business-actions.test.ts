import { beforeEach, describe, expect, it, vi } from 'vitest';
import { listWorkflowBusinessActions } from './workflow-business-actions';
import { http } from './http';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const entry = { ownerKey: 'data_approval', actionKey: 'data_approval.apply_serial_rule_update', actionVersion: 1,
  businessType: 'data_approval.serial_rule.update', placementKey: 'root_finalizer', actionHash: 'a'.repeat(64) };
beforeEach(() => vi.clearAllMocks());
describe('业务动作目录', () => {
  it('经生成客户端请求实际目录并接纳空作用域', async () => {
    vi.mocked(http.request).mockResolvedValueOnce({ items: [entry] }); expect(await listWorkflowBusinessActions()).toEqual([entry]);
    expect(http.request).toHaveBeenCalledWith('/api/v1/workflow/definitions/business-action-catalog', expect.objectContaining({ method: 'GET' }), undefined);
    vi.mocked(http.request).mockResolvedValueOnce({ items: [] }); expect(await listWorkflowBusinessActions()).toEqual([]);
  });
  it.each([{ ...entry, actionVersion: 65536 }, { ...entry, actionHash: 'A'.repeat(64) }, { ...entry, placementKey: 'arbitrary_sql' }, { ...entry, ownerKey: '' }])('拒绝不完整或不受支持的元数据 %j', async value => {
    vi.mocked(http.request).mockResolvedValueOnce({ items: [value] }); await expect(listWorkflowBusinessActions()).rejects.toThrow('client.invalid_workflow_business_action_catalog');
  });
  it('拒绝同动作版本重复，保留请求取消信号', async () => {
    const controller = new AbortController(); vi.mocked(http.request).mockResolvedValueOnce({ items: [entry, entry] });
    await expect(listWorkflowBusinessActions(controller.signal)).rejects.toThrow();
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/workflow/definitions/business-action-catalog', expect.objectContaining({ method: 'GET' }), controller.signal);
  });
});
