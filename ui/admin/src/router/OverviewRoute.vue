<script setup lang="ts">
import { defineAsyncComponent } from 'vue';

// 会话恢复前路由也可能被解析；只有后台真正渲染概览时才加载页面依赖。
const OverviewView = defineAsyncComponent(() => import('../views/OverviewView.vue'));
defineOptions({ name: 'OverviewRoute' });
</script>

<template>
  <OverviewView />
</template>
