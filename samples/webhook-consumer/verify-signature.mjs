import { readFile } from 'node:fs/promises';
import { readSigningSecret, verifySignature } from './signature.mjs';

const args = process.argv.slice(2);
function argument(name) { const index = args.indexOf(name); if (index < 0 || !args[index + 1]) throw Error(); return args[index + 1]; }
try {
  const secret = await readSigningSecret(argument('--secret-file'));
  const metadata = JSON.parse(await readFile(argument('--metadata-file'),'utf8'));
  const valid = verifySignature(await readFile(argument('--payload-file')), argument('--signature'), secret, metadata, {keyId:argument('--key-id')});
  console.log(valid ? 'SIGNATURE_OK' : 'SIGNATURE_INVALID'); process.exitCode = valid ? 0 : 1;
} catch { console.error('Usage: --secret-file <path> --key-id <configured-uuid> --payload-file <path> --metadata-file <path> --signature <hex>'); process.exitCode = 1; }
