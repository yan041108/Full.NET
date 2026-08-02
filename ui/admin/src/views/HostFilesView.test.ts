import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import HostFilesView from './HostFilesView.vue';
import { useSessionStore } from '../auth/session';
import {
  deleteHostFile,
  downloadHostFileContent,
  listHostFiles,
  openHostFileBlob,
  uploadHostFile
} from '../api/host-files';

vi.mock('../api/host-files', () => ({
  deleteHostFile: vi.fn(),
  downloadHostFileContent: vi.fn(),
  listHostFiles: vi.fn(),
  openHostFileBlob: vi.fn(),
  uploadHostFile: vi.fn()
}));

const listMock = vi.mocked(listHostFiles);
const uploadMock = vi.mocked(uploadHostFile);
const deleteMock = vi.mocked(deleteHostFile);
const downloadMock = vi.mocked(downloadHostFileContent);
const openBlobMock = vi.mocked(openHostFileBlob);

const sampleFile = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  originalFileName: 'parity.txt',
  contentType: 'text/plain',
  sizeBytes: 12,
  contentHash: 'a'.repeat(64),
  createdAtUtc: '2026-07-26T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '\u7ba1\u7406\u5458',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(HostFilesView, { global: { plugins: [pinia] } });
}

describe('Vue Host \u6587\u4ef6\u7ba1\u7406\u9875', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [sampleFile],
      page: 1,
      pageSize: 20,
      total: 1
    });
    uploadMock.mockReset();
    deleteMock.mockReset();
    downloadMock.mockReset().mockResolvedValue(new Blob(['hello'], { type: 'text/plain' }));
    openBlobMock.mockReset();
  });

  it('\u4ec5\u6709 read \u65f6\u4e0d\u663e\u793a\u4e0a\u4f20\u3001\u4e0b\u8f7d\u4e0e\u5220\u9664\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions(['files.files.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-files-upload"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-files-download"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-files-delete"]').exists()).toBe(false);
  });

  it('download-only \u53ea\u663e\u793a\u4e0b\u8f7d\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions(['files.files.read', 'files.files.download']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-files-upload"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-files-download"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="host-files-delete"]').exists()).toBe(false);
  });

  it('delete-only \u53ea\u663e\u793a\u5220\u9664\u6309\u94ae', async () => {
    const wrapper = mountWithPermissions(['files.files.read', 'files.files.delete']);
    await flushPromises();

    expect(wrapper.find('[data-testid="host-files-upload"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-files-download"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="host-files-delete"]').exists()).toBe(true);
  });

  it('\u4e0b\u8f7d\u4f7f\u7528\u8ba4\u8bc1 Blob \u5ba2\u6237\u7aef\u5e76\u6253\u5f00\u77ed\u751f\u547d\u5468\u671f URL', async () => {
    const wrapper = mountWithPermissions(['files.files.read', 'files.files.download']);
    await flushPromises();

    await wrapper.get('[data-testid="host-files-download"]').trigger('click');
    await flushPromises();

    expect(downloadMock).toHaveBeenCalledWith(sampleFile.id);
    expect(openBlobMock).toHaveBeenCalled();
  });
});