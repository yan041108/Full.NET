import { describe, expect, it } from 'vitest';
import { createPinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import { createAppRouter } from './index';

describe('Vue 管理端路由拆包', () => {
  it('包括概览的所有后台页面均按路由加载，匿名登录不预载概览', () => {
    const router = createAppRouter(createMemoryHistory(), createPinia());
    const routes = router.getRoutes();

    for (const route of routes) {
      if (!route.components?.default) {
        continue;
      }

      expect(
        typeof route.components.default,
        `路由 ${route.path} 必须延迟加载`
      ).toBe('function');
    }

    const overview = routes.find(route =>
      route.name === 'overview'
    );
    expect(typeof overview?.components?.default).toBe('function');
  });
});
