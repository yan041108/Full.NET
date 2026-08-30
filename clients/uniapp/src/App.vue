<script setup lang="ts">
import { onLaunch } from '@dcloudio/uni-app';

import { initializeLocale } from './i18n';

// #ifdef H5
import { restoreH5IdentitySession } from './features/identity/h5-application-session';
// #endif

onLaunch(() => {
  initializeLocale();
  // #ifdef H5
  void restoreH5IdentitySession();
  // #endif
});
</script>

<style lang="scss">
@use "./styles/fullnet-uni-ui.scss";

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
