#!/usr/bin/env python3
"""只读核对固定 OCI 候选的 Host/模块验收回执，不签发部署授权或现场成功。"""
import argparse
import json
import math
from pathlib import Path
import re
import stat
import sys


class EvidenceError(ValueError):
    """仅携带本文件声明的稳定机器码，不包含回执字段值。"""


def require(value, code):
    if not value:
        raise EvidenceError(code)


def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result, "receipt_duplicate_key")
        result[key] = value
    return result


def load(path):
    require(stat.S_ISREG(path.lstat().st_mode), "receipt_file_invalid")
    with path.open("rb") as stream:
        content = stream.read(262145)
    require(len(content) <= 262144, "receipt_too_large")
    return json.loads(content.decode("utf-8"), object_pairs_hook=pairs, parse_float=finite_number,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("receipt_nonfinite")))


def finite_number(text):
    # parse_constant 不处理 1e309 这类合法数值语法，必须同时拒绝浮点转换溢出。
    value = float(text)
    require(math.isfinite(value), "receipt_nonfinite")
    return value


def sha(value, prefix="sha256:"):
    return isinstance(value, str) and re.fullmatch(re.escape(prefix) + r"[0-9a-f]{64}", value) is not None


def image_source(image_id, repo_digests):
    return sha(image_id) and isinstance(repo_digests, list) and 1 <= len(repo_digests) <= 8 and all(
        isinstance(item, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._:/-]{0,255}@sha256:[0-9a-f]{64}", item)
        for item in repo_digests)


