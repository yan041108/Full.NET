'use strict';

// 固定上限不可由调用选项放宽；同时覆盖解析器内部回退和外部 AST 的递归入口。
const MAX_DEPTH = 64;
const depthError = () => Object.assign(
  new SyntaxError('Brace nesting exceeds the fixed depth limit (64)'),
  { code: 'ERR_BRACES_DEPTH_LIMIT' }
);

// 子节点必须组成树；parent 回链另行有界校验，prev 不参与结构遍历。
const invalidAst = () => Object.assign(
  new TypeError('Brace AST must have distinct object nodes, array children and string values'),
  { code: 'ERR_BRACES_INVALID_AST' }
);

const assertAstDepth = (ast, strictParents = false) => {
  const pending = [{ node: ast, depth: 0, parent: undefined }];
  const seen = new WeakSet();
  while (pending.length > 0) {
    const { node, depth, parent: expectedParent } = pending.pop();
    // 上游解析结果是树；拒绝共享子树，防止少量对象诱发指数级遍历。
    if (!node || typeof node !== 'object' || seen.has(node)) throw invalidAst();
    seen.add(node);
    if (node.value !== undefined && typeof node.value !== 'string') throw invalidAst();
    if (node.nodes !== undefined && !Array.isArray(node.nodes)) throw invalidAst();
    // expand 的队列必须属于当前遍历祖先，不能借外部 parent 引入未校验队列。
    if (strictParents && node.parent !== undefined && node.parent !== expectedParent) throw invalidAst();
    if (depth > MAX_DEPTH) throw depthError();
    // expand 会沿 parent 回溯到块；外部 AST 也不能用回链环卡住该循环。
    let parent = node && node.parent;
    let parentDepth = 0;
    while (parent) {
      if (++parentDepth > MAX_DEPTH) throw depthError();
      parent = parent.parent;
    }
    if (node && Array.isArray(node.nodes)) {
      for (const child of node.nodes) pending.push({ node: child, depth: depth + 1, parent: node });
    }
  }
};

module.exports = { MAX_DEPTH, depthError, assertAstDepth };
