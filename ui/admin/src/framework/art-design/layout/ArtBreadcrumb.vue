<script setup lang="ts">
defineOptions({ name: 'ArtBreadcrumb' });

defineProps<{
  rootLabel: string;
  currentLabel: string;
}>();
</script>

<template>
  <nav class="art-breadcrumb" aria-label="breadcrumb">
    <ul>
      <li>
        <span>{{ rootLabel }}</span>
        <span class="art-breadcrumb__sep" aria-hidden="true">/</span>
      </li>
      <li>
        <strong>{{ currentLabel }}</strong>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.art-breadcrumb {
  margin-left: 8px;
}

.art-breadcrumb ul {
  display: flex;
  align-items: center;
  margin: 0;
  padding: 0;
  list-style: none;
}

.art-breadcrumb li {
  display: flex;
  align-items: center;
  color: var(--art-gray-600);
  font-size: 14px;
}

.art-breadcrumb__sep {
  margin: 0 6px;
  color: var(--art-gray-500);
}

.art-breadcrumb strong {
  color: var(--art-gray-800);
  font-weight: 500;
}

@media (max-width: 1024px) {
  .art-breadcrumb {
    display: none;
  }
}
</style>
