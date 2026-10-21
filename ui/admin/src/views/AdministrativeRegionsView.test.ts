import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import AdministrativeRegionsView from './AdministrativeRegionsView.vue';
import { useSessionStore } from '../auth/session';
import { listAdministrativeRegionChildren, getLatestAdministrativeRegionDatasetManifest } from '../api/administrative-regions';

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

const treeMock = vi.mocked(listAdministrativeRegionChildren);
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
      hasChildren: true
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

  it('loads only roots initially and children on expansion', async () => {
    const wrapper = mountWithPermissions(['regions.administrative_regions.read']);
    await flushPromises();
    const initialCalls = treeMock.mock.calls.length;
    expect(initialCalls).toBeGreaterThanOrEqual(1);
    expect(treeMock.mock.calls.every(call => call[0] === undefined)).toBe(true);
    expect(treeMock.mock.calls[0]?.[0]).toBeUndefined();
    treeMock.mockResolvedValue([]);
    await wrapper.find('.el-table__expand-icon').trigger('click');
    await flushPromises();
    expect(treeMock).toHaveBeenCalledTimes(initialCalls + 1);
    expect(treeMock.mock.calls[initialCalls]?.[0]).toBe('01912345-6789-7abc-8def-0123456789ab');
    expect(wrapper.findComponent({ name: 'ElTable' }).props('lazy')).toBe(true);
    wrapper.unmount();
    expect(treeMock.mock.calls.some(call => call[1]?.aborted)).toBe(true);
  });

  it('discards children from an earlier refresh generation', async () => {
    const wrapper = mountWithPermissions(['regions.administrative_regions.read']);
    await flushPromises();
    let complete!: (rows: Awaited<ReturnType<typeof listAdministrativeRegionChildren>>) => void;
    treeMock.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const table = wrapper.findComponent({ name: 'ElTable' });
    const resolve = vi.fn();
    const load = table.props('load') as (row: { id: string }, node: unknown, resolve: (rows: unknown[]) => void) => Promise<void>;
    const pending = load({ id: 'old-parent' }, {}, resolve);
    const signal = treeMock.mock.calls.at(-1)?.[1];
    wrapper.findComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh');
    await flushPromises();
    expect(signal?.aborted).toBe(true);
    complete([{ id: 'late', parentId: 'old-parent', code: 'late', name: '过期节点', level: 2, displayOrder: 1, hasChildren: false }]);
    await pending;
    expect(resolve).toHaveBeenCalledWith([]);
    expect(wrapper.text()).not.toContain('过期节点');
    wrapper.unmount();
  });
});
