<script setup lang="ts">
import { ref } from 'vue';
import { ElDatePicker, ElPagination } from 'element-plus';

const selectedDate = ref<Date>();
</script>

<template>
  <section
    class="component-locale-fixture"
    data-component-locale-fixture
    aria-label="Component locale verification"
  >
    <el-date-picker v-model="selectedDate" type="date" />
    <el-pagination background layout="prev, pager, next" :page-size="10" :total="30" />
  </section>
</template>

<style scoped>
.component-locale-fixture { position: fixed; right: 16px; bottom: 16px; z-index: 20; display: grid; gap: 10px; width: min(360px, calc(100vw - 32px)); padding: 14px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: 0 16px 40px rgb(14 22 19 / 16%); }
</style>
