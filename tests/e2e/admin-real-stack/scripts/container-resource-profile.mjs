export function configureRealStackContainer(container, role) {
  const profile = process.env.FULLNET_E2E_RESOURCE_PROFILE;
  if (!profile) return container;
  if (profile !== 'bounded-local') throw new Error('Unsupported E2E resource profile');
  const budgets = {
    sqlserver: { memory: 3, cpu: 1 },
    mysql: { memory: 1.5, cpu: 1 },
    redis: { memory: 0.25, cpu: 0.25 },
  };
  if (!Object.hasOwn(budgets, role)) throw new Error('Unsupported E2E resource role');
  // 配额在 Docker create 前设置；范围仅为应用容器，不包含 Ryuk 或共享服务。
  return container.withResourcesQuota(budgets[role]);
}
