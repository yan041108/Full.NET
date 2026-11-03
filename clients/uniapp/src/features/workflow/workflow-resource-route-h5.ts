import { onMounted, onBeforeUnmount, type Ref } from 'vue';
import { onShow, onHide } from '@dcloudio/uni-app';
import { workflowResourceIdFromHash, type WorkflowResourceKind } from './workflow-resource-links';

/** uni H5复用同页查询时不会重新onLoad；仅活动页监听并销毁旧资源上下文。 */
export function useWorkflowResourceRoute(kind: WorkflowResourceKind, id: Ref<string>, changed: () => void): void {
  let active = false;
  function refresh(): void {
    if (!active) return;
    const next = workflowResourceIdFromHash(window.location.hash, kind);
    if (next === undefined || next === id.value) return;
    id.value = next; changed();
  }
  onShow(() => { active = true; refresh(); });
  onHide(() => { active = false; });
  onMounted(() => { window.addEventListener('hashchange', refresh); refresh(); });
  onBeforeUnmount(() => { active = false; window.removeEventListener('hashchange', refresh); });
}
