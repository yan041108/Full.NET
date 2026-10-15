import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiAgentRunsView from './AiAgentRunsView.vue';
import { createAiAgentRun, getAiAgentRun, streamAiAgentRunEvents } from '../api/ai-agent-runs';
import { listAiModelConfigs } from '../api/ai-model-configs';
import { useSessionStore } from '../auth/session';

vi.mock('../api/ai-agent-runs', () => ({
  createAiAgentRun: vi.fn(),
  getAiAgentRun: vi.fn(),
  cancelAiAgentRun: vi.fn(),
  resumeAiAgentRun: vi.fn(),
  streamAiAgentRunEvents: vi.fn(),
  parseAgUiStateSnapshot: vi.fn()
}));

vi.mock('../api/ai-model-configs', () => ({
  listAiModelConfigs: vi.fn()
}));

const getRunMock = vi.mocked(getAiAgentRun);
const streamMock = vi.mocked(streamAiAgentRunEvents);
const listModelsMock = vi.mocked(listAiModelConfigs);

const wrappers: VueWrapper[] = [];
const createRunMock = vi.mocked(createAiAgentRun);
function mountView(allowCreate = false) {
  const pinia = createPinia();
  setActivePinia(pinia);
  if (allowCreate) vi.spyOn(useSessionStore(), 'can').mockReturnValue(true);
  const wrapper = mount(AiAgentRunsView, { global: { plugins: [pinia], stubs: { AiPromptPanel: true, AiAgentReconciliationPanel: { props: ['runId'], template: '<div data-testid="test-reconciliation">{{ runId }}</div>' } } } });
  wrappers.push(wrapper); return wrapper;
}

