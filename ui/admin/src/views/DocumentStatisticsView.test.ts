import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentStatisticsView from './DocumentStatisticsView.vue';
import { listDocumentAccessLogs } from '../api/document-access-logs';
import { getDocumentStatistics } from '../api/document-statistics';
import { useSessionStore } from '../auth/session';

vi.mock('../api/document-statistics', () => ({
  getDocumentStatistics: vi.fn()
}));

vi.mock('../api/document-access-logs', () => ({
  listDocumentAccessLogs: vi.fn()
}));

const statsMock = vi.mocked(getDocumentStatistics);
const accessLogsMock = vi.mocked(listDocumentAccessLogs);

describe('Vue 文档统计页', () => {
  function mountWithPermissions(permissions: string[]) {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.currentUser = {
      id: '01912345-6789-7abc-8def-0123456789ae',
      username: 'doc-admin',
      displayName: '文档管理员',
      tenantId: '01912345-6789-7abc-8def-0123456789aa',
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: false,
    passwordChangeRequired: false,
      permissions,
      sessionId: '01912345-6789-7abc-8def-0123456789a1',
      preferredLocale: 'zh-CN',
      profileVersion: 1
    };
    return mount(DocumentStatisticsView, { global: { plugins: [pinia] } });
  }

  beforeEach(() => {
    statsMock.mockReset().mockResolvedValue({
      summary: {
        totalItems: 3,
        totalVersions: 5,
        totalSizeKb: 1024,
        totalSizeInfo: '1 MB'
      },
      byType: [
        { extension: 'pdf', count: 2, totalSizeKb: 512 },
        { extension: 'docx', count: 1, totalSizeKb: 512 }
      ],
      byCategory: [],
      shareCount: 1,
      todayAccessCount: 0,
      todayDownloadCount: 0,
      todayCreatedCount: 1,
      recycleBinCount: 0
    });

    accessLogsMock.mockReset().mockResolvedValue({
      items: [
        {
          id: '00000000-0000-0000-0000-000000000001',
          documentItemId: '00000000-0000-0000-0000-000000000002',
          documentTitle: 'Demo',
          accessTypeKey: 'preview',
          sourceKey: 'authenticated',
          actorUserId: '00000000-0000-0000-0000-000000000003',
          occurredAtUtc: '2026-09-06T10:00:00Z',
          clientIpFingerprint: 'abc'
        }
      ],
      page: 1,
      pageSize: 20,
      total: 1
    });
  });

  it('加载成功后展示统计面板与按类型分布', async () => {
    const wrapper = mountWithPermissions([
      'document.host_statistics.read',
      'document.host_access_logs.read'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="document-statistics-panel"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('1 MB');
    expect(wrapper.find('[data-testid="document-statistics-by-type"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('pdf');
  });

  it('切换到访问日志页签后加载分页列表', async () => {
    const wrapper = mountWithPermissions([
      'document.host_statistics.read',
      'document.host_access_logs.read'
    ]);
    await flushPromises();

    const tabs = wrapper.findAll('[data-testid="document-statistics-tabs"] .el-tabs__item');
    await tabs[1].trigger('click');
    await flushPromises();

    expect(accessLogsMock).toHaveBeenCalledWith(1, 20, {});
    expect(wrapper.find('[data-testid="document-access-logs-table"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Demo');
  });
});
