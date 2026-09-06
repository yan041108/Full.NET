import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import PrintingPreviewView from './PrintingPreviewView.vue';
import { useSessionStore } from '../auth/session';
import { listPrintingTemplates } from '../api/printing-templates';

vi.mock('../api/printing-templates', () => ({
  listPrintingTemplates: vi.fn(),
  createPrintingTemplate: vi.fn(),
  publishPrintingTemplate: vi.fn(),
  previewPrintingTemplate: vi.fn()
}));

const listMock = vi.mocked(listPrintingTemplates);

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(PrintingPreviewView, { global: { plugins: [pinia] } });
}

describe('PrintingPreviewView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue([]);
  });

  it('shows preview action entry point', async () => {
    const wrapper = mountWithPermissions(['printing.templates.preview']);
    await flushPromises();
    expect(wrapper.find('[data-testid="printing-preview-run"]').exists()).toBe(true);
  });
});
