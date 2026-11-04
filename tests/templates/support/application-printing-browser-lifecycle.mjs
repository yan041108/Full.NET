// 将外部取消传到浏览器拥有的资源；关闭 context 会中断挂起的页面、响应和无障碍操作。
export function watchPrintingBrowserCancellation(signal, closeOwnedBrowser) {
 signal?.throwIfAborted();let pending;
 const abort=()=>{pending=Promise.resolve().then(closeOwnedBrowser);pending.catch(()=>{});};
 signal?.addEventListener('abort',abort,{once:true});
 return {
  async dispose() {signal?.removeEventListener('abort',abort);await pending;},
 };
}

// 同时接管响应等待和按钮操作的拒绝，取消或点击失败时不能遗留未处理 Promise。
export async function runPrintingBrowserResponseAction(page, predicate, action) {
 const [response]=await Promise.all([page.waitForResponse(predicate),Promise.resolve().then(action)]);
 return response;
}
