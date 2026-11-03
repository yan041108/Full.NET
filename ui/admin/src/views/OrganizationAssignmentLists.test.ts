import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElPagination } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import { listOrganizationUserUnits, listAssignableOrganizationUserUnitUsers } from '../api/org-user-units';
import { listOrganizationUserPositions, listAssignableOrganizationUserPositionUsers } from '../api/org-user-positions';
import OrgUserUnitsView from './OrgUserUnitsView.vue';
import OrgUserPositionsView from './OrgUserPositionsView.vue';

vi.mock('../api/org-user-units', () => ({
  listOrganizationUserUnits: vi.fn(), listAssignableOrganizationUserUnitUsers: vi.fn(),
  createOrganizationUserUnit: vi.fn(), updateOrganizationUserUnit: vi.fn(), disableOrganizationUserUnit: vi.fn()
}));
vi.mock('../api/org-user-positions', () => ({
  listOrganizationUserPositions: vi.fn(), listAssignableOrganizationUserPositionUsers: vi.fn(),
  createOrganizationUserPosition: vi.fn(), updateOrganizationUserPosition: vi.fn(), disableOrganizationUserPosition: vi.fn()
}));
vi.mock('../api/org-units', () => ({ listOrganizationUnits: vi.fn() }));
vi.mock('../api/org-positions', () => ({ listOrganizationPositions: vi.fn() }));

const sample = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf300', userId: '019bc2b1-2a40-7cc3-8992-a80de51bf301',
  username: 'server-result', displayName: '服务端搜索结果',
  unitId: '019bc2b1-2a40-7cc3-8992-a80de51bf302', unitCode: 'unit', unitName: '机构',
  positionId: '019bc2b1-2a40-7cc3-8992-a80de51bf303', positionCode: 'position', positionName: '职位',
  isPrimary: false, isActive: false, createdAtUtc: '2026-07-21T00:00:00Z', updatedAtUtc: null, version: 1
};
const cases = [
  { entity: 'unit', permission: 'organization.user_units.read', component: OrgUserUnitsView, list: vi.mocked(listOrganizationUserUnits) },
  { entity: 'position', permission: 'organization.user_positions.read', component: OrgUserPositionsView, list: vi.mocked(listOrganizationUserPositions) }
] as const;

for (const target of cases) {
  describe(`${target.entity} 隶属服务端列表`, () => {
    beforeEach(() => {
      vi.resetAllMocks();
      target.list.mockResolvedValue({ items: [sample], page: 1, pageSize: 20, total: 25 });
    });

    function mountView() {
      const pinia = createPinia();
      setActivePinia(pinia);
      useSessionStore().currentUser = {
        id: sample.userId, username: 'reader', displayName: '只读用户', tenantId: sample.unitId,
        actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, passwordChangeRequired: false,
        permissions: [target.permission], sessionId: sample.id, preferredLocale: 'zh-CN', profileVersion: 1
      };
      return mount(target.component, { global: { plugins: [pinia], directives: { loading: () => {} } } });
    }

    it('使用后端总数，翻页重新请求，搜索和重置回到第一页', async () => {
      const wrapper = mountView();
      await flushPromises();
      expect(wrapper.findComponent(ElPagination).props('total')).toBe(25);
      target.list.mockResolvedValueOnce({ items: [sample], page: 2, pageSize: 20, total: 25 });
      await wrapper.find('.el-pagination .btn-next').trigger('click');
      await flushPromises();
      expect(target.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, pageSize: 20 }), expect.any(AbortSignal));
      wrapper.findComponent(ArtSearchBar).vm.$emit('search', { user: ' 用户 ', [target.entity]: ' 目标 ', status: 'inactive' });
      await flushPromises();
      expect(target.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, userSearch: '用户', [`${target.entity}Search`]: '目标', isActive: false }, expect.any(AbortSignal));
      // 服务端已完成筛选；页面不能再次以关键字裁剪响应。
      expect(wrapper.text()).toContain('服务端搜索结果');
      wrapper.findComponent(ArtSearchBar).vm.$emit('reset');
      await flushPromises();
      expect(target.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, userSearch: undefined, [`${target.entity}Search`]: undefined, isActive: undefined }, expect.any(AbortSignal));
      expect(listAssignableOrganizationUserUnitUsers).not.toHaveBeenCalled();
      expect(listAssignableOrganizationUserPositionUsers).not.toHaveBeenCalled();
      expect(wrapper.find('[data-testid$="action-create"]').exists()).toBe(false);
      wrapper.unmount();
    });

    it('修改页大小回到第一页，失败后清除旧列表和总数', async () => {
      const wrapper = mountView();
      await flushPromises();
      target.list.mockResolvedValueOnce({ items: [sample], page: 2, pageSize: 20, total: 25 });
      await wrapper.find('.el-pagination .btn-next').trigger('click');
      await flushPromises();
      target.list.mockRejectedValueOnce({ status: 403, code: 'authorization.permission_denied', title: '没有访问权限' });
      wrapper.findComponent(ElPagination).vm.$emit('update:page-size', 50);
      await flushPromises();
      expect(target.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, pageSize: 50 }), expect.any(AbortSignal));
      expect(wrapper.findComponent(ElPagination).props('total')).toBe(0);
      expect(wrapper.text()).toContain('没有访问权限');
      expect(wrapper.text()).not.toContain('服务端搜索结果');
      wrapper.unmount();
    });

    it('翻页被拒绝后保留错误，不因分页控件收缩自动重试', async () => {
      const wrapper = mountView();
      await flushPromises();
      target.list.mockRejectedValueOnce({ status: 403, code: 'authorization.permission_denied', title: '没有访问权限' });
      await wrapper.find('.el-pagination .btn-next').trigger('click');
      await flushPromises();
      expect(target.list).toHaveBeenCalledTimes(2);
      expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1);
      expect(wrapper.text()).toContain('没有访问权限');
      wrapper.unmount();
    });

    it('迟到的旧请求不能覆盖新筛选，卸载会取消当前请求', async () => {
      let finish!: (value: { items: typeof sample[]; page: number; pageSize: number; total: number }) => void;
      target.list.mockImplementationOnce(() => new Promise<{ items: typeof sample[]; page: number; pageSize: number; total: number }>(resolve => { finish = resolve; }));
      const wrapper = mountView();
      const firstSignal = target.list.mock.calls[0]![1]!;
      wrapper.findComponent(ArtSearchBar).vm.$emit('search', { user: '新筛选' });
      await flushPromises();
      expect(firstSignal.aborted).toBe(true);
      finish({ items: [], page: 1, pageSize: 20, total: 0 });
      await flushPromises();
      expect(wrapper.findComponent(ElPagination).props('total')).toBe(25);
      expect(wrapper.text()).toContain('服务端搜索结果');
      const lastSignal = target.list.mock.calls.at(-1)![1]!;
      wrapper.unmount();
      expect(lastSignal.aborted).toBe(true);
    });
  });
}
