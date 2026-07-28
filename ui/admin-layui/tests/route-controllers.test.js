import { describe, expect, it, vi } from 'vitest';
import { createRouteControllerRegistry } from '../js/core/route-controllers.js';

describe('Layui 路由控制器注册表', () => {
  it('首次进入路由时才加载控制器且合并并发请求', async () => {
    const controller = {
      load: vi.fn().mockResolvedValue(undefined),
      dispose: vi.fn()
    };
    const create = vi.fn(() => controller);
    const importController = vi.fn().mockResolvedValue({ create });
    const registry = createRouteControllerRegistry({
      definitions: new Map([
        ['/identity/users', {
          importController,
          create: module => module.create()
        }]
      ]),
      isActive: () => true
    });

    expect(importController).not.toHaveBeenCalled();

    await Promise.all([
      registry.load('/identity/users'),
      registry.load('/identity/users')
    ]);

    expect(importController).toHaveBeenCalledOnce();
    expect(create).toHaveBeenCalledOnce();
    expect(controller.load).toHaveBeenCalledOnce();

    await registry.load('/identity/users');
    expect(importController).toHaveBeenCalledOnce();
    expect(controller.load).toHaveBeenCalledTimes(2);

    registry.dispose();
    expect(controller.dispose).toHaveBeenCalledOnce();
  });

  it('路由切换后不启动已过期控制器的数据请求', async () => {
    let resolveImport;
    const importController = vi.fn(() => new Promise(resolve => {
      resolveImport = resolve;
    }));
    const controller = {
      load: vi.fn().mockResolvedValue(undefined),
      dispose: vi.fn()
    };
    let active = true;
    const registry = createRouteControllerRegistry({
      definitions: new Map([
        ['/tenants', {
          importController,
          create: () => controller
        }]
      ]),
      isActive: () => active
    });

    const loading = registry.load('/tenants');
    active = false;
    resolveImport({});
    await loading;

    expect(controller.load).not.toHaveBeenCalled();
    registry.dispose();
    expect(controller.dispose).toHaveBeenCalledOnce();
  });

  it('卸载后完成的动态导入不会创建控制器', async () => {
    let resolveImport;
    const create = vi.fn(() => ({
      load: vi.fn(),
      dispose: vi.fn()
    }));
    const registry = createRouteControllerRegistry({
      definitions: new Map([
        ['/jobs/host-definitions', {
          importController: () => new Promise(resolve => {
            resolveImport = resolve;
          }),
          create
        }]
      ]),
      isActive: () => true
    });

    const loading = registry.load('/jobs/host-definitions');
    registry.dispose();
    resolveImport({});
    await loading;

    expect(create).not.toHaveBeenCalled();
  });
});
