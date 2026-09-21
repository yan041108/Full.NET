import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import AdministrativeRegionsView from './AdministrativeRegionsView.vue';
import { useSessionStore } from '../auth/session';
import { getAdministrativeRegionTree, getLatestAdministrativeRegionDatasetManifest } from '../api/administrative-regions';

vi.mock('../api/administrative-regions', () => ({
  applyAdministrativeRegionImport: vi.fn(),
  createAdministrativeRegion: vi.fn(),
  deleteAdministrativeRegion: vi.fn(),
  getAdministrativeRegion: vi.fn(),
  getAdministrativeRegionTree: vi.fn(),
  getLatestAdministrativeRegionDatasetManifest: vi.fn(),
  listAdministrativeRegionChildren: vi.fn(),
  previewAdministrativeRegionImport: vi.fn(),
  updateAdministrativeRegion: vi.fn()
}));

const treeMock = vi.mocked(getAdministrativeRegionTree);
const manifestMock = vi.mocked(getLatestAdministrativeRegionDatasetManifest);

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
  treeMock.mockResolvedValue([
    {
      id: '01912345-6789-7abc-8def-0123456789ab',
      parentId: null,
      code: '110000',
      name: '北京市',
      level: 1,
      displayOrder: 10,
      children: []
    }
  ]);
  return mount(AdministrativeRegionsView, { global: { plugins: [pinia] } });
}

describe('AdministrativeRegionsView', () => {
  beforeEach(() => {
    treeMock.mockReset();
    manifestMock.mockResolvedValue(null);
  });

  it('hides mutating actions without permissions', async () => {
    const wrapper = mountWithPermissions(['regions.administrative_regions.manage']);
    await flushPromises();

    expect(wrapper.find('[data-testid="administrative-regions-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="administrative-regions-edit"]').exists()).toBe(false);
  });

  it('shows create action when create permission is granted', async () => {
    const wrapper = mountWithPermissions([
      'regions.administrative_regions.manage',
      'regions.administrative_regions.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="administrative-regions-action-create"]').exists()).toBe(true);
  });
});
