<script setup lang="ts">
import { computed, onMounted, ref, watch, type Component } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElOption, ElSelect } from 'element-plus';
import {
  Bell,
  Grid,
  OfficeBuilding,
  Search
} from '@element-plus/icons-vue';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import LoginView from './views/LoginView.vue';
import { useSessionStore } from './auth/session';
import { flattenNavigation } from './navigation/catalog';

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const contextProblem = ref<FullNetProblemDetails>();
const hostContextValue = '__fullnet_host__';
const statusPaths = new Set(['/403', '/404', '/500']);
const iconCatalog: Record<string, Component> = {
  dashboard: Grid,
  building: OfficeBuilding
};

onMounted(() => {
  if (session.state === 'initializing') {
    void session.restore();
  }
});

const navigation = computed(() => flattenNavigation(session.navigation));
const activePath = computed(() => route.path);
const selectedContext = computed(() =>
  session.currentUser?.tenantId ?? hostContextValue
);
const activeNavigationTitle = computed(() =>
  navigation.value.find(item => item.path === activePath.value)?.title ?? '状态页'
);

function iconFor(icon: string): Component {
  return iconCatalog[icon] ?? Grid;
}

async function switchFromSelector(value: string): Promise<void> {
  contextProblem.value = undefined;
  try {
    await session.switchTenant(value === hostContextValue ? null : value);
  } catch (error: unknown) {
    contextProblem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.context_switch_failed',
          title: '上下文切换未完成'
        };
  }
}

watch(
  () => [session.state, session.navigation, route.path] as const,
  () => {
    if (!session.isAuthenticated || statusPaths.has(route.path)) {
      return;
    }

    const allowed = navigation.value;
    if (!allowed.some(item => item.path === route.path)) {
      void router.replace(allowed[0]?.path ?? '/403');
    }
  },
  { deep: true }
);
</script>

<template>
  <div v-if="session.state === 'initializing'" class="session-boot" aria-live="polite">
    <span>F</span><strong>正在恢复安全会话</strong><i />
  </div>
  <LoginView v-else-if="session.state === 'anonymous'" />
  <div v-else class="admin-shell" data-client-kind="vue">
    <aside class="sidebar">
      <router-link class="brand" to="/" aria-label="Full.NET 工作台">
        <span class="brand__mark"><i /><i /><i /></span>
        <span><strong>Full.NET</strong><small>CONTROL PLANE</small></span>
      </router-link>

      <div class="tenant-card">
        <span>当前租户</span>
        <strong>{{ session.currentContextName }}</strong>
        <small>{{ session.currentUser?.scope }}</small>
      </div>

      <nav aria-label="主导航">
        <p>管理域</p>
        <router-link v-for="item in navigation" :key="item.path" :to="item.path" :class="{ active: activePath === item.path }">
          <component :is="iconFor(item.icon)" />
          <span><strong>{{ item.title }}</strong><small>{{ item.caption }}</small></span>
          <i class="nav-signal" />
        </router-link>
      </nav>

      <div class="sidebar__footer">
        <span class="environment-light" />
        <div><strong>Production</strong><small>API v1 · 正常</small></div>
      </div>
    </aside>

    <section class="shell-body">
      <header class="topbar">
        <div class="command-box">
          <Search />
          <span>搜索菜单、用户或命令</span>
          <kbd>⌘ K</kbd>
        </div>
        <div class="topbar__tools">
          <div v-if="session.can('tenancy.tenants.read')" class="context-picker">
            <span>有效范围</span>
            <el-select
              :model-value="selectedContext"
              :disabled="session.switching || !session.can('tenancy.tenants.switch')"
              aria-label="切换租户上下文"
              @change="switchFromSelector"
            >
              <el-option label="Full.NET Host" :value="hostContextValue" />
              <el-option
                v-for="tenant in session.availableTenants"
                :key="tenant.id"
                :label="tenant.name"
                :value="tenant.id"
              />
            </el-select>
          </div>
          <button type="button" aria-label="通知"><Bell /><i /></button>
          <div class="operator"><span>FN</span><div><strong>{{ session.currentUser?.displayName }}</strong><small>{{ session.currentUser?.scope === 'host' ? 'Host Admin' : session.currentUser?.username }}</small></div></div>
          <button type="button" aria-label="退出登录" @click="session.logout">↗</button>
        </div>
      </header>

      <div class="context-rail">
        <span>管理控制台</span><i>/</i><strong>{{ activeNavigationTitle }}</strong>
        <em>TRACE READY</em>
      </div>

      <div v-if="contextProblem" class="shell-problem" role="alert">
        <strong>{{ contextProblem.code }}</strong>
        <span>{{ contextProblem.title }}</span>
        <code v-if="contextProblem.traceId">{{ contextProblem.traceId }}</code>
      </div>

      <div class="page-stage">
        <router-view />
      </div>
    </section>
  </div>
