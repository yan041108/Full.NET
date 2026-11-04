import { ref } from 'vue';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';

// 文件缺失仅按服务端明确机器码识别，权限、限流和存储故障不能变成“结果已过期”。
export function useUnavailableResultFiles() {
  const unavailable = ref(new Set<string>());
  let generation = 0;
  return {
    isUnavailable: (key: string) => unavailable.value.has(key),
    reset() { generation++; unavailable.value.clear(); },
    begin(key: string) {
      const captured = generation;
      const current = () => captured === generation;
      return {
        current,
        markUnavailable(error: unknown) {
          if (!current() || !isFullNetProblemDetails(error) || error.status !== 404 || error.code !== 'files.file.not_found') return false;
          unavailable.value.add(key);
          return true;
        }
      };
    }
  };
}
