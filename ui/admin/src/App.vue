<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import {
  Bell,
  Grid,
  OfficeBuilding,
  Search,
  Setting,
  User
} from '@element-plus/icons-vue';

const route = useRoute();

const navigation = [
  { label: '工作台', caption: 'Overview', path: '/', icon: Grid },
  { label: '身份权限', caption: 'Identity', path: '/identity', icon: User },
  { label: '组织架构', caption: 'Organization', path: '/organization', icon: OfficeBuilding },
  { label: '系统设置', caption: 'Settings', path: '/settings', icon: Setting }
];

const activePath = computed(() => route.path);
</script>

<template>
  <div class="admin-shell">
    <aside class="sidebar">
      <router-link class="brand" to="/" aria-label="Full.NET 工作台">
        <span class="brand__mark"><i /><i /><i /></span>
        <span><strong>Full.NET</strong><small>CONTROL PLANE</small></span>
      </router-link>

      <div class="tenant-card">
        <span>当前租户</span>
        <strong>星云科技</strong>
        <small>CN-SH / Production</small>
      </div>

      <nav aria-label="主导航">
        <p>管理域</p>
        <router-link v-for="item in navigation" :key="item.path" :to="item.path" :class="{ active: activePath === item.path }">
          <component :is="item.icon" />
          <span><strong>{{ item.label }}</strong><small>{{ item.caption }}</small></span>
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
          <button type="button" aria-label="通知"><Bell /><i /></button>
          <div class="operator"><span>FN</span><div><strong>系统管理员</strong><small>Host Admin</small></div></div>
        </div>
      </header>

      <div class="context-rail">
        <span>管理控制台</span><i>/</i><strong>{{ navigation.find(item => item.path === activePath)?.label ?? '状态页' }}</strong>
        <em>TRACE READY</em>
      </div>

      <div class="page-stage">
        <router-view />
      </div>
    </section>
  </div>
</template>

<style scoped>
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
.page-stage { padding: clamp(18px, 2.5vw, 32px); }
@media (max-width: 820px) { .sidebar { position: static; width: 100%; min-height: auto; } .tenant-card, nav, .sidebar__footer { display: none; } .shell-body { margin-left: 0; } .topbar { padding-inline: 16px; } .command-box { min-width: 0; } .command-box span, .command-box kbd, .operator div { display: none; } .context-rail { padding-inline: 18px; } }
</style>
