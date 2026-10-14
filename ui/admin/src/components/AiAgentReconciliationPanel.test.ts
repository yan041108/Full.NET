import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElCheckbox, ElInput, ElSwitch } from 'element-plus';
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

async function load(w: VueWrapper) {
  await w.get('button').trigger('click'); await flushPromises();
}
async function review(w: VueWrapper) {
  w.getComponent(ElCheckbox).vm.$emit('update:modelValue', true); await flushPromises();
}
async function evidence(w: VueWrapper, value = 'a'.repeat(64)) {
  w.findAllComponents(ElInput)[0]!.vm.$emit('update:modelValue', value); await flushPromises();
}
const submit = (w: VueWrapper) => w.findAll('button').at(-1)!;

it('服务端快照不允许对账时保持只读', async () => {
  vi.mocked(api.getAiAgentReconciliation).mockResolvedValue({ ...row, canReconcile: false });
  const w = render(); await load(w);
  expect(w.findComponent(ElInput).exists()).toBe(false); expect(api.reconcileAiAgentRun).not.toHaveBeenCalled();
});
it('错误运行身份的快照不能取得提交资格', async () => {
  vi.mocked(api.getAiAgentReconciliation).mockResolvedValue({ ...row, runId: '00000000-0000-0000-0000-000000000002' });
  const w = render(); await load(w);
  expect(w.findComponent(ElInput).exists()).toBe(false); expect(w.text()).toContain('对账处理失败');
});
it('提交结果不明时重试保留相同请求身份和核证材料', async () => {
  vi.mocked(api.reconcileAiAgentRun).mockRejectedValueOnce(new Error('network unknown'));
  const w = render(); await load(w); await evidence(w); await review(w);
  await submit(w).trigger('click'); await flushPromises();
  expect(w.emitted('reconciled')).toBeUndefined(); expect(w.text()).toContain('对账处理失败');
  const first = structuredClone(vi.mocked(api.reconcileAiAgentRun).mock.calls[0]![1]);
  await submit(w).trigger('click'); await flushPromises();
  expect(api.reconcileAiAgentRun).toHaveBeenCalledTimes(2);
  expect(vi.mocked(api.reconcileAiAgentRun).mock.calls[1]![1]).toEqual(first);
  expect(w.emitted('reconciled')).toHaveLength(1);
});
it('材料修改须重新核对并为新材料生成请求身份', async () => {
  vi.mocked(api.reconcileAiAgentRun).mockRejectedValueOnce(new Error('network unknown'));
  const w = render(); await load(w); await evidence(w); await review(w);
  await submit(w).trigger('click'); await flushPromises();
  const first = vi.mocked(api.reconcileAiAgentRun).mock.calls[0]![1].clientRequestId;
  await evidence(w, 'b'.repeat(64)); expect(submit(w).attributes('disabled')).toBeDefined(); await review(w);
  await submit(w).trigger('click'); await flushPromises();
  const next = vi.mocked(api.reconcileAiAgentRun).mock.calls[1]![1];
  expect(next.clientRequestId).not.toBe(first); expect(next.evidenceDigest).toBe('B'.repeat(64));
});
it('完整已核证计量保留零值和可选缓存数值', async () => {
  const w = render(); await load(w); await evidence(w);
  w.getComponent(ElSwitch).vm.$emit('update:modelValue', true); await flushPromises();
  const fields = w.findAllComponents(ElInput);
  for (const [i, value] of ['0', '0', '0'].entries()) fields[i + 1]!.vm.$emit('update:modelValue', value);
  await flushPromises(); await review(w); await submit(w).trigger('click'); await flushPromises();
  expect(vi.mocked(api.reconcileAiAgentRun).mock.calls[0]![1]).toMatchObject({ inputTokens: 0, outputTokens: 0, cachedInputTokens: 0 });
});
it.each([
  ['', '1', ''], ['-1', '1', ''], ['1.5', '1', ''], ['1000000001', '1', ''],
  ['1e3', '1', ''], ['1', '', ''], ['1', '2', '2']
])('无效或不完整计量 %s/%s/%s 阻止提交', async (input, output, cached) => {
  const w = render(); await load(w); await evidence(w);
  w.getComponent(ElSwitch).vm.$emit('update:modelValue', true); await flushPromises();
  const fields = w.findAllComponents(ElInput);
  [input, output, cached].forEach((value, i) => fields[i + 1]!.vm.$emit('update:modelValue', value));
  await flushPromises(); await review(w);
  expect(submit(w).attributes('disabled')).toBeDefined(); expect(api.reconcileAiAgentRun).not.toHaveBeenCalled();
});
it('待提交期间重复点击不产生第二次写请求', async () => {
  let finish!: (value: boolean) => void;
  vi.mocked(api.reconcileAiAgentRun).mockReturnValue(new Promise(resolve => { finish = resolve; }));
  const w = render(); await load(w); await evidence(w); await review(w);
  await submit(w).trigger('click'); await submit(w).trigger('click');
  expect(api.reconcileAiAgentRun).toHaveBeenCalledTimes(1); finish(true); await flushPromises();
});
it('卸载取消提交，迟到结果不通知父页面刷新', async () => {
  let finish!: (value: boolean) => void;
  vi.mocked(api.reconcileAiAgentRun).mockReturnValue(new Promise(resolve => { finish = resolve; }));
  const w = render(); await load(w); await evidence(w); await review(w); await submit(w).trigger('click');
  const signal = vi.mocked(api.reconcileAiAgentRun).mock.calls[0]![2]!;
  w.unmount(); finish(true); await flushPromises();
  expect(signal.aborted).toBe(true); expect(w.emitted('reconciled')).toBeUndefined();
});
