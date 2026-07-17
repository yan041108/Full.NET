<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElButton, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';

const session = useSessionStore();
const problem = ref<FullNetProblemDetails>();
const pendingTenantId = ref<string | null>();
const canSwitch = computed(() => session.can('tenancy.tenants.switch'));

async function selectContext(tenantId: string | null): Promise<void> {
  if (session.switching) {
    return;
  }

  pendingTenantId.value = tenantId;
  problem.value = undefined;
  try {
    await session.switchTenant(tenantId);
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.context_switch_failed',
          title: '上下文切换未完成'
        };
  } finally {
    pendingTenantId.value = undefined;
  }
}
</script>

<template>
  <main class="tenant-context-view">
    <section class="context-heading">
      <div>
        <p>TRUSTED TENANT CONTEXT</p>
        <h1>租户上下文</h1>
        <span>当前范围来自已签名 Token；浏览器输入不能直接改变数据边界。</span>
      </div>
      <el-tag effect="dark" type="success">SESSION BOUND</el-tag>
    </section>

    <section class="current-context" aria-label="当前租户上下文">
      <div class="current-context__signal"><i /><span>当前有效范围</span></div>
      <strong>{{ session.currentContextName }}</strong>
      <code>{{ session.currentUser?.scope }}</code>
      <el-button
        v-if="canSwitch && session.currentUser?.tenantId"
        :loading="session.switching && pendingTenantId === null"
        :disabled="session.switching"
        data-testid="return-host"
        @click="selectContext(null)"
      >返回 Host</el-button>
    </section>

    <div v-if="problem" class="context-problem" role="alert">
      <strong>{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId">{{ problem.traceId }}</code>
    </div>

    <section class="tenant-directory" aria-labelledby="tenant-directory-title">
      <header>
        <div><span>01</span><h2 id="tenant-directory-title">可用租户</h2></div>
        <small>{{ session.availableTenants.length }} 个活动范围</small>
      </header>
      <div class="tenant-grid">
        <article
          v-for="tenant in session.availableTenants"
          :key="tenant.id"
          :class="{ active: session.currentUser?.tenantId === tenant.id }"
        >
          <span class="tenant-grid__code">{{ tenant.identifier }}</span>
          <h3>{{ tenant.name }}</h3>
          <p>{{ tenant.domain }}</p>
          <div>
            <small>{{ session.currentUser?.tenantId === tenant.id ? '当前上下文' : '可进入' }}</small>
            <el-button
              v-if="canSwitch && session.currentUser?.tenantId !== tenant.id"
              :data-tenant-id="tenant.id"
              :loading="session.switching && pendingTenantId === tenant.id"
              :disabled="session.switching"
              @click="selectContext(tenant.id)"
            >进入租户</el-button>
          </div>
        </article>
      </div>
    </section>
  </main>
</template>

<style scoped>
.tenant-context-view { display: grid; gap: 18px; }
.context-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.context-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.context-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.context-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.current-context { position: relative; display: grid; grid-template-columns: minmax(180px, 1fr) minmax(240px, 2fr) minmax(220px, 1fr) auto; align-items: center; gap: 20px; overflow: hidden; min-height: 112px; padding: 24px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; box-shadow: var(--fullnet-shadow-panel); }
.current-context::after { position: absolute; right: -42px; width: 160px; height: 160px; border: 1px solid rgb(66 185 166 / 24%); border-radius: 50%; content: ""; }
.current-context__signal { display: flex; align-items: center; gap: 10px; color: #91a0a3; font-size: 10px; letter-spacing: .12em; }
.current-context__signal i { width: 8px; height: 8px; border-radius: 50%; background: var(--fullnet-color-accent-bright); box-shadow: 0 0 0 6px rgb(66 185 166 / 12%); }
.current-context > strong { font-family: var(--fullnet-font-display); font-size: 25px; font-weight: 520; }
.current-context > code { color: var(--fullnet-color-accent-bright); font-size: 11px; }
.current-context :deep(.el-button) { position: relative; z-index: 1; border-color: rgb(255 255 255 / 22%); background: transparent; color: #fff; }
.context-problem { display: flex; align-items: center; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); font-size: 12px; }
.context-problem strong { color: var(--fullnet-color-danger); }
.context-problem code { margin-left: auto; color: var(--fullnet-color-ink-muted); }
.tenant-directory { border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.tenant-directory > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.tenant-directory > header div { display: flex; align-items: center; gap: 12px; }
.tenant-directory > header span { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; }
.tenant-directory h2 { margin: 0; font-size: 15px; }
.tenant-directory > header small { color: var(--fullnet-color-ink-muted); }
.tenant-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 14px; padding: 18px; }
.tenant-grid article { position: relative; min-height: 176px; padding: 20px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-sm); background: #fafbf7; transition: border-color var(--fullnet-motion-fast), transform var(--fullnet-motion-fast); }
.tenant-grid article:hover { border-color: var(--fullnet-color-accent); transform: translateY(-2px); }
.tenant-grid article.active { border-color: var(--fullnet-color-accent); box-shadow: inset 3px 0 var(--fullnet-color-accent); }
.tenant-grid__code { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 9px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; }
.tenant-grid h3 { margin: 13px 0 7px; font-family: var(--fullnet-font-display); font-size: 20px; }
.tenant-grid p { margin: 0; color: var(--fullnet-color-ink-muted); font-size: 11px; }
.tenant-grid article > div { display: flex; align-items: center; justify-content: space-between; margin-top: 22px; }
.tenant-grid article > div small { color: var(--fullnet-color-success); }
.tenant-grid :deep(.el-button) { --el-button-bg-color: var(--fullnet-color-ink); --el-button-border-color: var(--fullnet-color-ink); --el-button-text-color: #fff; }
@media (max-width: 860px) { .context-heading { align-items: flex-start; flex-direction: column; } .current-context { grid-template-columns: 1fr; } }
</style>
