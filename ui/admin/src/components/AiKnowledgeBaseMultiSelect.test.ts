import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Select from './AiKnowledgeBaseMultiSelect.vue';
import { listKnowledgeBases } from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
vi.mock('../api/ai-knowledge');
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showWarning: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001'; const second = '01900000-0000-7000-8000-000000000002';
const base = { id, name: '第一页知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
let wrappers: VueWrapper[] = [];
async function render(allow = true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(allow);
  const wrapper = mount(Select, { props: { modelValue: [] }, global: { plugins: [pinia] } }); wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listKnowledgeBases).mockResolvedValue({ items: [base], page: 1, pageSize: 20, total: 21 }); });
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it('多选跨页保留已选名称和资源标识', async () => {
  const wrapper = await render(); wrapper.getComponent(ElSelect).vm.$emit('update:modelValue', [id]); await flushPromises(); await wrapper.setProps({ modelValue: [id] });
  vi.mocked(listKnowledgeBases).mockResolvedValue({ items: [{ ...base, id: second, name: '第二页知识库' }], page: 2, pageSize: 20, total: 21 });
  await wrapper.get('[data-testid="knowledge-bases-next"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).toContain('第一页知识库'); wrapper.getComponent(ElSelect).vm.$emit('update:modelValue', [id, second]); await flushPromises(); expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[id, second]]);
});
it.each([{ values: [id, id] }, { values: ['invalid'] }, { values: [second] }])('拒绝重复、非法或未加载选项', async ({ values }) => {
  const wrapper = await render(); wrapper.getComponent(ElSelect).vm.$emit('update:modelValue', values); await flushPromises(); expect(wrapper.emitted('update:modelValue')).toBeUndefined();
});
it('九个有效候选也不能超过八库上限', async () => {
  const values = Array.from({ length: 9 }, (_, i) => `01900000-0000-7000-8000-${String(i + 1).padStart(12, '0')}`);
  vi.mocked(listKnowledgeBases).mockResolvedValue({ items: values.map(id => ({ ...base, id })), page: 1, pageSize: 20, total: 9 }); const wrapper = await render();
  wrapper.getComponent(ElSelect).vm.$emit('update:modelValue', values); await flushPromises(); expect(wrapper.emitted('update:modelValue')).toBeUndefined();
});
it('没有目录读取权不查询或呈现多选', async () => { const wrapper = await render(false); expect(listKnowledgeBases).not.toHaveBeenCalled(); expect(wrapper.findComponent(ElSelect).exists()).toBe(false); });
it('切换会话上下文清空选择并拒绝迟到目录', async () => {
  let finish!: (value: Awaited<ReturnType<typeof listKnowledgeBases>>) => void; vi.mocked(listKnowledgeBases).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); useSessionStore().switching = true; await flushPromises(); finish({ items: [base], page: 1, pageSize: 20, total: 1 }); await flushPromises();
  expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[]]); expect(wrapper.text()).not.toContain(base.name);
});
