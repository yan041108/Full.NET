<script setup lang="ts">
import { onLaunch } from '@dcloudio/uni-app';

import { initializeLocale } from './i18n';

onLaunch(() => {
  initializeLocale();
});
</script>

<style>
page {
  min-height: 100%;
  background: #071421;
  color: #e7f3f2;
  font-family: "Avenir Next", "Noto Sans SC", "Microsoft YaHei", sans-serif;
}

button,
input,
radio {
  font: inherit;
}
</style>
