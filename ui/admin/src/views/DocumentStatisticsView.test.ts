import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DocumentStatisticsView from './DocumentStatisticsView.vue';
import { getDocumentStatistics } from '../api/document-statistics';

vi.mock('../api/document-statistics', () => ({
  getDocumentStatistics: vi.fn()
}));

const statsMock = vi.mocked(getDocumentStatistics);

describe('Vue 文档统计页', () => {
  beforeEach(() => {
    statsMock.mockReset().mockResolvedValue({
      summary: {
        totalItems: 3,
        totalVersions: 5,
        totalSizeKb: 1024,
        totalSizeInfo: '1 MB'
      },
      byType: [],
      byCategory: [],
      shareCount: 1,
      todayAccessCount: 0,
      todayDownloadCount: 0,
      todayCreatedCount: 1,
      recycleBinCount: 0
    });
  });

  it('加载成功后展示统计面板', async () => {
    const wrapper = mount(DocumentStatisticsView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();
    expect(wrapper.find('[data-testid="document-statistics-panel"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('1 MB');
  });
});
