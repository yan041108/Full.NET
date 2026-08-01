#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const profilesDir = path.join(root, 'profiles');
const required = ['2k.json', '5k.json', '10k.json', 'soak.json'];
const order = ['2k', '5k', '10k', 'soak'];
const errors = [];

for (const file of required) {
  const full = path.join(profilesDir, file);
  if (!fs.existsSync(full)) {
    errors.push(`Missing profile ${file}`);
    continue;
  }

  const profile = JSON.parse(fs.readFileSync(full, 'utf8'));
  if (profile.treatVuAsActualInFlight === true) {
    errors.push(`${file}: must not treat VU as actual in-flight`);
  }
  if (profile.capacityStatus !== 'Capacity-not-verified') {
    errors.push(`${file}: capacityStatus must remain Capacity-not-verified until dedicated certification`);
  }
  if (!Array.isArray(profile.executionOrderGate) || profile.executionOrderGate.join(',') !== order.join(',')) {
    errors.push(`${file}: executionOrderGate must be 2k->5k->10k->soak`);
  }
  if (!profile.closedLoop || !profile.openLoop) {
    errors.push(`${file}: both closed_loop and open_loop models are required`);
  }
  if (!profile.providers?.includes('SqlServer') || !profile.providers?.includes('MySql')) {
    errors.push(`${file}: SqlServer and MySql must both be listed for separate certification`);
  }
  if (profile.name === '10k' && profile.targetInFlight !== 10000) {
    errors.push('10k.json: targetInFlight must be 10000');
  }
  for (const scenario of profile.scenariosRequired ?? []) {
    const scenarioPath = path.join(root, 'k6', 'scenarios', `${scenario}.js`);
    if (!fs.existsSync(scenarioPath)) {
      errors.push(`Missing scenario script ${scenario}.js`);
    }
  }
}

if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exit(1);
}

console.log('Load profiles validated: closed/open loop present; VU!=in-flight; order gate 2k->5k->10k->soak.');
