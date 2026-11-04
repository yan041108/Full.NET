<script setup lang="ts">
import { useRouter } from 'vue-router';
import { ElMenuItem, ElSubMenu } from 'element-plus';
import type { ShellNavigationTreeItem } from '../adapters/fullNetShellAdapter';
import { SHELL_NAV_GROUP_PATH_PREFIX } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtSidebarSubmenu' });

defineProps<{
  items: ShellNavigationTreeItem[];
}>();

const router = useRouter();

function hasChildren(item: ShellNavigationTreeItem): boolean {
  return item.children.length > 0;
}

function navigate(path: string): void {
  if (path.startsWith(SHELL_NAV_GROUP_PATH_PREFIX)) {
    return;
  }

  void router.push(path);
}
</script>

<template>
  <template v-for="item in items" :key="item.id">
    <ElSubMenu v-if="hasChildren(item)" :index="item.path">
      <template #title>
        <component :is="item.icon" class="art-sidebar__icon" aria-hidden="true" />
        <span class="art-sidebar__text">{{ item.title }}</span>
      </template>
      <ArtSidebarSubmenu :items="item.children" />
    </ElSubMenu>

    <ElMenuItem
      v-else
      :index="item.path"
      @click="navigate(item.path)"
    >
      <!-- 保留链接语义；链接自行导航并阻止冒泡，避免菜单项为同一次点击再次导航。 -->
      <router-link
        :to="item.path"
        class="art-sidebar__route-link"
        @click.stop
      >
        <component :is="item.icon" class="art-sidebar__icon" aria-hidden="true" />
        <span class="art-sidebar__text">{{ item.title }}</span>
      </router-link>
    </ElMenuItem>
  </template>
</template>

<style scoped>
.art-sidebar__route-link {
  display: inline-flex;
  align-items: center;
  width: 100%;
  height: 100%;
  color: inherit;
  text-decoration: none;
}
</style>
