function scheduleAfterNextPaint(callback) {
  if (typeof globalThis.requestAnimationFrame === 'function') {
    globalThis.requestAnimationFrame(() => {
      globalThis.setTimeout(callback, 0);
    });
    return;
  }

  globalThis.setTimeout(callback, 0);
}

export function deferLayuiRuntime(options) {
  const schedule = options.schedule ?? scheduleAfterNextPaint;
  const whenSettled = Promise.resolve(options.ready)
    .catch(() => undefined)
    // 单体运行库只负责渐进增强，必须让会话主流程和首个可见界面先完成绘制。
    .then(() => new Promise(resolve => schedule(resolve)))
    .then(() => options.importRuntime())
    .then(() => options.enhance())
    .catch(error => options.onError?.(error));

  return { whenSettled };
}
