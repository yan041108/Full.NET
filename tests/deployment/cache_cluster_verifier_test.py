"""以受控 kubectl 传输验证编排和拒绝边界，不连接集群或冒充真实缓存传播。"""
import contextlib
import copy
import importlib.util
import io
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Verify-CacheBackplane.py"
SPEC = importlib.util.spec_from_file_location("cache_cluster_verifier", SCRIPT)
verifier = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = verifier
SPEC.loader.exec_module(verifier)


def pod(name, node):
    return {"metadata": {"name": name, "uid": "uid-" + name,
            "labels": {"app.kubernetes.io/instance": "fullnet-api", "app.kubernetes.io/component": "api"}},
            "spec": {"nodeName": node, "containers": [{"name": "api"}]},
            "status": {"phase": "Running", "conditions": [{"type": "Ready", "status": "True"}]}}


class FakeKubectl:
    def __init__(self):
        self.pods = [pod("api-a", "node-a"), pod("api-b", "node-b")]
        self.calls = []
        self.mutate_receipt = lambda role, record: record
        self.inventory_reads = 0
        self.replace_after_run = False
        self.unready_after_run = False
        self.fail_copy = False
        self.fail_marker_cleanup = False
        self.fail_first_directory_cleanup = False
        self.timeout_role = None
        self.invalid_json = False
        self.on_final_inventory = None

    def __call__(self, args, **options):
        self.calls.append(args)
        value, code = "", 0
        if "get" in args:
            position = args.index("get")
            if args[position + 1] == "pods":
                self.inventory_reads += 1
                if self.inventory_reads == 2 and self.replace_after_run:
                    self.pods[1]["metadata"]["uid"] = "replacement"
                if self.inventory_reads == 2 and self.unready_after_run:
                    self.pods[1]["status"]["conditions"][0]["status"] = "False"
                if self.inventory_reads == 2 and self.on_final_inventory:
                    self.on_final_inventory()
                value = json.dumps({"items": self.pods})
            else:
                value = json.dumps(next(item for item in self.pods if item["metadata"]["name"] == args[position + 2]))
        elif "cp" in args:
            code = 1 if self.fail_copy and args[-1].startswith("api-b:") else 0
        elif "exec" in args:
            command = args[args.index("--") + 1:]
            if command[0] == "timeout" and command[-1] in ("a", "b"):
                role, run = command[-1], command[-2]
                if role == self.timeout_role:
                    raise subprocess.TimeoutExpired(args, options["timeout"], output="secret-sentinel")
                record = {"role": role, "run": run, "processId": 77, "instance": "instance-" + role,
                    "l1TtlSeconds": 120, "receivedInvalidationMilliseconds": [12.5], "l2Removal": True,
                    "authoritativeRefill": True, "missedNotificationConvergenceMilliseconds": None if role == "a" else 1000,
                    "health": "Healthy", "node": "untrusted-node", "unknown": "secret-sentinel"}
                value = json.dumps(self.mutate_receipt(role, record))
                if self.invalid_json and role == "a":
                    value = "{secret-sentinel"
            elif command[-1] == "cleanup":
                code = 1 if self.fail_marker_cleanup else 0
            elif command[0] == "rm":
                code = 1 if self.fail_first_directory_cleanup and args[args.index("exec") + 1] == "api-a" else 0
        return subprocess.CompletedProcess(args, code, value, "secret-sentinel" if code else "")

    def removals(self):
        return [args for args in self.calls if "--" in args and args[args.index("--") + 1] == "rm"]


class ClusterVerifierTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="fullnet-cache-driver-test-")
        self.addCleanup(self.directory.cleanup)
        root = pathlib.Path(self.directory.name)
        self.publish, self.output = root / "publish", root / "receipt.json"
        self.publish.mkdir()
        for suffix in ("dll", "deps.json", "runtimeconfig.json"):
            (self.publish / ("Full.NET.CacheBackplaneProbe." + suffix)).write_text("fixture", encoding="utf-8")
        self.transport = FakeKubectl()

    def run_driver(self, *extra):
        stdout, stderr = io.StringIO(), io.StringIO()
        args = [str(self.publish), str(self.output), "--namespace", "isolated-test", "--context", "fixture-context", *extra]
        with mock.patch.object(verifier.subprocess, "run", side_effect=self.transport), \
                contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            code = verifier.main(args)
        self.assertNotIn("secret-sentinel", stdout.getvalue() + stderr.getvalue())
        return code

    def test_success_checks_both_roles_and_preserves_namespace_identity_and_hard_deadline(self):
        self.assertEqual(0, self.run_driver("--kubectl", "kubectl"))
        report = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual("passed", report["outcome"])
        self.assertEqual(["node-a", "node-b"], [item["node"] for item in report["results"]])
        self.assertEqual(["uid-api-a", "uid-api-b"], [item["podUid"] for item in report["results"]])
        # 不同 Pod 的 PID 可以相同；独立性由 Pod UID、节点与缓存实例共同证明。
        self.assertEqual([77, 77], [item["processId"] for item in report["results"]])
        self.assertNotIn("unknown", report["results"][0])
        self.assertTrue(all(args[:5] == ["kubectl", "--context", "fixture-context", "--namespace", "isolated-test"] for args in self.transport.calls))
        role_calls = [args for args in self.transport.calls if "70s" in args]
        self.assertEqual(2, len(role_calls))
        self.assertTrue(all("--kill-after=5s" in args and "--container" in args for args in role_calls))
        self.assertEqual(2, len(self.transport.removals()))

    def test_existing_receipt_is_never_overwritten_or_contacted(self):
        self.output.write_text("keep", encoding="utf-8")
        self.assertEqual(1, self.run_driver())
        self.assertEqual("keep", self.output.read_text(encoding="utf-8"))
        self.assertEqual([], self.transport.calls)

    def test_incomplete_publish_is_rejected_before_cluster_access(self):
        (self.publish / "Full.NET.CacheBackplaneProbe.runtimeconfig.json").unlink()
        self.assertEqual(1, self.run_driver())
        self.assertEqual([], self.transport.calls)

    def test_publish_links_are_rejected_without_copying_external_content(self):
        external = pathlib.Path(self.directory.name) / "outside.txt"
        external.write_text("keep", encoding="utf-8")
        (self.publish / "linked.txt").symlink_to(external)
        self.assertEqual(1, self.run_driver())
        self.assertEqual([], self.transport.calls)
        self.assertEqual("keep", external.read_text(encoding="utf-8"))

    def test_unstable_or_unowned_inventory_has_no_mutations(self):
        mutations = [
            lambda rows: rows.append(copy.deepcopy(rows[0])),
            lambda rows: rows[1]["spec"].update(nodeName="node-a"),
            lambda rows: rows[1]["metadata"].update(deletionTimestamp="now"),
            lambda rows: rows[1]["metadata"]["labels"].update({"app.kubernetes.io/instance": "other"}),
            lambda rows: rows[1]["status"]["conditions"][0].update(status="False"),
            lambda rows: rows[1]["status"].update(conditions=None),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutations.index(mutate)):
                self.transport = FakeKubectl()
                mutate(self.transport.pods)
                self.assertEqual(1, self.run_driver())
                self.assertFalse(any("exec" in args or "cp" in args for args in self.transport.calls))
                self.assertFalse(self.output.exists())

    def test_invalid_receipts_fail_with_cleanup_and_without_success_output(self):
        changes = [{"run": "other"}, {"role": "wrong"}, {"processId": True}, {"instance": "same"},
                   {"l1TtlSeconds": 1}, {"l2Removal": "true"}, {"authoritativeRefill": False},
                   {"health": "Degraded"}, {"receivedInvalidationMilliseconds": []},
                   {"receivedInvalidationMilliseconds": [float("nan")]},
                   {"receivedInvalidationMilliseconds": [10 ** 1000]},
                   {"missedNotificationConvergenceMilliseconds": 0}]
        for change in changes:
            with self.subTest(field=list(change)):
                self.transport = FakeKubectl()
                self.transport.mutate_receipt = lambda role, row: dict(row, **change)
                self.assertEqual(1, self.run_driver())
                self.assertEqual(2, len(self.transport.removals()))
                self.assertFalse(self.output.exists())

    def test_replaced_pod_does_not_receive_old_identity_cleanup(self):
        self.transport.replace_after_run = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(1, len(self.transport.removals()))
        self.assertEqual("api-a", self.transport.removals()[0][self.transport.removals()[0].index("exec") + 1])
        self.assertFalse(self.output.exists())

    def test_unready_owned_pod_still_receives_its_directory_cleanup(self):
        self.transport.unready_after_run = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(self.output.exists())

    def test_partial_copy_cleans_both_owned_directories_without_running_missing_probe(self):
        self.transport.fail_copy = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(any(args[-1] == "cleanup" for args in self.transport.calls))

    def test_role_timeout_is_redacted_and_both_directories_are_cleaned(self):
        self.transport.timeout_role = "a"
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(self.output.exists())

    def test_marker_cleanup_failure_does_not_skip_directory_cleanup_or_publish_success(self):
        self.transport.fail_marker_cleanup = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(self.output.exists())

    def test_first_directory_cleanup_failure_does_not_skip_the_second(self):
        self.transport.fail_first_directory_cleanup = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(self.output.exists())

    def test_namespace_is_required_instead_of_defaulting_to_production(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
            verifier.main([str(self.publish), str(self.output)])
        self.assertEqual(2, error.exception.code)
        self.assertEqual([], self.transport.calls)

    def test_malformed_json_is_redacted_and_does_not_sign_a_receipt(self):
        self.transport.invalid_json = True
        self.assertEqual(1, self.run_driver())
        self.assertEqual(2, len(self.transport.removals()))
        self.assertFalse(self.output.exists())

    def test_receipt_created_during_verification_is_preserved(self):
        self.transport.on_final_inventory = lambda: self.output.write_text("keep-new-owner", encoding="utf-8")
        self.assertEqual(1, self.run_driver())
        self.assertEqual("keep-new-owner", self.output.read_text(encoding="utf-8"))
        self.assertEqual(2, len(self.transport.removals()))


if __name__ == "__main__":
    unittest.main()
