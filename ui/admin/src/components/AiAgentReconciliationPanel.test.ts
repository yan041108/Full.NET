import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElCheckbox, ElInput } from 'element-plus';
import Panel from './AiAgentReconciliationPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-agent-runs';
vi.mock('../api/ai-agent-runs');
const id = '00000000-0000-0000-0000-000000000001';
const row = { runId: id, version: 8, statusKey: 'reconciliation_required', usageStatus: 'unknown', reservedTokens: 200,
  quotaMonthKey: '2026-09', inputTokens: null, outputTokens: null, cachedInputTokens: null, canReconcile: true };
const wrappers: VueWrapper[] = [];
function render(allow = true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allow);
  const wrapper = mount(Panel, { props: { runId: id }, global: { plugins: [pinia] } }); wrappers.push(wrapper); return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getAiAgentReconciliation).mockResolvedValue(row); vi.mocked(api.reconcileAiAgentRun).mockResolvedValue(true); });
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); });
it('仅有读取权限时不展示人工提交入口', async () => {
  const w = render(false); await w.get('button').trigger('click'); await flushPromises();
  expect(w.findComponent(ElInput).exists()).toBe(false); expect(w.text()).toContain('计量状态: 未知');
});
it('核对材料后关闭未知运行，省略计量不提交零值', async () => {
  const w = render(); await w.get('button').trigger('click'); await flushPromises();
  w.getComponent(ElInput).vm.$emit('update:modelValue', 'a'.repeat(64)); await flushPromises();
  w.getComponent(ElCheckbox).vm.$emit('update:modelValue', true); await flushPromises();
  await w.findAll('button').at(-1)!.trigger('click'); await flushPromises();
  expect(api.reconcileAiAgentRun).toHaveBeenCalledWith(id, { clientRequestId: expect.any(String), expectedVersion: 8, evidenceDigest: 'A'.repeat(64) }, expect.any(AbortSignal));
  expect(w.emitted('reconciled')).toHaveLength(1);
});
it('切换运行取消旧请求，迟到快照不能显示', async () => {
  let resolve!: (value: typeof row) => void; vi.mocked(api.getAiAgentReconciliation).mockReturnValue(new Promise(r => { resolve = r; }));
  const w = render(); await w.get('button').trigger('click'); const signal = vi.mocked(api.getAiAgentReconciliation).mock.calls[0]![1]!;
  await w.setProps({ runId: '00000000-0000-0000-0000-000000000002' }); resolve(row); await flushPromises();
  expect(signal.aborted).toBe(true); expect(w.text()).not.toContain('2026-09');
});
