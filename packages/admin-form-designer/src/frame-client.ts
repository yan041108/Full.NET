import { VFORM_RUNTIME_HASH } from './runtime-release.generated';
const maximumMessageLength = 2_100_000;
/** 隔离帧只返回固定协议；窗口、opaque Origin、随机通道和请求身份必须同时匹配。 */
export class VFormFrameClient {
  readonly channel = crypto.randomUUID();
  private requests = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout> }>();
  private ready = false;
  private failed = false;
  constructor(private frame: () => Window | null, private onReady: () => void, private target = window, private onData?: (value: Record<string, unknown>) => void, private onError?: (code: string) => void) { target.addEventListener('message', this.receive); }
  private receive = (event: MessageEvent): void => {
    const data = event.data;
    if (this.failed || event.source !== this.frame() || event.origin !== 'null' || !data || typeof data !== 'object' || Array.isArray(data) || data.protocol !== 'fullnet.vform3.v1' || data.channel !== this.channel) return;
    // postMessage 允许循环引用和 BigInt，但本协议只传 JSON；异常与超限回执不得穿透宿主。
    let length: number;
    try { length = JSON.stringify(data).length; }
    catch { this.rejectMessage(data, 'client.vform3_operation_failed'); return; }
    if (length > maximumMessageLength) { this.rejectMessage(data, 'client.vform3_payload_too_large'); return; }
    if (data.ready === true) {
      // 同一宿主只接收一次就绪；重载后虽是同一个 WindowProxy，也已不是原来的设计会话。
      if (this.ready) {
        this.fail(data.runtimeHash === VFORM_RUNTIME_HASH ? 'client.vform3_operation_failed' : 'client.vform3_runtime_mismatch');
        return;
      }
      if (data.runtimeHash !== VFORM_RUNTIME_HASH) { this.onError?.('client.vform3_runtime_mismatch'); return; }
      this.ready = true; this.onReady(); return;
    }
    // Ready 的摘要核验先于任何业务数据，错误产物不能提前向调用方注入变更。
    if (!this.ready) return;
    if (Object.hasOwn(data, 'changed')) {
      const changed = data.changed;
      // 不能静默漏掉当前帧的输入后继续提交旧补丁；非法通知必须关闭本次会话。
      if (!changed || typeof changed !== 'object' || Array.isArray(changed) ||
        ![Object.prototype, null].includes(Object.getPrototypeOf(changed))) this.fail('client.vform3_operation_failed');
      else if (JSON.stringify(changed).length > 2_000_000) this.fail('client.vform3_payload_too_large');
      else this.onData?.(changed);
      return;
    }
    const request = this.requests.get(data.id);
    if (!request) return;
    clearTimeout(request.timer); this.requests.delete(data.id);
    if (data.error) request.reject(new Error('client.vform3_operation_failed')); else request.resolve(data.result);
  };
  private rejectMessage(data: Record<string, unknown>, code: string): void {
    if (this.ready && Object.hasOwn(data, 'changed')) this.fail(code);
    else this.rejectRequest(data.id, code);
  }
  private rejectRequest(id: unknown, code: string): void {
    if (typeof id !== 'string') return;
    const request = this.requests.get(id);
    if (!request) return;
    clearTimeout(request.timer); this.requests.delete(id); request.reject(new Error(code));
  }
  /** 帧重载或加载失败后必须重建宿主；拒绝所有旧请求，不接收空画布或迟到数据。 */
  fail(code: string): void {
    if (this.failed) return;
    this.failed = true; this.ready = false;
    for (const request of this.requests.values()) { clearTimeout(request.timer); request.reject(new Error(code)); }
    this.requests.clear();
    this.onError?.(code);
  }
  request(method: 'setFormJson' | 'getFormJson' | 'render' | 'getFormData', value?: unknown): Promise<unknown> {
    if (!this.ready) return Promise.reject(new Error('client.vform3_not_ready'));
    const id = crypto.randomUUID(); const message = { protocol: 'fullnet.vform3.v1', channel: this.channel, id, method, value };
    if (JSON.stringify(message).length > maximumMessageLength) return Promise.reject(new Error('client.vform3_payload_too_large'));
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.requests.delete(id); reject(new Error('client.vform3_timeout')); }, 15_000);
      this.requests.set(id, { resolve, reject, timer });
      // opaque Origin 不能作为 targetOrigin；接收方仍核验来源窗口、父 Origin 与通道。
      this.frame()?.postMessage(JSON.parse(JSON.stringify(message)), '*');
    });
  }
  dispose(): void {
    this.target.removeEventListener('message', this.receive);
    for (const request of this.requests.values()) { clearTimeout(request.timer); request.reject(new Error('client.vform3_disposed')); }
    this.requests.clear(); this.ready = false; this.failed = true;
  }
}
