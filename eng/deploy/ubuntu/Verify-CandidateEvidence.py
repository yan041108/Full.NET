#!/usr/bin/env python3
"""只读核对固定 OCI 候选的 Host/模块验收回执，不签发部署授权或现场成功。"""
import argparse
import json
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
    return json.loads(content.decode("utf-8"), object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("receipt_nonfinite")))


def sha(value, prefix="sha256:"):
    return isinstance(value, str) and re.fullmatch(re.escape(prefix) + r"[0-9a-f]{64}", value) is not None


def verify(receipts, source, oci_run, required_hosts=(), require_modules=False):
    """只接受已知范围；同一绑定内不同验收源码合法，不同应用/镜像来源必须拒绝。"""
    require(re.fullmatch(r"[0-9a-f]{40}", source) and re.fullmatch(r"[1-9][0-9]{0,14}", oci_run), "expected_identity_invalid")
    require(1 <= len(receipts) <= 16, "receipt_count_invalid")
    binding, hosts, modules = None, set(), False
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
            hosts.add(scope)
        keyring = receipt.get("keyRingVerification")
        if keyring is not None:
            require(isinstance(keyring, dict) and keyring.get("mode") == "consumers" and
                    keyring.get("businessHostConsumerVerified") is False, "receipt_module_scope_unsupported")
            consumers = keyring.get("consumers")
            require(isinstance(consumers, dict) and consumers.get("moduleProtectorComponentsVerified") is True and
                    all(consumers.get(key) is False for key in ("businessHostPipelineVerified", "externalDeliveryVerified",
                                                               "paymentChannelVerified", "nativeAotVerified")), "receipt_modules_not_verified")
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
            modules = True
        require(host is not None or keyring is not None, "receipt_no_supported_evidence")
    require(set(required_hosts) <= hosts, "required_host_evidence_missing")
    require(not require_modules or modules, "required_module_evidence_missing")
    return {"schemaVersion": 1, "status": "consistent", "sourceCommit": source, "ociRunId": oci_run,
            **binding, "verificationCommits": sorted(verification_commits), "hostScopes": sorted(hosts),
            "moduleProtectorComponentsVerified": modules, "receiptCount": len(receipts),
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
