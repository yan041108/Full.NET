<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTable, ElTableColumn } from 'element-plus';
import PermissionGate from '../components/PermissionGate.vue';
import { createTenantInvitation, listTenantInvitations, listTenantMembers } from '../api/tenant-members';

defineOptions({ name: 'TenantMembersView' });

const members = ref<Awaited<ReturnType<typeof listTenantMembers>>['items']>([]);
const invitations = ref<Awaited<ReturnType<typeof listTenantInvitations>>['items']>([]);
const loading = ref(false);

async function refresh() {
  loading.value = true;
  try {
    const [memberPage, invitationPage] = await Promise.all([
      listTenantMembers({ page: 1, pageSize: 50 }),
      listTenantInvitations({ page: 1, pageSize: 50 })
    ]);
    members.value = memberPage.items;
    invitations.value = invitationPage.items;
  } catch (error) {
    ElMessage.error(String(error));
  } finally {
    loading.value = false;
  }
}

async function inviteSample() {
  try {
    const result = await createTenantInvitation({
      targetEmail: `invite-${Date.now()}@example.com`,
      memberRole: 'Member'
    });
    ElMessage.success('Invitation created.');
    await refresh();
  } catch (error) {
    ElMessage.error(String(error));
  }
}

onMounted(() => {
  void refresh();
});
</script>

<template>
  <div class="tenant-members-view">
    <ElCard>
      <template #header>
        <div class="header-row">
          <span>Tenant Members</span>
          <PermissionGate code="identity.tenant_members.invite">
            <ElButton type="primary" :loading="loading" @click="inviteSample">Invite Member</ElButton>
          </PermissionGate>
        </div>
      </template>
      <ElTable :data="members" v-loading="loading" stripe>
        <ElTableColumn prop="displayName" label="Name" />
        <ElTableColumn prop="username" label="Username" />
        <ElTableColumn prop="memberRole" label="Role" />
        <ElTableColumn prop="status" label="Status" />
      </ElTable>
    </ElCard>
    <ElCard class="invitations-card">
      <template #header>Pending Invitations</template>
      <ElTable :data="invitations" v-loading="loading" stripe>
        <ElTableColumn prop="targetEmail" label="Email" />
        <ElTableColumn prop="memberRole" label="Role" />
        <ElTableColumn prop="status" label="Status" />
        <ElTableColumn prop="expiresAtUtc" label="Expires" />
      </ElTable>
    </ElCard>
  </div>
</template>

<style scoped>
.header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.invitations-card {
  margin-top: 16px;
}
</style>
