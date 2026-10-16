import { beforeEach, describe, expect, it, vi } from 'vitest';
import { workflowListMyCc, workflowMarkCcRead } from '@fullnet/client-contracts';
import { listMyWorkflowCc, markWorkflowCcRead } from './workflow-cc';
import { workflowCcRecord as row, workflowCcReceipt as receipt } from '../test/workflow-cc-fixtures';
vi.mock('@fullnet/client-contracts', async original => ({ ...await original<typeof import('@fullnet/client-contracts')>(), workflowListMyCc: vi.fn(), workflowMarkCcRead: vi.fn() }));
beforeEach(() => { vi.resetAllMocks(); vi.mocked(workflowListMyCc).mockResolvedValue([row]); vi.mocked(workflowMarkCcRead).mockResolvedValue(receipt); });
describe('本人抄送客户端契约与资源归属', () => {
  it('沿用生成客户端并传递取消信号', async () => { const signal = new AbortController().signal; await listMyWorkflowCc(signal); await markWorkflowCcRead(row.id, signal); expect(workflowListMyCc).toHaveBeenCalledWith(expect.anything(), {}, signal); expect(workflowMarkCcRead).toHaveBeenCalledWith(expect.anything(), { ccId: row.id }, signal); });
  it.each(['bad', '', '00000000-0000-0000-0000-000000000000'])('非法标识在 HTTP 前拒绝 %s', async id => { await expect(markWorkflowCcRead(id)).rejects.toThrow(); expect(workflowMarkCcRead).not.toHaveBeenCalled(); });
  it('拒绝错误资源的已读回执', async () => { vi.mocked(workflowMarkCcRead).mockResolvedValueOnce({ ...receipt, id: row.instanceId }); await expect(markWorkflowCcRead(row.id)).rejects.toThrow(); });
  it('允许回执 UUID 大小写差异', async () => { vi.mocked(workflowMarkCcRead).mockResolvedValueOnce({ ...receipt, id: row.id.toUpperCase() }); await expect(markWorkflowCcRead(row.id)).resolves.toMatchObject({ readAtUtc: receipt.readAtUtc }); });
  it.each([null, '', 'invalid-date'])('拒绝无效已读时间 %s', async value => { vi.mocked(workflowMarkCcRead).mockResolvedValueOnce({ ...receipt, readAtUtc: value } as never); await expect(markWorkflowCcRead(row.id)).rejects.toThrow(); });
  it('拒绝非数组目录', async () => { vi.mocked(workflowListMyCc).mockResolvedValueOnce({ items: [row] } as never); await expect(listMyWorkflowCc()).rejects.toThrow(); });
  it('拒绝大小写重复行', async () => { vi.mocked(workflowListMyCc).mockResolvedValueOnce([row, { ...row, id: row.id.toUpperCase() }]); await expect(listMyWorkflowCc()).rejects.toThrow(); });
  it('有界目录不能超过服务端规定的 100 项', async () => { vi.mocked(workflowListMyCc).mockResolvedValueOnce(Array.from({ length: 101 }, (_, index) => ({ ...row, id: '019c1a90-8f9b-7b9c-9cf4-' + (index + 1).toString(16).padStart(12, '0') }))); await expect(listMyWorkflowCc()).rejects.toThrow(); });
  it.each([{ id: 'bad' }, { instanceId: 'bad' }, { stepId: 'bad' }, { nodeKey: '' }, { businessType: '' }, { businessId: '' }, { businessTitle: 2 }, { createdAtUtc: 'bad' }, { readAtUtc: 'bad' }])('非法抄送字段不能接入 %j', async change => { vi.mocked(workflowListMyCc).mockResolvedValueOnce([{ ...row, ...change }] as never); await expect(listMyWorkflowCc()).rejects.toThrow(); });
  it('遵循生成 DTO 的可空步骤和标题契约', async () => { vi.mocked(workflowListMyCc).mockResolvedValueOnce([{ ...row, stepId: null, businessTitle: null }]); await expect(listMyWorkflowCc()).resolves.toHaveLength(1); });
  it('错误继续保留标准 ProblemDetails 供调用者区分', async () => { const problem = { status: 404, code: 'workflow.cc_not_found', title: '不存在' }; vi.mocked(workflowMarkCcRead).mockRejectedValueOnce(problem); await expect(markWorkflowCcRead(row.id)).rejects.toBe(problem); });
});
