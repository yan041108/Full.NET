export default async function globalTeardown() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1'
    || process.env.FULLNET_E2E_KEEP_STACK === '1') {
    return;
  }

  const { teardownStack } = await import('./scripts/bootstrap-stack.mjs');
  await teardownStack();

}
