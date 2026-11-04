/** 等待不合作的提供器时也响应取消，已终止的 Promise 保留拒绝处理器避免未处理异常。 */
export function withAbort<T>(value: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) { void value.catch(() => {}); return Promise.reject(signal.reason); }
  return new Promise((resolve,reject) => {
    const abort = () => reject(signal.reason); signal.addEventListener('abort',abort,{once:true});
    value.then(resolve,reject).finally(() => signal.removeEventListener('abort',abort));
  });
}
