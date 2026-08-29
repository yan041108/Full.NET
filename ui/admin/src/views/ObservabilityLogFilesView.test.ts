import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ObservabilityLogFilesView from './ObservabilityLogFilesView.vue';
import { useSessionStore } from '../auth/session';
import {
  downloadObservabilityLogFile,
  listObservabilityLogFiles,
  tailObservabilityLogFile
} from '../api/observability-log-files';

vi.mock('../api/observability-log-files', () => ({
  downloadObservabilityLogFile: vi.fn(),
  listObservabilityLogFiles: vi.fn(),
  tailObservabilityLogFile: vi.fn()
}));

const listMock = vi.mocked(listObservabilityLogFiles);
const tailMock = vi.mocked(tailObservabilityLogFile);
const downloadMock = vi.mocked(downloadObservabilityLogFile);

function mountView(permissions: string[]) {
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
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ObservabilityLogFilesView, { global: { plugins: [pinia] } });
}

describe('运行日志控制面', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue([{
      id: 'a'.repeat(64),
      fileName: 'api.log',
      sizeBytes: 128,
      lastModifiedUtc: '2026-08-30T00:00:00Z'
    }]);
    tailMock.mockReset().mockResolvedValue({
      id: 'a'.repeat(64),
      fileName: 'api.log',
      content: 'first\nsecond',
      bytesRead: 12,
      isTruncated: false
    });
    downloadMock.mockReset().mockResolvedValue(new Blob(['log']));
  });

  it('读取权限会加载有界文件列表并自动查看首个文件尾部', async () => {
    const wrapper = mountView(['observability.log_files.read']);
    await flushPromises();

    expect(listMock).toHaveBeenCalledTimes(1);
    expect(tailMock).toHaveBeenCalledWith('a'.repeat(64), 200, 262144);
    expect(wrapper.text()).toContain('api.log');
    expect(wrapper.text()).toContain('second');
    expect(wrapper.find('[data-testid="observability-log-download"]').exists()).toBe(false);
  });

  it('下载按钮由独立权限控制并使用稳定文件标识', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:log');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const wrapper = mountView([
      'observability.log_files.read',
      'observability.log_files.download'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="observability-log-download"]').trigger('click');
    await flushPromises();
    expect(downloadMock).toHaveBeenCalledWith('a'.repeat(64));
  });
});
