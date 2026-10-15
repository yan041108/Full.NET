function reject() { throw new Error('Native AOT TRX 未完整通过，不能登记本轮验收。'); }

function xmlCharacter(value) {
  return value === 9 || value === 10 || value === 13 || value >= 0x20 && value <= 0xd7ff
    || value >= 0xe000 && value <= 0xfffd || value >= 0x10000 && value <= 0x10ffff;
}

function decode(value) {
  const entities = /&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos);/gi;
  if (value.replace(entities, '').includes('&')) reject();
  return value.replace(entities, (_, name) => {
    if (name.startsWith('#')) {
      const number = name.startsWith('#x') ? Number.parseInt(name.slice(2), 16) : Number(name.slice(1));
      if (!Number.isSafeInteger(number) || !xmlCharacter(number)) reject();
      return String.fromCodePoint(number);
    }
    const known = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };
    if (!Object.hasOwn(known, name)) reject();
    return known[name];
  });
}

function attributes(source) {
  const result = Object.create(null);
  const pattern = /[ \t\r\n]+([A-Za-z_][\w:.-]*)[ \t\r\n]*=[ \t\r\n]*(?:"([^"]*)"|'([^']*)')/y;
  let offset = 0;
  while (!/^[ \t\r\n]*$/.test(source.slice(offset))) {
    pattern.lastIndex = offset;
    const match = pattern.exec(source);
    if (!match || Object.hasOwn(result, match[1]) || match[1].includes(':') && !['xml:space', 'xml:lang', 'xml:base'].includes(match[1])) reject();
    result[match[1]] = decode(match[2] ?? match[3]);
    offset = pattern.lastIndex;
  }
  return result;
}

// 只读取框架 TRX 的固定 XML 子集：不展开 DTD/外部实体，日志的注释和 CDATA 保持不透明。
function document(xml) {
  const stack = []; const nodes = []; let root; let offset = 0;
  while (offset < xml.length) {
    if (xml[offset] !== '<') {
      let end = xml.indexOf('<', offset); if (end < 0) end = xml.length;
      const text = xml.slice(offset, end);
      if (!stack.length && /[^ \t\r\n]/.test(text) || text.includes(']]>')) reject();
      decode(text); offset = end; continue;
    }
    if (xml.startsWith('<!--', offset)) {
      const end = xml.indexOf('-->', offset + 4);
      if (end < 0 || xml.slice(offset + 4, end).includes('--')) reject();
      offset = end + 3; continue;
    }
    if (xml.startsWith('<![CDATA[', offset)) {
      const end = xml.indexOf(']]>', offset + 9);
      if (end < 0 || !stack.length) reject();
      offset = end + 3; continue;
    }
    if (xml.startsWith('<?xml ', offset)) {
      const declaration = /^<\?xml[ \t\r\n]+version=(?:"1\.[01]"|'1\.[01]')(?:[ \t\r\n]+encoding=(?:"[A-Za-z][\w.-]*"|'[A-Za-z][\w.-]*'))?(?:[ \t\r\n]+standalone=(?:"(?:yes|no)"|'(?:yes|no)'))?[ \t\r\n]*\?>/.exec(xml.slice(offset));
      if (offset !== 0 || !declaration) reject();
      offset += declaration[0].length; continue;
    }
    let end = offset + 1; let quote = null;
    for (; end < xml.length; end++) {
      const value = xml[end];
      if (value === '<') reject();
      if (quote) { if (value === quote) quote = null; }
      else if (value === '"' || value === "'") quote = value;
      else if (value === '>') break;
    }
    if (end === xml.length || quote) reject();
    const tag = xml.slice(offset + 1, end); offset = end + 1;
    if (tag.startsWith('/')) {
      const name = /^\/([A-Za-z_][\w:.-]*)[ \t\r\n]*$/.exec(tag)?.[1];
      if (!name || stack.pop()?.name !== name) reject();
      continue;
    }
    const name = /^([A-Za-z_][\w:.-]*)(?=[ \t\r\n]|\/|$)/.exec(tag)?.[1];
    if (!name || name.includes(':') || nodes.length >= 200000 || stack.length >= 64) reject();
    const node = { name, attributes: attributes(tag.slice(name.length).replace(/\/$/, '')), children: [] };
    if (Object.hasOwn(node.attributes, 'xmlns') && node.attributes.xmlns !== 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') reject();
    if (node.attributes['xml:space'] && !['default', 'preserve'].includes(node.attributes['xml:space'])) reject();
    if (stack.length) stack.at(-1).children.push(node);
    else { if (root) reject(); root = node; }
    nodes.push(node);
    if (!tag.endsWith('/')) stack.push(node);
  }
  if (!root || root.name !== 'TestRun' || stack.length
    || root.attributes.xmlns !== 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') reject();
  return { root, nodes };
}

/** 核对本次完整 TRX 与发现 UID；进程退出 0 不代替零失败、零跳过和完整覆盖。 */
export function validateNativeAotRuntimeResults(xml, expectedUids) {
  if (typeof xml !== 'string' || xml.length > 64 * 1024 * 1024 || !xml.isWellFormed()
    || !Array.isArray(expectedUids) || !expectedUids.length
    || expectedUids.some(uid => typeof uid !== 'string' || !uid.trim() || uid.length > 512)) reject();
  const expected = new Set(expectedUids.map(uid => uid.toLowerCase()));
  if (expected.size !== expectedUids.length) reject();
  for (const character of xml) if (!xmlCharacter(character.codePointAt(0))) reject();
  const { root, nodes } = document(xml.replace(/^\uFEFF/, ''));
  const single = (parent, name) => {
    const children = parent.children.filter(node => node.name === name);
    if (children.length !== 1 || nodes.filter(node => node.name === name).length !== 1) reject();
    return children[0];
  };
  const results = single(root, 'Results'); const summary = single(root, 'ResultSummary');
  if (!['Completed', 'Passed'].includes(summary.attributes.outcome)) reject();
  const rows = results.children.filter(node => node.name === 'UnitTestResult');
  if (nodes.filter(node => node.name === 'UnitTestResult').length !== rows.length) reject();
  const actual = new Set();
  for (const { attributes: row } of rows) {
    if (row.outcome !== 'Passed' || !row.testId || actual.has(row.testId.toLowerCase()) || !expected.has(row.testId.toLowerCase())) reject();
    actual.add(row.testId.toLowerCase());
  }
  if (actual.size !== expected.size) reject();
  const counters = single(summary, 'Counters').attributes;
  for (const key of ['total', 'executed', 'passed', 'failed', 'notExecuted']) if (!Object.hasOwn(counters, key)) reject();
  for (const [key, value] of Object.entries(counters)) {
    if (!/^(0|[1-9]\d*)$/.test(value) || !Number.isSafeInteger(Number(value))) reject();
    if (Number(value) !== (['total', 'executed', 'passed'].includes(key) ? expected.size : 0)) reject();
  }
  return { passed: actual.size, uids: [...actual].sort() };
}
