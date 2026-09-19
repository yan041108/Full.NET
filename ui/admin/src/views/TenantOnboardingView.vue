<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElCard, ElDescriptions, ElDescriptionsItem, ElTag } from 'element-plus';
import { listHostTenants } from '../api/tenants';

defineOptions({ name: 'TenantOnboardingView' });

const tenants = ref<Awaited<ReturnType<typeof listHostTenants>>['items']>([]);
const loading = ref(false);

onMounted(async () => {
  loading.value = true;
  try {
    const page = await listHostTenants(1, 20);
    tenants.value = page.items;
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <ElCard v-loading="loading">
    <template #header>Tenant Onboarding</template>
    <div v-for="tenant in tenants" :key="tenant.id" class="tenant-row">
      <ElDescriptions :column="2" border>
        <ElDescriptionsItem label="Name">{{ tenant.name }}</ElDescriptionsItem>
        <ElDescriptionsItem label="Identifier">{{ tenant.identifier }}</ElDescriptionsItem>
        <ElDescriptionsItem label="Domain">{{ tenant.domain }}</ElDescriptionsItem>
        <ElDescriptionsItem label="Status">
          <ElTag :type="tenant.isActive ? 'success' : 'info'">
            {{ tenant.isActive ? 'Active' : 'Inactive' }}
          </ElTag>
        </ElDescriptionsItem>
      </ElDescriptions>
    </div>
  </ElCard>
</template>

<style scoped>
.tenant-row {
  margin-bottom: 16px;
}
</style>
