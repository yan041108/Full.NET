import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElOption } from 'element-plus';
import { afterEach, expect, it, vi } from 'vitest';
import Select from './AiKnowledgeBaseSelect.vue';
import { listKnowledgeBases } from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
vi.mock('../api/ai-knowledge');
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn() }));
const first = '01900000-0000-7000-8000-000000000001'; const second = '01900000-0000-7000-8000-000000000002';
const base = { id: first, name: '目录', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, createdAtUtc: 'now', updatedAtUtc: null, version: 1 };
let wrappers: VueWrapper[] = [];
async function render() {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockReturnValue(true);
  const wrapper = mount(Select, { props: { modelValue: '', candidateIds: [second] }, global: { plugins: [pinia] } }); wrappers.push(wrapper); await flushPromises(); return wrapper;
}
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.clearAllMocks(); vi.restoreAllMocks(); });
it('候选仅保留本次请求与授权目录的交集', async () => {
  vi.mocked(listKnowledgeBases).mockResolvedValue({ items: [base, { ...base, id: second }], page: 1, pageSize: 20, total: 2 }); const wrapper = await render();
  expect(wrapper.findAllComponents(ElOption).map(option => option.props('value'))).toEqual([second]);
});
it('候选集合变化清空选择并拒绝旧目录响应', async () => {
  let finish!: (value: Awaited<ReturnType<typeof listKnowledgeBases>>) => void;
  vi.mocked(listKnowledgeBases).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValue({ items: [base], page: 1, pageSize: 20, total: 1 });
  const wrapper = await render(); await wrapper.setProps({ candidateIds: [first] }); await flushPromises(); finish({ items: [{ ...base, id: second }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
  expect(wrapper.findAllComponents(ElOption).map(option => option.props('value'))).toEqual([first]); expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['']);
});
