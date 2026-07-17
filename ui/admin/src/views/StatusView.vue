<script setup lang="ts">
defineProps<{
  code: string;
  title: string;
  description: string;
}>();
</script>

<template>
  <main class="status-view">
    <span>{{ code }}</span>
    <h1>{{ title }}</h1>
    <p>{{ description }}</p>
    <router-link to="/">返回工作台</router-link>
  </main>
</template>

<style scoped>
.status-view { display: grid; place-items: start; align-content: center; min-height: calc(100vh - 160px); padding: clamp(32px, 8vw, 96px); border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: radial-gradient(circle at 85% 15%, rgb(11 143 135 / 12%), transparent 32%), var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.status-view span { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 68px; font-weight: 650; letter-spacing: -.06em; }
.status-view h1 { margin: 8px 0; color: var(--fullnet-color-ink); font-family: var(--fullnet-font-display); font-size: 30px; }
.status-view p { max-width: 560px; color: var(--fullnet-color-ink-muted); line-height: 1.8; }
.status-view a { margin-top: 20px; padding: 11px 18px; border-radius: var(--fullnet-radius-sm); background: var(--fullnet-color-ink); color: #fff; text-decoration: none; }
</style>
