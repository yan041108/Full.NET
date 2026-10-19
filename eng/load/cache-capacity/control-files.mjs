import fs from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';

// Windows/容器共享文件的短暂占用不能使控制通道失效；保留原文件，只有原子替换可见新命令。
export async function writeControl(file, value, io = fs, wait = delay) {
  const temporary = file + '.tmp';
  await io.writeFile(temporary, JSON.stringify(value, null, 2));
  for (let attempt = 0; attempt < 20; attempt++) {
    try { await io.rename(temporary, file); return; }
    catch (error) {
      if (!['EPERM', 'EACCES', 'EBUSY'].includes(error.code) || attempt === 19) throw error;
      await wait(50);
    }
  }
}