def verify(receipts, source, oci_run, required_hosts=(), require_modules=False):
    """只接受已知范围；同一绑定内不同验收源码合法，不同应用/镜像来源必须拒绝。"""
    require(re.fullmatch(r"[0-9a-f]{40}", source) and re.fullmatch(r"[1-9][0-9]{0,14}", oci_run), "expected_identity_invalid")
    require(1 <= len(receipts) <= 16, "receipt_count_invalid")
    binding, hosts, modules = None, set(), False
    module_binding = None
    verification_commits = set()
    for receipt in receipts:
        require(isinstance(receipt, dict) and type(receipt.get("schemaVersion")) is int and receipt["schemaVersion"] == 1,
                "receipt_schema_invalid")
        require(receipt.get("scope") == "linux-oci-import" and receipt.get("sourceCommit") == source and
                receipt.get("ociRunId") == oci_run, "receipt_candidate_mismatch")
        require(all(isinstance(receipt.get(key), str) and re.fullmatch(r"[0-9a-f]{40}", receipt[key])
                    for key in ("packagingCommit", "verificationCommit")), "receipt_commit_invalid")
        require(receipt.get("imageImportVerified") is True and receipt.get("runtimeDiscoveryVerified") is True,
                "receipt_import_not_verified")
        require(all(receipt.get(key) is False for key in ("deploymentVerified", "nativeAotVerified", "capacityVerified")),
                "receipt_unsupported_claim")
        require(sha(receipt.get("candidateReceiptSha256")), "receipt_candidate_digest_invalid")
        roles = receipt.get("roles")
        require(isinstance(roles, dict) and set(roles) == {"api", "worker", "migrator"}, "receipt_roles_invalid")
        identities = {}
        for role, value in roles.items():
            require(isinstance(value, dict) and all(sha(value.get(key)) for key in ("imageId", "archiveSha256", "manifestDigest")),
                    "receipt_role_digest_invalid")
            layers = value.get("rootfsDiffIds")
            require(isinstance(layers, list) and 1 <= len(layers) <= 129 and all(sha(item) for item in layers),
                    "receipt_layers_invalid")
            runtimes = value.get("runtimeDiscovery")
            require(isinstance(runtimes, list) and 2 <= len(runtimes) <= 8 and all(isinstance(item, str) for item in runtimes),
                    "receipt_runtime_proof_invalid")
            found = [re.fullmatch(r"Microsoft\.(NETCore|AspNetCore)\.App 10\.0\.[0-9]+ \[/[^\r\n]+\]", item) for item in runtimes]
            require(all(found) and {item.group(1) for item in found} == {"NETCore", "AspNetCore"}, "receipt_runtime_proof_invalid")
            identities[role] = {key: value[key] for key in ("imageId", "archiveSha256", "manifestDigest", "rootfsDiffIds")}
        current = {"packagingCommit": receipt["packagingCommit"], "candidateReceiptSha256": receipt["candidateReceiptSha256"],
                   "roles": identities}
        require(binding is None or binding == current, "receipt_image_binding_mismatch")
        binding = current
        verification_commits.add(receipt["verificationCommit"])
        host = receipt.get("hostVerification")
        require(all(receipt.get(key) is (host is not None) for key in ("runtimeStartupVerified", "databaseMigrationVerified")),
                "receipt_host_claim_mismatch")
        if host is not None:
            require(isinstance(host, dict), "receipt_host_invalid")
            scope = str(host.get("environment")) + "/" + str(host.get("provider"))
            require(scope in ("Testing/MySql", "Testing/SqlServer", "Production/MySql"), "receipt_host_scope_unsupported")
            require(host.get("runtimeStartupVerified") is True and host.get("databaseMigrationVerified") is True and
                    host.get("initialEmptyDatabaseRestoreVerified") is True and
                    type(host.get("migrationJournalCount")) is int and host["migrationJournalCount"] > 0,
                    "receipt_host_not_verified")
            require(image_source(host.get("databaseImageId"), host.get("databaseRepoDigests")) and
                    sha(host.get("initialEmptyDatabaseBackupSha256"), ""), "receipt_database_proof_invalid")
            require(host.get("productionConfigurationVerified") is (host["environment"] == "Production"),
                    "receipt_environment_claim_mismatch")
            checks = host.get("hosts")
            require(isinstance(checks, dict) and set(checks) == {"api", "worker"}, "receipt_host_checks_invalid")
            for check in checks.values():
                require(isinstance(check, dict) and all(type(check.get(key)) is int and check[key] == 200
                        for key in ("live", "startup", "ready")) and type(check.get("gracefulStopExitCode")) is int and
                        check["gracefulStopExitCode"] == 0, "receipt_host_checks_failed")
            if scope == "Production/MySql":
                evidence = host.get("productionEvidence")
                require(isinstance(evidence, dict) and evidence.get("mysqlTlsMode") == "VerifyFull" and
                        evidence.get("mysqlSecureTransportRequired") is True and evidence.get("ephemeralSigningEnabled") is False and
                        type(evidence.get("mysqlObservedTlsAccepts")) is int and evidence["mysqlObservedTlsAccepts"] > 0,
                        "receipt_production_evidence_invalid")
                require(sha(evidence.get("mysqlCaSha256"), "") and sha(evidence.get("dataProtectionCertificateSha256"), "") and
                        image_source(evidence.get("redisImageId"), evidence.get("redisRepoDigests")) and
                        evidence.get("sharedCertificateProtectedKeyRingConfigured") is True and
                        evidence.get("cloudProviderCallsVerified") is False, "receipt_production_metadata_invalid")
            hosts.add(scope)
        keyring = receipt.get("keyRingVerification")
        if keyring is not None:
            require(isinstance(keyring, dict) and keyring.get("mode") == "consumers" and
                    keyring.get("businessHostConsumerVerified") is False, "receipt_module_scope_unsupported")
            require(keyring.get("scope") == "independent-processes-in-candidate-images" and keyring.get("environment") == "Production" and
                    keyring.get("nativeAotVerified") is False and sha(keyring.get("candidateHostingSha256"), "") and
                    all(keyring.get(key) is True for key in ("apiToWorkerVerified", "workerToApiVerified", "freshProcessReloadVerified")),
                    "receipt_keyring_proof_invalid")
            consumers = keyring.get("consumers")
            require(isinstance(consumers, dict) and consumers.get("moduleProtectorComponentsVerified") is True and
                    all(consumers.get(key) is False for key in ("businessHostPipelineVerified", "externalDeliveryVerified",
                                                               "paymentChannelVerified", "nativeAotVerified")), "receipt_modules_not_verified")
            inventories = consumers.get("moduleAssemblies")
            expected = {"Full.NET.Modules." + name + ".dll" for name in ("Identity", "Notifications", "Payments")}
            require(consumers.get("scope") == "actual-module-protector-components-in-independent-processes" and
                    isinstance(inventories, dict) and set(inventories) == {"api", "worker"} and
                    all(isinstance(inventory, dict) and set(inventory) == expected and all(sha(value, "") for value in inventory.values())
                        for inventory in inventories.values()), "receipt_module_inventory_invalid")
            current_modules = {"hostingSha256": keyring["candidateHostingSha256"], "assemblies": inventories}
            require(module_binding is None or module_binding == current_modules, "receipt_module_binding_mismatch")
            module_binding = current_modules
            protectors = consumers.get("protectors")
            require(isinstance(protectors, dict) and set(protectors) == {"notification", "ldap", "oauth", "totp", "payment-api", "payment-private"} and
                    all(isinstance(check, dict) and all(check.get(key) is True for key in ("apiToWorkerVerified", "workerToApiVerified",
                        "differentConsumerPurposeRejected")) for check in protectors.values()) and
                    consumers.get("genericProbePurposeRejected") is True and consumers.get("paymentPurposesBothDirectionsRejected") is True,
                    "receipt_protector_checks_failed")
            recovery = consumers.get("notificationRecipientMediaRecovery")
            require(isinstance(recovery, dict) and all(recovery.get(key) is True for key in ("originalPayloadRestoredVerified",
                    "restoredRoleRoundTripVerified", "corruptedBackupRejectedBeforeWrites", "missingCertificateRejectedBeforeWrites",
                    "postBackupKeyPayloadRejected", "sourceBackupUnchanged")) and recovery.get("productionRecoveryVerified") is False,
                    "receipt_recipient_recovery_not_verified")
            require(recovery.get("businessHostConsumerVerified") is False and recovery.get("certificatePasswordIncludedInBackup") is False and
                    type(recovery.get("restoredEncryptedKeyCount")) is int and 1 <= recovery["restoredEncryptedKeyCount"] <= 32 and
                    all(sha(recovery.get(key), "") for key in ("backupArchiveSha256", "backupManifestSha256", "certificateSha256")),
                    "receipt_recovery_metadata_invalid")
            modules = True
        require(host is not None or keyring is not None, "receipt_no_supported_evidence")
    require(set(required_hosts) <= hosts, "required_host_evidence_missing")
    require(not require_modules or modules, "required_module_evidence_missing")
    return {"schemaVersion": 1, "status": "consistent", "sourceCommit": source, "ociRunId": oci_run,
            **binding, "verificationCommits": sorted(verification_commits), "hostScopes": sorted(hosts),
            "moduleProtectorComponentsVerified": modules, "receiptCount": len(receipts),
            "moduleEvidence": module_binding,
            "receiptAuthenticityVerified": False, "deploymentAuthorized": False, "deploymentVerified": False,
            "businessHostPipelineVerified": False, "externalChannelsVerified": False, "capacityVerified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--oci-run-id", required=True)
    parser.add_argument("--receipt", action="append", type=Path, required=True)
    parser.add_argument("--require-host", action="append", default=[], choices=("Testing/MySql", "Testing/SqlServer", "Production/MySql"))
    parser.add_argument("--require-modules", action="store_true")
    args = parser.parse_args()
    try:
        require(len(args.receipt) <= 16, "receipt_count_invalid")
        result = verify([load(path) for path in args.receipt], args.source_commit, args.oci_run_id,
                        args.require_host, args.require_modules)
    except EvidenceError as error:
        print(json.dumps({"status": "rejected", "code": str(error)}), file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError, TypeError, RecursionError):
        # 只输出稳定分类，不回显未受信回执的内容、路径或异常输入。
        print('{"status":"rejected","code":"candidate_evidence_invalid"}', file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
