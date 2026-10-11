/** 登记本次启动实际取得的资源，启动中途失败时也有清理入口。 */
export function createStackResourceScope() {
  const cleanups = [];
  let disposed = false;
  return {
    add(cleanup) {
      if (disposed) throw new Error('Resource scope is already disposed');
      cleanups.push(cleanup);
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      const failures = [];
      for (const cleanup of cleanups.reverse()) {
        try { await cleanup(); } catch (error) { failures.push(error); }
      }
      if (failures.length) throw new AggregateError(failures, 'Real-stack resource cleanup failed');
    }
  };
}

/** 释放资源与删除归属记录必须作为一次有序交付。 */
export async function releaseOwnedStack(resources, clearOwnedState) {
  await resources.dispose();
  await clearOwnedState();
}
