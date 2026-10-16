import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from './useAuthorizedViewScope';
/** 三个既有大屏页面共用可信 Host、会话及路由代次，局部权限仍由调用方精确申请。 */
export function useGoViewScope(name: string, reset: () => void, resume: () => void | Promise<void>, required = 'goview.projects.read') {
  const session = useSessionStore(), route = inject(routeLocationKey, undefined), active = ref(false);
  onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; });
  onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
  const canRead = computed(() => active.value && (!route || route.name === name) && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null && session.can('goview.projects.read') && session.can(required));
  const scope = useAuthorizedViewScope(session, reset, () => { if (canRead.value) return resume(); });
  watch(() => JSON.stringify([route?.name, route?.params.projectId, route?.query.versionNumber]), () => { scope.invalidate(); if (canRead.value) void resume(); }, { flush: 'sync' });
  return { session, route, canRead, begin: (permission: string) => canRead.value ? scope.begin(permission) : undefined };
}
