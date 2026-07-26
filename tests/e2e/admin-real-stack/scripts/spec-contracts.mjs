const forbiddenHostContextLocator =
  /getByText\s*\(\s*(['"`])Full\.NET Host\1/g;

/**
 * 返回直接按 Host 文本定位当前上下文的行号。
 * 真实栈必须复用双端辅助函数，避免 Vue 隐藏选项与 Layui 可见文本产生不同语义。
 *
 * @param {string} source
 * @returns {number[]}
 */
export function findForbiddenSessionContextLocators(source) {
  const lineNumbers = [];

  for (const match of source.matchAll(forbiddenHostContextLocator)) {
    lineNumbers.push(source.slice(0, match.index).split('\n').length);
  }

  return lineNumbers;
}
