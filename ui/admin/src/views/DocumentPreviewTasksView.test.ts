import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentPreviewTasksView from './DocumentPreviewTasksView.vue';
import { useSessionStore } from '../auth/session';
import { getDocumentPreviewCapabilities, cancelDocumentPreviewTask, retryDocumentPreviewTask, listDocumentPreviewTasks } from '../api/document-preview-tasks';

vi.mock('../api/document-preview-tasks', () => ({
  getDocumentPreviewCapabilities: vi.fn().mockResolvedValue({ enabled: true, allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800 }),
  cancelDocumentPreviewTask: vi.fn(), retryDocumentPreviewTask: vi.fn(),
  listDocumentPreviewTasks: vi.fn(),
  createDocumentPreviewTask: vi.fn(),
  openDocumentPreviewTaskContent: vi.fn()
}));

const listMock = vi.mocked(listDocumentPreviewTasks);
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.useRealTimers(); vi.restoreAllMocks(); });

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  const wrapper = mount(DocumentPreviewTasksView, { global: { plugins: [pinia], directives: { loading: () => {} }, stubs: {
    ElDialog: { props: ['modelValue', 'title'], template: '<aside v-if="modelValue"><h2>{{ title }}</h2><slot/><slot name="footer"/></aside>' }
  } } }); wrappers.push(wrapper); return wrapper;
}