</template>

<style scoped>
.session-boot { display: grid; min-height: 100vh; place-content: center; justify-items: center; gap: 13px; background: #172027; color: #fff; font-family: var(--fullnet-font-display); }
.session-boot span { display: grid; width: 46px; height: 46px; place-items: center; background: var(--fullnet-color-accent-bright); color: #172027; font-size: 20px; font-weight: 800; }
.session-boot strong { font-size: 12px; letter-spacing: .1em; }
.session-boot i { width: 120px; height: 2px; overflow: hidden; background: rgb(255 255 255 / 10%); }
.session-boot i::after { display: block; width: 40%; height: 100%; animation: boot 1s infinite ease-in-out; background: var(--fullnet-color-accent-bright); content: ""; }
@keyframes boot { from { transform: translateX(-100%); } to { transform: translateX(350%); } }
.admin-shell { min-height: 100vh; background: var(--fullnet-color-canvas); color: var(--fullnet-color-ink); }
.sidebar { position: fixed; inset: 0 auto 0 0; z-index: 20; display: flex; width: var(--fullnet-shell-sidebar-width); flex-direction: column; overflow: hidden; background: var(--fullnet-color-sidebar); color: #fff; }
.sidebar::after { position: absolute; top: 0; right: 0; width: 1px; height: 100%; background: linear-gradient(180deg, transparent, var(--fullnet-color-accent-bright) 28%, transparent 68%); opacity: .5; content: ""; }
.brand { display: flex; align-items: center; gap: 12px; height: var(--fullnet-shell-header-height); padding: 0 22px; border-bottom: 1px solid rgb(255 255 255 / 8%); color: #fff; text-decoration: none; }
.brand__mark { display: flex; align-items: flex-end; gap: 3px; width: 25px; height: 24px; }
.brand__mark i { width: 5px; border-radius: 1px; background: var(--fullnet-color-accent-bright); }
.brand__mark i:nth-child(1) { height: 12px; }
.brand__mark i:nth-child(2) { height: 22px; }
.brand__mark i:nth-child(3) { height: 17px; background: var(--fullnet-color-signal); }
.brand strong, .brand small { display: block; }
.brand strong { font-family: var(--fullnet-font-display); font-size: 17px; letter-spacing: -.02em; }
.brand small { margin-top: 2px; color: #718087; font-family: var(--fullnet-font-display); font-size: 7px; letter-spacing: .19em; }
.tenant-card { margin: 21px 16px 11px; padding: 14px 14px 15px; border: 1px solid rgb(255 255 255 / 9%); border-radius: var(--fullnet-radius-sm); background: rgb(255 255 255 / 4%); }
.tenant-card span, .tenant-card strong, .tenant-card small { display: block; }
.tenant-card span { color: #75858b; font-size: 9px; letter-spacing: .12em; }
.tenant-card strong { margin-top: 8px; font-size: 13px; }
.tenant-card small { margin-top: 4px; color: #8d9b9e; font-family: var(--fullnet-font-display); font-size: 8px; }
nav { padding: 12px 10px; }
nav > p { margin: 0 12px 10px; color: #66767c; font-size: 9px; letter-spacing: .18em; }
nav a { position: relative; display: grid; grid-template-columns: 20px 1fr 4px; align-items: center; gap: 12px; min-height: 56px; margin-bottom: 4px; padding: 0 13px; border-radius: var(--fullnet-radius-sm); color: #8f9ca1; text-decoration: none; transition: color var(--fullnet-motion-fast), background var(--fullnet-motion-fast); }
nav a > svg { width: 17px; }
nav a strong, nav a small { display: block; }
nav a strong { color: #cbd2d4; font-size: 12px; }
nav a small { margin-top: 3px; color: #65757a; font-family: var(--fullnet-font-display); font-size: 8px; letter-spacing: .05em; }
nav a:hover, nav a.active { background: var(--fullnet-color-sidebar-raised); color: var(--fullnet-color-accent-bright); }
nav a.active strong { color: #fff; }
.nav-signal { width: 4px; height: 4px; border-radius: 50%; background: transparent; }
nav a.active .nav-signal { background: var(--fullnet-color-signal); box-shadow: 0 0 0 4px rgb(217 155 53 / 12%); }
.sidebar__footer { display: flex; align-items: center; gap: 10px; margin: auto 17px 18px; padding: 14px; border-top: 1px solid rgb(255 255 255 / 8%); }
.environment-light { width: 7px; height: 7px; border-radius: 50%; background: var(--fullnet-color-accent-bright); box-shadow: 0 0 0 5px rgb(66 185 166 / 11%); }
.sidebar__footer strong, .sidebar__footer small { display: block; }
.sidebar__footer strong { font-family: var(--fullnet-font-display); font-size: 10px; }
.sidebar__footer small { margin-top: 3px; color: #75858b; font-size: 8px; }
.shell-body { min-height: 100vh; margin-left: var(--fullnet-shell-sidebar-width); }
.topbar { position: sticky; top: 0; z-index: 15; display: flex; align-items: center; justify-content: space-between; height: var(--fullnet-shell-header-height); padding: 0 26px; border-bottom: 1px solid var(--fullnet-color-line); background: rgb(255 254 250 / 90%); backdrop-filter: blur(16px); }
.command-box { display: flex; align-items: center; gap: 10px; min-width: 320px; color: #89938f; font-size: 11px; }
.command-box svg { width: 15px; }
.command-box kbd { margin-left: auto; padding: 4px 7px; border: 1px solid var(--fullnet-color-line); border-radius: 4px; background: #f4f5f1; color: #66727d; font-family: var(--fullnet-font-display); font-size: 9px; }
.topbar__tools { display: flex; align-items: center; gap: 18px; }
.context-picker { display: grid; grid-template-columns: auto 190px; align-items: center; gap: 9px; }
.context-picker > span { color: var(--fullnet-color-ink-muted); font-size: 9px; letter-spacing: .08em; }
.context-picker :deep(.el-select__wrapper) { min-height: 36px; border-radius: var(--fullnet-radius-sm); background: #f4f5f1; box-shadow: 0 0 0 1px var(--fullnet-color-line) inset; }
.topbar__tools button { position: relative; display: grid; width: 34px; height: 34px; place-items: center; border: 1px solid var(--fullnet-color-line); border-radius: 50%; background: transparent; color: var(--fullnet-color-ink); cursor: pointer; }
.topbar__tools button svg { width: 15px; }
.topbar__tools button i { position: absolute; top: 5px; right: 5px; width: 6px; height: 6px; border: 2px solid var(--fullnet-color-panel); border-radius: 50%; background: var(--fullnet-color-signal); }
.operator { display: flex; align-items: center; gap: 9px; }
.operator > span { display: grid; width: 34px; height: 34px; place-items: center; border-radius: 9px; background: var(--fullnet-color-ink); color: #fff; font-family: var(--fullnet-font-display); font-size: 10px; }
.operator strong, .operator small { display: block; }
.operator strong { font-size: 11px; }
.operator small { margin-top: 2px; color: var(--fullnet-color-ink-muted); font-family: var(--fullnet-font-display); font-size: 8px; }
.context-rail { display: flex; align-items: center; gap: 9px; height: 38px; padding: 0 28px; border-bottom: 1px solid var(--fullnet-color-line); background: #eaede8; color: #87918e; font-size: 9px; }
.context-rail i { font-style: normal; opacity: .5; }
.context-rail strong { color: var(--fullnet-color-ink); }
.context-rail em { margin-left: auto; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 8px; font-style: normal; letter-spacing: .13em; }
.shell-problem { display: flex; align-items: center; gap: 12px; margin: 14px 28px 0; padding: 11px 14px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); font-size: 11px; }
.shell-problem strong { color: var(--fullnet-color-danger); }
.shell-problem code { margin-left: auto; color: var(--fullnet-color-ink-muted); }
.page-stage { padding: clamp(18px, 2.5vw, 32px); }
@media (max-width: 1020px) { .context-picker { grid-template-columns: 150px; } .context-picker > span { display: none; } }
@media (max-width: 820px) { .sidebar { position: static; width: 100%; min-height: auto; } .tenant-card, nav, .sidebar__footer { display: none; } .shell-body { margin-left: 0; } .topbar { padding-inline: 16px; } .command-box { min-width: 0; } .command-box span, .command-box kbd, .operator div, .topbar__tools > button:first-of-type { display: none; } .context-picker { grid-template-columns: minmax(130px, 1fr); } .context-rail { padding-inline: 18px; } }
</style>
