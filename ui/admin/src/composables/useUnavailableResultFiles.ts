import { ref } from 'vue';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

// 文件缺失仅按服务端明确机器码识别，权限、限流和存储故障不能变成“结果已过期”。
export function useUnavailableResultFiles() {
  const unavailable = ref(new Set<string>());
  const readable = ref(new Set<string>());
  let generation = 0;
  return {
    isUnavailable: (key: string) => unavailable.value.has(key),
    availabilityMessageKey(key: string): MessageKey {
      if (unavailable.value.has(key)) return 'dataOutput.resultUnavailable';
      if (readable.value.has(key)) return 'dataOutput.resultLastRead';
      return 'dataOutput.resultPendingVerification';
    },
    reset() {
      generation++;
      unavailable.value.clear();
      readable.value.clear();
    },
    begin(key: string) {
      const captured = generation;
      const current = () => captured === generation;
      return {
        current,
        markReadable() {
          if (!current()) return;
          unavailable.value.delete(key);
          readable.value.add(key);
        },
        markUnavailable(error: unknown) {
          if (!current()) return false;
          readable.value.delete(key);
          if (!isFullNetProblemDetails(error) || error.status !== 404 || error.code !== 'files.file.not_found') return false;
          unavailable.value.add(key);
          return true;
        }
      };
    }
  };
}
