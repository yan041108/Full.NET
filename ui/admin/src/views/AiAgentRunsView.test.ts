import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiAgentRunsView from './AiAgentRunsView.vue';
import { ElMessage } from 'element-plus';
import { cancelAiAgentRun, resumeAiAgentRun, createAiAgentRun, getAiAgentRun, streamAiAgentRunEvents } from '../api/ai-agent-runs';
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

  const operations = [['cancel', 'running'], ['resume', 'awaiting_approval']] as const;
  const otherRunId = '55555555-5555-5555-5555-555555555555';
  async function readRun(wrapper: VueWrapper, id = run.id) {
    await wrapper.get('[data-testid="ai-agent-runs-id"]').setValue(id);
    await wrapper.get('[data-testid="ai-agent-runs-load"]').trigger('click'); await flushPromises();
  }
  it.each(operations)('%s 的成功响应不能切回已离开的运行', async (action, state) => {
    let finish!: (value: boolean) => void; const mutation = vi.mocked(action === 'cancel' ? cancelAiAgentRun : resumeAiAgentRun);
    mutation.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    getRunMock.mockImplementation(async id => ({ ...run, id, statusKey: id === run.id ? state : 'completed' }));
    const wrapper = mountView(true); await readRun(wrapper); await wrapper.get(`[data-testid="ai-agent-runs-${action}"]`).trigger('click');
    await readRun(wrapper, otherRunId); const readCount = getRunMock.mock.calls.length;
    finish(true); await flushPromises();
    expect(getRunMock).toHaveBeenCalledTimes(readCount);
    expect(wrapper.get('[data-testid="ai-agent-runs-id"]').element).toHaveProperty('value', otherRunId);
  });
  it.each(operations)('%s 的迟到错误不能污染另一个运行', async (action, state) => {
    let fail!: (value: Error) => void; const mutation = vi.mocked(action === 'cancel' ? cancelAiAgentRun : resumeAiAgentRun);
    mutation.mockReturnValue(new Promise((_resolve, reject) => { fail = reject; }));
    getRunMock.mockImplementation(async id => ({ ...run, id, statusKey: id === run.id ? state : 'completed' }));
    const wrapper = mountView(true); await readRun(wrapper); await wrapper.get(`[data-testid="ai-agent-runs-${action}"]`).trigger('click');
    await readRun(wrapper, otherRunId); fail(new Error('late')); await flushPromises();
    expect(wrapper.find('.art-page-alert').exists()).toBe(false);
    expect(wrapper.get('[data-testid="ai-agent-runs-id"]').element).toHaveProperty('value', otherRunId);
  });
  it.each(operations)('%s 等待期间只允许一次派发', async (action, state) => {
    const mutation = vi.mocked(action === 'cancel' ? cancelAiAgentRun : resumeAiAgentRun); mutation.mockReturnValue(new Promise(() => {}));
    getRunMock.mockResolvedValue({ ...run, statusKey: state }); const wrapper = mountView(true); await readRun(wrapper);
    const button = wrapper.get(`[data-testid="ai-agent-runs-${action}"]`); await button.trigger('click'); await button.trigger('click'); await flushPromises();
    expect(mutation).toHaveBeenCalledTimes(1); expect(button.attributes('disabled')).toBeDefined();
  });
  it.each(operations)('%s 的明确拒绝回执不能提示成功或刷新运行', async (action, state) => {
    const success = vi.spyOn(ElMessage, 'success'); const mutation = vi.mocked(action === 'cancel' ? cancelAiAgentRun : resumeAiAgentRun); mutation.mockResolvedValue(false);
    getRunMock.mockResolvedValue({ ...run, statusKey: state }); const wrapper = mountView(true); await readRun(wrapper); const readCount = getRunMock.mock.calls.length;
    await wrapper.get(`[data-testid="ai-agent-runs-${action}"]`).trigger('click'); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(getRunMock).toHaveBeenCalledTimes(readCount); expect(wrapper.find('.art-page-alert').exists()).toBe(true);
  });
  it('旧上下文取消结束时不能释放新上下文的等待状态', async () => {
    let oldFinish!: (value: boolean) => void; let newFinish!: (value: boolean) => void;
    vi.mocked(cancelAiAgentRun).mockReturnValueOnce(new Promise(resolve => { oldFinish = resolve; })).mockReturnValueOnce(new Promise(resolve => { newFinish = resolve; }));
    getRunMock.mockImplementation(async id => ({ ...run, id, statusKey: 'running' })); const wrapper = mountView(true); await readRun(wrapper);
    await wrapper.get('[data-testid="ai-agent-runs-cancel"]').trigger('click'); const oldSignal = vi.mocked(cancelAiAgentRun).mock.calls[0]![1]!;
    useSessionStore().currentContextName = 'Tenant B'; await flushPromises(); expect(oldSignal.aborted).toBe(true); await readRun(wrapper, otherRunId);
    await wrapper.get('[data-testid="ai-agent-runs-cancel"]').trigger('click'); oldFinish(true); await flushPromises();
    expect(wrapper.get('[data-testid="ai-agent-runs-cancel"]').attributes('disabled')).toBeDefined();
    expect(vi.mocked(cancelAiAgentRun)).toHaveBeenCalledTimes(2); newFinish(true); await flushPromises();
    expect(wrapper.get('[data-testid="ai-agent-runs-cancel"]').attributes('disabled')).toBeUndefined();
  });
  it('初次读取返回另一运行时拒绝显示和启动事件流', async () => {
    getRunMock.mockResolvedValue({ ...run, id: otherRunId, statusKey: 'reconciliation_required' }); const wrapper = mountView(); await readRun(wrapper);
    expect(streamMock).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="test-reconciliation"]').exists()).toBe(false); expect(wrapper.find('.art-page-alert').exists()).toBe(true);
  });
  it('事件流结束的错运行刷新不能替换已核对的运行', async () => {
    getRunMock.mockResolvedValueOnce(run).mockResolvedValueOnce({ ...run, id: otherRunId, statusKey: 'reconciliation_required' });
    const wrapper = mountView(); await readRun(wrapper); expect(getRunMock).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="test-reconciliation"]').exists()).toBe(false); expect(wrapper.text()).toContain('completed');
  });
  it('运行标识仅大小写不同仍可读取和显示', async () => {
    const target = 'abcdefab-abcd-7abc-8abc-abcdefabcdef'; getRunMock.mockResolvedValue({ ...run, id: target });
    const wrapper = mountView(); await readRun(wrapper, target.toUpperCase()); expect(streamMock).toHaveBeenCalledWith(target, expect.any(Object), expect.any(AbortSignal)); expect(wrapper.text()).toContain('completed');
  });

  it('恢复已经确认且事件流持续打开时仍允许取消新运行', async () => {
    vi.mocked(resumeAiAgentRun).mockResolvedValue(true); getRunMock.mockResolvedValue({ ...run, statusKey: 'awaiting_approval' });
    const wrapper = mountView(true); await readRun(wrapper);
    getRunMock.mockResolvedValue({ ...run, statusKey: 'running' }); streamMock.mockReturnValue(new Promise(() => {}));
    await wrapper.get('[data-testid="ai-agent-runs-resume"]').trigger('click'); await flushPromises();
    const cancel = wrapper.get('[data-testid="ai-agent-runs-cancel"]'); expect(cancel.attributes('disabled')).toBeUndefined();
    vi.mocked(cancelAiAgentRun).mockResolvedValue(false); await cancel.trigger('click'); await flushPromises();
    expect(cancelAiAgentRun).toHaveBeenCalledTimes(1);
  });
});
