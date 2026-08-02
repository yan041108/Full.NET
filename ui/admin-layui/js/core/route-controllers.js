function defineController(importController, exportName, root, options) {
  return {
    importController,
    create: module => module[exportName](root, options)
  };
}

export function createLayuiRouteControllerDefinitions(root, options) {
  const sharedOptions = {
    request: options.request,
    translation: options.translation,
    hasPermission: options.hasPermission
  };

  return new Map([
    ['/', defineController(
      () => import('./overview-dashboard.js'),
      'createOverviewDashboardController',
      root,
      sharedOptions
    )],
    ['/identity/super-administrators', defineController(
      () => import('./super-administrators.js'),
      'createSuperAdministratorController',
      root,
      sharedOptions
    )],
    ['/tenants', defineController(
      () => import('./tenants.js'),
      'createTenantsController',
      root,
      {
        ...sharedOptions,
        getPermissions: options.getPermissions
      }
    )],
    ['/tenant-packages', defineController(
      () => import('./tenant-packages.js'),
      'createTenantPackagesController',
      root,
      sharedOptions
    )],
    ['/settings/dict-types', defineController(
      () => import('./dict-types.js'),
      'createDictTypesController',
      root,
      sharedOptions
    )],
    ['/settings/tenant-dict-types', defineController(
      () => import('./tenant-dict-types.js'),
      'createTenantDictTypesController',
      root,
      {
        ...sharedOptions,
        canWrite: options.canWriteTenantDictTypes
      }
    )],
    ['/settings/diagnostic-policy', defineController(
      () => import('./diagnostic-policy.js'),
      'createDiagnosticPolicyController',
      root,
      sharedOptions
    )],
  ['/settings/config-entries', defineController(
      () => import('./config-entries.js'),
      'createConfigEntriesController',
      root,
      sharedOptions
    )],
    ['/settings/enum-catalogs', defineController(
      () => import('./enum-catalogs.js'),
      'createEnumCatalogsController',
      root,
      sharedOptions
    )],
    ['/files/host-files', defineController(
      () => import('./host-files.js'),
      'createHostFilesController',
      root,
      sharedOptions
    )],
    ['/notifications/host-announcements', defineController(
      () => import('./host-announcements.js'),
      'createHostAnnouncementsController',
      root,
      sharedOptions
    )],
    ['/notifications/inbox-messages', defineController(
      () => import('./inbox-messages.js'),
      'createInboxMessagesController',
      root,
      sharedOptions
    )],
    ['/jobs/host-definitions', defineController(
      () => import('./host-jobs.js'),
      'createHostJobsController',
      root,
      sharedOptions
    )],
    ['/code-generation/previews', defineController(
      () => import('./code-generation-previews.js'),
      'createCodeGenerationPreviewsController',
      root,
      sharedOptions
    )],
    ['/auditing/access-logs', defineController(
      () => import('./access-logs.js'),
      'createAccessLogsController',
      root,
      sharedOptions
    )],
    ['/auditing/operation-logs', defineController(
      () => import('./operation-logs.js'),
      'createOperationLogsController',
      root,
      sharedOptions
    )],
    ['/auditing/exception-logs', defineController(
      () => import('./exception-logs.js'),
      'createExceptionLogsController',
      root,
      sharedOptions
    )],
    ['/auditing/outbound-call-logs', defineController(
      () => import('./outbound-call-logs.js'),
      'createOutboundCallLogsController',
      root,
      sharedOptions
    )],
    ['/identity/online-sessions', defineController(
      () => import('./online-sessions.js'),
      'createOnlineSessionsController',
      root,
      {
        ...sharedOptions,
        getPermissions: options.getPermissions
      }
    )],
    ['/identity/api-keys', defineController(
      () => import('./api-keys.js'),
      'createApiKeysController',
      root,
      {
        ...sharedOptions,
        hasPermission: options.hasPermission,
        getPermissions: options.getPermissions
      }
    )],
    ['/identity/modules', defineController(
      () => import('./module-catalog.js'),
      'createModuleCatalogController',
      root,
      sharedOptions
    )],
    ['/identity/users', defineController(
      () => import('./users.js'),
      'createUsersController',
      root,
      sharedOptions
    )],
    ['/identity/roles', defineController(
      () => import('./roles.js'),
      'createRolesController',
      root,
      {
        ...sharedOptions,
        getTenantId: options.getTenantId,
        getPermissions: options.getPermissions
      }
    )],
    ['/identity/menus', defineController(
      () => import('./menus.js'),
      'createMenusController',
      root,
      {
        ...sharedOptions,
        getPermissions: options.getPermissions
      }
    )],
    ['/organization/units', defineController(
      () => import('./org-units.js'),
      'createOrgUnitsController',
      root,
      sharedOptions
    )],
    ['/organization/positions', defineController(
      () => import('./org-positions.js'),
      'createOrgPositionsController',
      root,
      sharedOptions
    )],
    ['/organization/position-levels', defineController(
      () => import('./org-position-levels.js'),
      'createOrgPositionLevelsController',
      root,
      sharedOptions
    )],
    ['/organization/user-units', defineController(
      () => import('./org-user-units.js'),
      'createOrgUserUnitsController',
      root,
      sharedOptions
    )],
    ['/organization/user-positions', defineController(
      () => import('./org-user-positions.js'),
      'createOrgUserPositionsController',
      root,
      sharedOptions
    )]
  ]);
}

export function createRouteControllerRegistry(options = {}) {
  const definitions = options.definitions ?? new Map();
  const isActive = options.isActive ?? (() => true);
  const controllerPromises = new Map();
  const controllers = new Map();
  const activeLoads = new Map();
  let disposed = false;

  const getController = (route) => {
    const existing = controllerPromises.get(route);
    if (existing) {
      return existing;
    }

    const definition = definitions.get(route);
    if (!definition || disposed) {
      return Promise.resolve(undefined);
    }

    let imported;
    try {
      imported = definition.importController();
    } catch (error) {
      return Promise.reject(error);
    }

    const controllerPromise = Promise.resolve(imported)
      .then((module) => {
        if (disposed) {
          return undefined;
        }

        const controller = definition.create(module);
        if (disposed) {
          controller?.dispose?.();
          return undefined;
        }

        controllers.set(route, controller);
        return controller;
      })
      .catch((error) => {
        controllerPromises.delete(route);
        throw error;
      });
    controllerPromises.set(route, controllerPromise);
    return controllerPromise;
  };

  const load = (route) => {
    if (disposed || !definitions.has(route)) {
      return Promise.resolve();
    }

    const activeLoad = activeLoads.get(route);
    if (activeLoad) {
      return activeLoad;
    }

    const operation = getController(route).then(async (controller) => {
      if (!controller || disposed || !isActive(route)) {
        return;
      }

      await controller.load?.();
    });
    activeLoads.set(route, operation);

    // 同一路由的并发触发共享一次加载，完成后才允许后续刷新重新请求数据。
    const release = () => {
      if (activeLoads.get(route) === operation) {
        activeLoads.delete(route);
      }
    };
    operation.then(release, release);
    return operation;
  };

  return {
    load,
    dispose() {
      if (disposed) {
        return;
      }

      disposed = true;
      activeLoads.clear();
      controllers.forEach(controller => controller?.dispose?.());
      controllers.clear();
      controllerPromises.clear();
    }
  };
}
