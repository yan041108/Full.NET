import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';

const args = process.argv.slice(2);

function readArg(name) {
  const index = args.indexOf(name);
  assert.ok(index >= 0 && index + 1 < args.length, `missing ${name}`);
  return args[index + 1];
}

const secret = readArg('--secret');
const payload = readArg('--payload');
const signature = readArg('--signature');
const expected = createHmac('sha256', secret).update(payload).digest('hex').toUpperCase();
assert.equal(expected, signature.toUpperCase());
console.log('SIGNATURE_OK');
