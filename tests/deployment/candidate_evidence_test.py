"""固定候选回执聚合的身份漂移、能力边界与输入拒绝检查。"""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Verify-CandidateEvidence.py"
spec = importlib.util.spec_from_file_location("candidate_evidence", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SOURCE, RUN = "a" * 40, "123456"
SHA = "sha256:" + "b" * 64


def base():
    return {"schemaVersion": 1, "scope": "linux-oci-import", "sourceCommit": SOURCE, "ociRunId": RUN,
            "packagingCommit": "c" * 40, "verificationCommit": "d" * 40, "candidateReceiptSha256": SHA,
            "imageImportVerified": True, "runtimeDiscoveryVerified": True, "runtimeStartupVerified": False,
            "databaseMigrationVerified": False, "deploymentVerified": False, "nativeAotVerified": False,
            "capacityVerified": False, "roles": {role: {"imageId": SHA, "archiveSha256": SHA,
                "manifestDigest": SHA, "rootfsDiffIds": [SHA], "runtimeDiscovery": [
                    "Microsoft.NETCore.App 10.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]",
                    "Microsoft.AspNetCore.App 10.0.1 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]"]}
                for role in ("api", "worker", "migrator")}}


def host(environment="Testing", provider="SqlServer"):
    result = base()
    result.update(runtimeStartupVerified=True, databaseMigrationVerified=True)
    result["hostVerification"] = {"environment": environment, "provider": provider, "runtimeStartupVerified": True,
        "databaseMigrationVerified": True, "initialEmptyDatabaseRestoreVerified": True, "migrationJournalCount": 397,
        "productionConfigurationVerified": environment == "Production", "hosts": {role: {
            "live": 200, "startup": 200, "ready": 200, "gracefulStopExitCode": 0} for role in ("api", "worker")}}
    result["hostVerification"].update(databaseImageId=SHA, databaseRepoDigests=["fixture@" + SHA],
                                      initialEmptyDatabaseBackupSha256="b" * 64)
    if environment == "Production":
        result["hostVerification"]["productionEvidence"] = {"mysqlTlsMode": "VerifyFull", "mysqlSecureTransportRequired": True,
            "ephemeralSigningEnabled": False, "mysqlObservedTlsAccepts": 36, "mysqlCaSha256": "b" * 64,
            "dataProtectionCertificateSha256": "b" * 64, "redisImageId": SHA, "redisRepoDigests": ["fixture@" + SHA],
            "sharedCertificateProtectedKeyRingConfigured": True, "cloudProviderCallsVerified": False}
    return result


def modules():
    result = base()
    result["keyRingVerification"] = {"mode": "consumers", "businessHostConsumerVerified": False, "consumers": {
        "moduleProtectorComponentsVerified": True, "businessHostPipelineVerified": False, "externalDeliveryVerified": False,
        "paymentChannelVerified": False, "nativeAotVerified": False, "genericProbePurposeRejected": True,
        "paymentPurposesBothDirectionsRejected": True, "protectors": {name: {"apiToWorkerVerified": True,
            "workerToApiVerified": True, "differentConsumerPurposeRejected": True} for name in
            ("notification", "ldap", "oauth", "totp", "payment-api", "payment-private")},
        "notificationRecipientMediaRecovery": {**{key: True for key in ("originalPayloadRestoredVerified",
            "restoredRoleRoundTripVerified", "corruptedBackupRejectedBeforeWrites", "missingCertificateRejectedBeforeWrites",
            "postBackupKeyPayloadRejected", "sourceBackupUnchanged")}, "productionRecoveryVerified": False}}}
    result["keyRingVerification"].update(scope="independent-processes-in-candidate-images", environment="Production",
        nativeAotVerified=False, candidateHostingSha256="b" * 64, apiToWorkerVerified=True, workerToApiVerified=True,
        freshProcessReloadVerified=True)
    consumers = result["keyRingVerification"]["consumers"]
    consumers.update(scope="actual-module-protector-components-in-independent-processes", moduleAssemblies={role: {
        "Full.NET.Modules." + name + ".dll": "b" * 64 for name in ("Identity", "Notifications", "Payments")} for role in ("api", "worker")})
    consumers["notificationRecipientMediaRecovery"].update(businessHostConsumerVerified=False, certificatePasswordIncludedInBackup=False,
        restoredEncryptedKeyCount=1, backupArchiveSha256="b" * 64, backupManifestSha256="b" * 64, certificateSha256="b" * 64)
    return result


class CandidateEvidenceTests(unittest.TestCase):
    def test_same_candidate_accepts_distinct_verification_commits_without_promoting_deployment(self):
        first, second = host(), host("Production", "MySql")
        second["verificationCommit"] = "e" * 40
        result = module.verify([first, second, modules()], SOURCE, RUN, ("Testing/SqlServer", "Production/MySql"), True)
        self.assertEqual(["Production/MySql", "Testing/SqlServer"], result["hostScopes"])
        self.assertEqual(2, len(result["verificationCommits"]))
        self.assertFalse(result["deploymentAuthorized"])
        self.assertFalse(result["receiptAuthenticityVerified"])

    def test_candidate_and_role_identity_drift_rejected(self):
        for field, value in (("sourceCommit", "f" * 40), ("ociRunId", "999"), ("candidateReceiptSha256", "sha256:" + "f" * 64)):
            changed = host()
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                module.verify([host(), changed], SOURCE, RUN)
        for field in ("imageId", "archiveSha256", "manifestDigest", "rootfsDiffIds"):
            changed = host()
            changed["roles"]["api"][field] = ["sha256:" + "f" * 64] if field == "rootfsDiffIds" else "sha256:" + "f" * 64
            with self.subTest(field=field), self.assertRaises(ValueError):
                module.verify([host(), changed], SOURCE, RUN)

    def test_required_scopes_cannot_be_inferred(self):
        with self.assertRaisesRegex(ValueError, "required_host_evidence_missing"):
            module.verify([host()], SOURCE, RUN, ("Production/MySql",))
        with self.assertRaisesRegex(ValueError, "required_module_evidence_missing"):
            module.verify([host()], SOURCE, RUN, require_modules=True)

    def test_boolean_cannot_impersonate_numeric_exit_or_journal(self):
        for mutate in (lambda v: v["hostVerification"].update(migrationJournalCount=True),
                       lambda v: v["hostVerification"]["hosts"]["api"].update(gracefulStopExitCode=False)):
            value = host()
            mutate(value)
            with self.assertRaises(ValueError):
                module.verify([value], SOURCE, RUN)

    def test_unsupported_claims_and_production_tls_relaxation_rejected(self):
        value = host()
        value["deploymentVerified"] = True
        with self.assertRaises(ValueError):
            module.verify([value], SOURCE, RUN)
        value = host("Production", "MySql")
        value["hostVerification"]["productionEvidence"]["mysqlTlsMode"] = "None"
        with self.assertRaises(ValueError):
            module.verify([value], SOURCE, RUN)

    def test_partial_protector_or_recovery_evidence_rejected(self):
        first, second = modules(), modules()
        del first["keyRingVerification"]["consumers"]["protectors"]["totp"]
        second["keyRingVerification"]["consumers"]["notificationRecipientMediaRecovery"]["postBackupKeyPayloadRejected"] = False
        for value in (first, second):
            with self.assertRaises(ValueError):
                module.verify([value], SOURCE, RUN, require_modules=True)

    def test_duplicate_nonfinite_and_oversized_json_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "receipt.json"
            for content in ('{"a":1,"a":2}', '{"a":NaN}', '{"a":1e309}', '{"a":-1e309}', ' ' * 262145):
                path.write_text(content)
                with self.assertRaises(ValueError):
                    module.load(path)

    def test_role_runtime_proof_cannot_be_missing_or_wrong_major(self):
        first, second = host(), host()
        del first["roles"]["api"]["runtimeDiscovery"]
        second["roles"]["api"]["runtimeDiscovery"][0] = "Microsoft.NETCore.App 9.0.1 [/runtime]"
        for value in (first, second):
            with self.assertRaisesRegex(ValueError, "receipt_runtime_proof_invalid"):
                module.verify([value], SOURCE, RUN)

    def test_database_and_production_sources_require_metadata(self):
        first, second = host(), host("Production", "MySql")
        del first["hostVerification"]["initialEmptyDatabaseBackupSha256"]
        del second["hostVerification"]["productionEvidence"]["redisRepoDigests"]
        for value in (first, second):
            with self.assertRaises(ValueError):
                module.verify([value], SOURCE, RUN)

    def test_module_inventory_missing_or_conflicting_rejected(self):
        first, second = modules(), modules()
        del first["keyRingVerification"]["consumers"]["moduleAssemblies"]["api"]["Full.NET.Modules.Identity.dll"]
        with self.assertRaisesRegex(ValueError, "receipt_module_inventory_invalid"):
            module.verify([first], SOURCE, RUN)
        second["keyRingVerification"]["consumers"]["moduleAssemblies"]["api"]["Full.NET.Modules.Identity.dll"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "receipt_module_binding_mismatch"):
            module.verify([modules(), second], SOURCE, RUN)

    def test_recovery_hash_and_numeric_key_count_required(self):
        first, second = modules(), modules()
        del first["keyRingVerification"]["consumers"]["notificationRecipientMediaRecovery"]["certificateSha256"]
        second["keyRingVerification"]["consumers"]["notificationRecipientMediaRecovery"]["restoredEncryptedKeyCount"] = True
        for value in (first, second):
            with self.assertRaisesRegex(ValueError, "receipt_recovery_metadata_invalid"):
                module.verify([value], SOURCE, RUN)

    def test_cli_rejects_without_disclosing_untrusted_contents(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "receipt.json"
            path.write_text(json.dumps({"secret": "must-never-appear"}))
            result = subprocess.run([sys.executable, str(SCRIPT), "--source-commit", SOURCE, "--oci-run-id", RUN,
                                     "--receipt", str(path)], capture_output=True, text=True)
            self.assertEqual(1, result.returncode)
            self.assertEqual("", result.stdout)
            self.assertNotIn("must-never-appear", result.stderr)
            self.assertEqual("receipt_schema_invalid", json.loads(result.stderr)["code"])


if __name__ == "__main__":
    unittest.main()