describe('AiAgentRunsView', () => {
  afterEach(() => {
    wrappers.splice(0).forEach(wrapper => wrapper.unmount());
    vi.restoreAllMocks();
  });

  beforeEach(() => {
    vi.resetAllMocks();
    listModelsMock.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
    streamMock.mockResolvedValue(undefined);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await flushPromises();
    expect(wrapper.find('[data-testid="ai-agent-runs-create"]').exists()).toBe(false);
  });

  it('loads run and renders status after stream completes', async () => {
    getRunMock.mockResolvedValue({
      id: '11111111-1111-1111-1111-111111111111',
      statusKey: 'completed',
      definitionKey: 'fullnet-single-text-v1',
      definitionVersion: 1,
      deadlineAtUtc: '2026-09-08T01:00:00Z',
      createdAtUtc: '2026-09-08T00:00:00Z',
      updatedAtUtc: '2026-09-08T00:05:00Z'
    });
    streamMock.mockImplementation(async (_runId, handlers) => {
      handlers.onEvent({
        eventType: 'RUN_STARTED',
        payload: { type: 'RUN_STARTED', threadId: 'thread', runId: 'run' }
      });
    });

    const wrapper = mountView();
    await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue('11111111-1111-1111-1111-111111111111');
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click');
    await flushPromises();

    expect(getRunMock).toHaveBeenCalled();
    expect(streamMock).toHaveBeenCalled();
    expect(wrapper.text()).toContain('completed');
  });
  it('discards delayed run read and clears run state when context changes', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof getAiAgentRun>>) => void;
    getRunMock.mockReturnValue(new Promise(r => { resolve = r; }));
    const wrapper = mountView();
    await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue('11111111-1111-1111-1111-111111111111');
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click');
    useSessionStore().currentContextName = 'Tenant B';
    resolve({ id: '11111111-1111-1111-1111-111111111111', statusKey: 'completed', definitionKey: 'fullnet-single-text-v1', definitionVersion: 1, deadlineAtUtc: 'now', createdAtUtc: 'now', updatedAtUtc: 'now' });
    await flushPromises();
    expect(streamMock).not.toHaveBeenCalled();
    expect((wrapper.get('[data-testid="ai-agent-runs-id"]').element as HTMLInputElement).value).toBe('');
    expect(wrapper.text()).not.toContain('completed'); wrapper.unmount();
  });

  async function fillCreate(wrapper: VueWrapper, prompt = 'First request') {
    await flushPromises();
    await wrapper.findAllComponents({ name: 'ElInput' })[1]!.setValue('22222222-2222-2222-2222-222222222222');
    await wrapper.get('[data-testid="ai-agent-runs-create-prompt"]').setValue(prompt);
  }
  async function create(wrapper: VueWrapper) {
    await wrapper.get('[data-testid="ai-agent-runs-create"]').trigger('click'); await flushPromises();
  }
  const run = { id: '11111111-1111-1111-1111-111111111111', statusKey: 'completed', definitionKey: 'fullnet-single-text-v1', definitionVersion: 1, deadlineAtUtc: 'now', createdAtUtc: 'now', updatedAtUtc: 'now' };

  it.each([new Error('network'), { code: 'temporary', title: 'Temporary', status: 503, type: 'about:blank' }])('创建结果不明时，同一有效载荷重试保持请求身份：%s', async failure => {
    createRunMock.mockRejectedValue(failure);
    const wrapper = mountView(true); await fillCreate(wrapper); await create(wrapper); await create(wrapper);
    expect(createRunMock).toHaveBeenCalledTimes(2);
    const first = createRunMock.mock.calls[0]![0]; const second = createRunMock.mock.calls[1]![0];
    expect(first.clientRequestId).toMatch(/^[0-9a-f-]{36}$/i); expect(second).toEqual(first);
  });
  it('仅改变首尾空白后重试仍保持规范载荷的请求身份', async () => {
    createRunMock.mockRejectedValue(new Error('network'));
    const wrapper = mountView(true); await fillCreate(wrapper); await create(wrapper);
    await wrapper.get('[data-testid="ai-agent-runs-create-prompt"]').setValue('  First request  '); await create(wrapper);
    expect(createRunMock.mock.calls[1]![0]).toEqual(createRunMock.mock.calls[0]![0]);
  });
  it('正文改变后创建新的请求身份', async () => {
    createRunMock.mockRejectedValue(new Error('network'));
    const wrapper = mountView(true); await fillCreate(wrapper); await create(wrapper);
    await wrapper.get('[data-testid="ai-agent-runs-create-prompt"]').setValue('Second request'); await create(wrapper);
    expect(createRunMock.mock.calls[1]![0].clientRequestId).not.toBe(createRunMock.mock.calls[0]![0].clientRequestId);
    expect(createRunMock.mock.calls[1]![0].prompt).toBe('Second request');
  });
  it.each(['model', 'definition'])('更改 %s 后创建新的请求身份', async field => {
    createRunMock.mockRejectedValue(new Error('network'));
    const wrapper = mountView(true); await fillCreate(wrapper); await create(wrapper);
    if (field === 'model') await wrapper.findAllComponents({ name: 'ElInput' })[1]!.setValue('44444444-4444-4444-4444-444444444444');
    else wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'fullnet-readonly-tool-loop-v1');
    await create(wrapper);
    expect(createRunMock.mock.calls[1]![0].clientRequestId).not.toBe(createRunMock.mock.calls[0]![0].clientRequestId);
    expect(createRunMock.mock.calls[1]![0][field === 'model' ? 'modelConfigId' : 'definitionKey']).not.toBe(createRunMock.mock.calls[0]![0][field === 'model' ? 'modelConfigId' : 'definitionKey']);
  });
  it('已知创建成功后，相同表单可以显式创建新的运行', async () => {
    createRunMock.mockResolvedValue({ runId: run.id }); getRunMock.mockResolvedValue(run);
    const wrapper = mountView(true); await fillCreate(wrapper); await create(wrapper); await create(wrapper);
    expect(createRunMock).toHaveBeenCalledTimes(2);
    expect(createRunMock.mock.calls[1]![0].clientRequestId).not.toBe(createRunMock.mock.calls[0]![0].clientRequestId);
  });
  it('上下文切换清除旧创建身份，旧响应不能接管新上下文重试', async () => {
    let finish!: (value: Awaited<ReturnType<typeof createAiAgentRun>>) => void;
    createRunMock.mockReturnValueOnce(new Promise(resolve => { finish = resolve; })).mockRejectedValue(new Error('network'));
    const wrapper = mountView(true); await fillCreate(wrapper); await wrapper.get('[data-testid="ai-agent-runs-create"]').trigger('click');
    const oldSignal = createRunMock.mock.calls[0]![1]!; useSessionStore().currentContextName = 'Tenant B'; await flushPromises();
    expect(oldSignal.aborted).toBe(true); await fillCreate(wrapper); await create(wrapper);
    const currentRequest = createRunMock.mock.calls[1]![0]; expect(currentRequest.clientRequestId).not.toBe(createRunMock.mock.calls[0]![0].clientRequestId);
    finish({ runId: run.id }); await flushPromises(); expect(getRunMock).not.toHaveBeenCalled();
    await create(wrapper); expect(createRunMock.mock.calls[2]![0]).toEqual(currentRequest);
  });
  it('开始读取另一运行时立即清除旧运行的对账面板', async () => {
    getRunMock.mockResolvedValue({ ...run, statusKey: 'reconciliation_required' });
    const wrapper = mountView(); await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue(run.id);
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="test-reconciliation"]').text()).toBe(run.id);
    getRunMock.mockReturnValue(new Promise(() => {}));
    await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue('33333333-3333-3333-3333-333333333333');
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="test-reconciliation"]').exists()).toBe(false);
  });
});
