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
                "manifestDigest": SHA, "rootfsDiffIds": [SHA]} for role in ("api", "worker", "migrator")}}


def host(environment="Testing", provider="SqlServer"):
    result = base()
    result.update(runtimeStartupVerified=True, databaseMigrationVerified=True)
    result["hostVerification"] = {"environment": environment, "provider": provider, "runtimeStartupVerified": True,
        "databaseMigrationVerified": True, "initialEmptyDatabaseRestoreVerified": True, "migrationJournalCount": 397,
        "productionConfigurationVerified": environment == "Production", "hosts": {role: {
            "live": 200, "startup": 200, "ready": 200, "gracefulStopExitCode": 0} for role in ("api", "worker")}}
    if environment == "Production":
        result["hostVerification"]["productionEvidence"] = {"mysqlTlsMode": "VerifyFull", "mysqlSecureTransportRequired": True,
            "ephemeralSigningEnabled": False, "mysqlObservedTlsAccepts": 36}
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
            for content in ('{"a":1,"a":2}', '{"a":NaN}', ' ' * 262145):
                path.write_text(content)
                with self.assertRaises(ValueError):
                    module.load(path)

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