describe('Vue 文档预览任务页', () => {
  beforeEach(() => {
    vi.mocked(cancelDocumentPreviewTask).mockReset().mockResolvedValue({} as never);
    vi.mocked(retryDocumentPreviewTask).mockReset().mockResolvedValue({} as never);
    vi.mocked(getDocumentPreviewCapabilities).mockResolvedValue({ enabled: true, allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800, maxAttempts: 3, providerKey: 'external_http' });
    listMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
        documentItemId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
        documentTitle: 'Quarterly report',
        versionId: null,
        sourceFileId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
        outputFileId: null,
        attemptCount: 0, statusKey: 'pending',
        providerKey: 'disabled',
        errorCode: null,
        requestedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
        createdAtUtc: '2026-09-06T12:00:00Z',
        startedAtUtc: null,
        completedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('仅有 read 时不显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-task-create"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Quarterly report');
  });

  it('create 权限显示提交按钮', async () => {
    const wrapper = mountWithPermissions([
      'document.host_preview_tasks.read',
      'document.host_preview_tasks.create'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-task-create"]').exists()).toBe(true);
  });
  it('转换关闭时隐藏新增和重试，但保留现有任务与取消入口', async () => {
    vi.mocked(getDocumentPreviewCapabilities).mockResolvedValue({ enabled: false, allowedMimeTypes: [], maxInputBytes: 1, maxAttempts: 3, providerKey: 'disabled' });
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.create', 'document.host_preview_tasks.cancel', 'document.host_preview_tasks.retry']);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-disabled"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="document-preview-task-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="document-preview-task-retry"]').exists()).toBe(false);
    await wrapper.find('[data-testid="document-preview-task-cancel"]').trigger('click'); await flushPromises();
    expect(cancelDocumentPreviewTask).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('确认取消预览转换');
    await wrapper.get('[data-testid="document-preview-confirm-cancel"]').trigger('click'); await flushPromises();
    expect(cancelDocumentPreviewTask).toHaveBeenCalledWith(expect.objectContaining({ version: 1, statusKey: 'pending' }), expect.any(AbortSignal));
  });
  it('有精确重试权限的终态任务允许新建重试', async () => {
    const page = await listMock(); page.items[0]!.statusKey = 'failed'; page.items[0]!.attemptCount = 3;
    listMock.mockResolvedValue(page);
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.retry']); await flushPromises();
    await wrapper.find('[data-testid="document-preview-task-retry"]').trigger('click'); await flushPromises();
    expect(retryDocumentPreviewTask).toHaveBeenCalledWith(page.items[0]!.id, expect.any(AbortSignal));
    expect(wrapper.find('[data-testid="document-preview-task-cancel"]').exists()).toBe(false);
  });

  it('处理中的预览仍可按服务端版本取消，确认固定点击时快照', async () => {
    const page = await listMock(); page.items[0]!.statusKey = 'processing'; page.items[0]!.version = 8; listMock.mockResolvedValue(page);
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="document-preview-task-cancel"]').trigger('click');
    expect(wrapper.text()).toContain('临时文件由后台释放');
    page.items[0]!.version = 9;
    await wrapper.get('[data-testid="document-preview-confirm-cancel"]').trigger('click'); await flushPromises();
    expect(cancelDocumentPreviewTask).toHaveBeenCalledWith(expect.objectContaining({ version: 8 }), expect.any(AbortSignal));
  });
  it('未知写入结果暂停后续操作和自动刷新，只允许显式重读', async () => {
    vi.useFakeTimers(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    vi.mocked(cancelDocumentPreviewTask).mockRejectedValue(new Error('Response lost'));
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await wrapper.get('[data-testid="document-preview-confirm-cancel"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-unconfirmed"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="document-preview-task-cancel"]').attributes('disabled')).toBeDefined();
    await vi.advanceTimersByTimeAsync(30_000); expect(cancelDocumentPreviewTask).toHaveBeenCalledTimes(1); expect(listMock).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="document-preview-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="document-preview-unconfirmed"]').exists()).toBe(false);
  });
  it('切换到租户会中止请求，迟到的 Host 数据不可重新显示', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listMock>>) => void;
    const result = await listMock(); listMock.mockClear().mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read']); const signal = listMock.mock.calls[0]![3]!;
    const session = useSessionStore(); session.currentUser = { ...session.currentUser!, tenantId: 'other', actorScope: 'tenant', scope: 'tenant' }; await flushPromises();
    expect(signal.aborted).toBe(true); resolve(result); await flushPromises(); expect(wrapper.text()).not.toContain('Quarterly report');
  });
  it('撤权清空列表与打开的确认，不再发出写入', async () => {
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="document-preview-task-cancel"]').trigger('click');
    useSessionStore().currentUser!.permissions = []; await flushPromises();
    expect(wrapper.text()).not.toContain('Quarterly report'); expect(wrapper.find('[data-testid="document-preview-confirm-cancel"]').exists()).toBe(false);
    expect(cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('仅轮询非终态，成功后停止刷新且不重放写操作', async () => {
    vi.useFakeTimers(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read']); await flushPromises();
    const result = structuredClone(await listMock.mock.results[0]!.value); result.items[0]!.statusKey = 'succeeded'; listMock.mockResolvedValue(result);
    await vi.advanceTimersByTimeAsync(5_000); await flushPromises(); expect(wrapper.text()).toContain('成功'); const count = listMock.mock.calls.length;
    await vi.advanceTimersByTimeAsync(30_000); expect(listMock).toHaveBeenCalledTimes(count); expect(cancelDocumentPreviewTask).not.toHaveBeenCalled();
  });
  it('自动读取失败暂停，手动刷新后才恢复轮询', async () => {
    vi.useFakeTimers(); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read']); await flushPromises(); const original = await listMock.mock.results[0]!.value;
    listMock.mockRejectedValueOnce(new Error('Unavailable')); await vi.advanceTimersByTimeAsync(5_000); await flushPromises();
    expect(wrapper.text()).toContain('自动刷新已暂停'); const count = listMock.mock.calls.length; await vi.advanceTimersByTimeAsync(30_000); expect(listMock).toHaveBeenCalledTimes(count);
    listMock.mockResolvedValue(original); await wrapper.get('[data-testid="document-preview-refresh"]').trigger('click'); await flushPromises();
    await vi.advanceTimersByTimeAsync(5_000); expect(listMock.mock.calls.length).toBe(count + 2);
  });
  it('身份失效后取消响应不能触发刷新或恢复旧列表', async () => {
    let resolve!: (value: never) => void; vi.mocked(cancelDocumentPreviewTask).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await flushPromises();
    await wrapper.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await wrapper.get('[data-testid="document-preview-confirm-cancel"]').trigger('click');
    const signal = vi.mocked(cancelDocumentPreviewTask).mock.calls[0]![1]!; useSessionStore().currentUser = undefined; await flushPromises();
    expect(signal.aborted).toBe(true); resolve({} as never); await flushPromises(); expect(listMock).toHaveBeenCalledTimes(1); expect(wrapper.text()).not.toContain('Quarterly report');
  });
  it('开始取消后，先前读取的迟到响应不能解除未知结果锁定', async () => {
    const wrapper = mountWithPermissions(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await flushPromises();
    const original = await listMock.mock.results[0]!.value; let resolve!: (value: typeof original) => void;
    listMock.mockImplementationOnce(() => new Promise(done => { resolve = done; })); await wrapper.get('[data-testid="document-preview-refresh"]').trigger('click');
    const oldSignal = listMock.mock.calls[1]![3]!; vi.mocked(cancelDocumentPreviewTask).mockRejectedValue(new Error('Response lost'));
    await wrapper.get('[data-testid="document-preview-task-cancel"]').trigger('click'); await wrapper.get('[data-testid="document-preview-confirm-cancel"]').trigger('click'); await flushPromises();
    expect(oldSignal.aborted).toBe(true); resolve(original); await flushPromises(); expect(wrapper.find('[data-testid="document-preview-unconfirmed"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="document-preview-task-cancel"]').attributes('disabled')).toBeDefined();
  });
});
