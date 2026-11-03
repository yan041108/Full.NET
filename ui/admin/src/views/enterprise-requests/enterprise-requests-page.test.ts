import { expect, it, vi } from 'vitest';
import type { HttpClient, EnterpriseRequestResponse } from '@fullnet/client-contracts';
import { useEnterpriseRequestPage } from './enterprise-requests-page.generated';

const row = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf330', status: 'Draft' } as EnterpriseRequestResponse;
function create(permissions: string[]) {
  const request = vi.fn(); const problem = vi.fn();
  const page = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
    hasPermission: key => permissions.includes(key), onProblem: problem });
  return { page, request, problem };
}
it('编辑权限不授予提交审批权限，也不调用提交 API', async () => {
  const { page, request } = create(['enterprise_request.enterprise_requests.update']);
  expect(page.canUpdate.value).toBe(true); expect(page.canSubmit.value).toBe(false);
  expect(await page.submitForApproval(row)).toBe(false); expect(request).not.toHaveBeenCalled();
});
it('独立提交权限允许调用审批 API，拒绝畸形返回且恢复按钮状态', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.submit']);
  request.mockResolvedValue({ id: row.id });
  expect(page.canSubmit.value).toBe(true); expect(page.canUpdate.value).toBe(false);
  expect(await page.submitForApproval(row)).toBe(false);
  expect(request).toHaveBeenCalledWith(expect.stringContaining('/submit-for-approval'), { method: 'POST' }, expect.any(AbortSignal));
  expect(problem).toHaveBeenCalled(); expect(page.changing.value).toBe(false);
});
it('非草稿和重复点击不会重复提交审批', async () => {
  const { page, request } = create(['enterprise_request.enterprise_requests.submit']);
  expect(await page.submitForApproval({ ...row, status: 'Submitted' })).toBe(false);
  let reject!: (reason: Error) => void;
  request.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail; }));
  const pending = page.submitForApproval(row);
  expect(await page.submitForApproval(row)).toBe(false); expect(request).toHaveBeenCalledTimes(1);
  reject(new Error('network')); expect(await pending).toBe(false); expect(page.changing.value).toBe(false);
});
it('会话重置取消列表请求且旧租户迟到数据不能重新进入列表', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.read']);
  let resolve!: (value: unknown) => void;
  request.mockImplementation(() => new Promise(done => { resolve = done; }));
  const pending = page.load(); const signal = request.mock.calls[0]![2] as AbortSignal;
  page.reset(); expect(signal.aborted).toBe(true);
  resolve({ items: [row], page: 1, pageSize: 20, total: 1 });
  expect(await pending).toBe(false); expect(page.items.value).toEqual([]); expect(problem).not.toHaveBeenCalled();
});
it('会话重置后的旧写入失败不会覆盖新上下文错误或锁住新操作', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.submit']);
  let reject!: (reason: Error) => void;
  request.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail; }));
  const pending = page.submitForApproval(row); const signal = request.mock.calls[0]![2] as AbortSignal;
  page.reset(); reject(new Error('old request')); expect(await pending).toBe(false);
  expect(signal.aborted).toBe(true); expect(problem).not.toHaveBeenCalled(); expect(page.changing.value).toBe(false);
});
