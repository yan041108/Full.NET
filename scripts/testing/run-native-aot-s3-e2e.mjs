#!/usr/bin/env node
/** Native AOT S3 Provider 专项统一复用消费者构建，真实运行仍须具备对应原生产物。 */
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {runNativeSuite} from './native-suite-runner.mjs';

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  runNativeSuite({
    suite: 'nativeAotS3Integration',
    results: 'artifacts/native-aot/linux-x64/test-results',
    trx: 'Full.NET.IntegrationTests-native-aot-s3.trx',
    label: 'Native AOT S3 Provider',
  }).catch(error => {console.error(error.message); process.exitCode = 1;});
}
