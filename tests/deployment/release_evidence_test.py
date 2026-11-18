"""隔离发布证据验收：受控 kubectl、真实进程/HTTP/文件系统，不连接集群。"""
import copy
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Finalize-Application.py"
spec = importlib.util.spec_from_file_location("fullnet_finalize", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture():
    deployments, pods = [], []
    for role in ("api", "worker"):
        labels = {"app.kubernetes.io/component": role, "app.kubernetes.io/instance": "fullnet"}
        image = f"registry.invalid/fullnet-{role}:candidate"
        deployments.append({"metadata": {"name": "fullnet-" + role, "uid": "deployment-" + role,
                                         "generation": 2, "labels": labels},
                            "spec": {"replicas": 2, "selector": {"matchLabels": labels},
                                     "template": {"spec": {"containers": [{"name": role, "image": image}]}}},
                            "status": {"observedGeneration": 2, "replicas": 2, "updatedReplicas": 2,
                                       "readyReplicas": 2, "availableReplicas": 2}})
        for number in (1, 2):
            pods.append({"metadata": {"name": f"{role}-{number}", "uid": f"uid-{role}-{number}", "labels": labels},
                         "spec": {"nodeName": f"node-{number}", "containers": [{"name": role, "image": image}]},
                         "status": {"phase": "Running", "conditions": [{"type": "Ready", "status": "True"}],
                                    "containerStatuses": [{"name": role, "imageID": "sha256:" + role,
                                                           "containerID": f"container-{role}-{number}",
                                                           "ready": True, "state": {"running": {}}, "restartCount": 0}]}})
    return {"deployments": {"items": deployments}, "pods": {"items": pods}}


class ReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fullnet-release-evidence-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.releases = self.root / "releases"
        self.release = self.releases / "candidate"
        self.frontend = self.release / "frontend"
        self.frontend.mkdir(parents=True)
        (self.frontend / "index.html").write_text("<html>candidate</html>")
        (self.frontend / "app.hash.js").write_text("/* candidate */")
        (self.release / "source-manifest.json").write_text('{"source":"frozen"}')
        (self.release / "chart.tar").write_bytes(b"fixed-chart")
        self.config = fixture()
        self.config_path = self.root / "fixture.json"
        self.trace = self.root / "commands.jsonl"
        self.lock = self.root / "release.lock"
        self.response = b"Healthy"
        self.http_status = 200
        self.requests = []
        owner = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                owner.requests.append(self.path)
                self.send_response(owner.http_status)
                self.send_header("Location", "http://127.0.0.1:1/forbidden")
                self.end_headers()
                self.wfile.write(owner.response)

            def log_message(self, *_):
                pass

        self.http = http.server.HTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.http.serve_forever)
        self.thread.start()
        self.addCleanup(self.close_http)
        self.kubectl = self.root / "kubectl"
        fake = """#!/usr/bin/env python3
import json, os, pathlib, signal, sys
config = json.loads(pathlib.Path(CONFIG).read_text())
trace = pathlib.Path(TRACE)
args = sys.argv[1:]
with trace.open("a") as output:
    output.write(json.dumps({"pid": os.getpid(), "args": args}) + "\\n")
if "port-forward" in args:
    if config.get("tunnel_failure"):
        print("untrusted-private-output", flush=True)
        sys.exit(1)
    print("Forwarding from 127.0.0.1:PORT -> 8080", flush=True)
    signal.pause()
    sys.exit(0)
kind = args[args.index("get") + 1]
count = pathlib.Path(CONFIG + "." + kind)
number = int(count.read_text()) if count.exists() else 0
count.write_text(str(number + 1))
if config.get("command_failure"):
    print("private-cluster-secret", file=sys.stderr)
    sys.exit(4)
if config.get("oversized"):
    sys.stdout.write("x" * (4 * 1024 * 1024 + 1))
    sys.exit(0)
if config.get("malformed"):
    print("{invalid-private-output")
    sys.exit(0)
print(json.dumps(config.get(kind + "_after", config[kind]) if number else config[kind]))
"""
        fake = fake.replace("CONFIG", repr(str(self.config_path))).replace("TRACE", repr(str(self.trace)))
        fake = fake.replace("PORT", str(self.http.server_port))
        self.kubectl.write_text(fake)
        self.kubectl.chmod(0o700)

    def close_http(self):
        self.http.shutdown()
        self.http.server_close()
        self.thread.join(3)
        self.assertFalse(self.thread.is_alive())

    def run_cli(self, *extra):
        self.config_path.write_text(json.dumps(self.config))
        result = subprocess.run([sys.executable, "-O", str(SCRIPT), str(self.release),
                                 "--release-root", str(self.releases), "--lock", str(self.lock),
                                 "--kubectl", str(self.kubectl),
                                 "--api-image", "registry.invalid/fullnet-api:candidate",
                                 "--worker-image", "registry.invalid/fullnet-worker:candidate", *map(str, extra)],
                                capture_output=True, text=True, timeout=30)
        if self.trace.exists():
            for line in self.trace.read_text().splitlines():
                item = json.loads(line)
                if "port-forward" in item["args"]:
                    with self.assertRaises(ProcessLookupError):
                        os.kill(item["pid"], 0)
        return result

    def failure(self, code):
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertEqual(json.loads(result.stderr), {"status": "failed", "code": code})
        self.assertFalse((self.release / "runtime-evidence").exists())
        self.assertEqual(list(self.release.glob(".runtime-evidence-*")), [])
        return result

    def test_seals_actual_runtime_frontend_and_private_checksums(self):
        result = self.run_cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["podsVerified"], 4)
        directory = self.release / "runtime-evidence"
        self.assertEqual(directory.stat().st_mode & 0o777, 0o700)
        for line in (directory / "SHA256SUMS").read_text().splitlines():
            checksum, name = line.split("  ")
            self.assertEqual(hashlib.sha256((directory / name).read_bytes()).hexdigest(), checksum)
            self.assertEqual((directory / name).stat().st_mode & 0o777, 0o600)
        runtime = json.loads((directory / "runtime.json").read_text())
        self.assertEqual(runtime["capacity"], "Capacity-not-verified")
        self.assertEqual({p["role"] for p in runtime["runtime"]["pods"]}, {"api", "worker"})
        inputs = json.loads((directory / "inputs.json").read_text())
        self.assertEqual(inputs["frontend"]["index.html"], hashlib.sha256((self.frontend / "index.html").read_bytes()).hexdigest())
        self.assertEqual(set(inputs["artifacts"]), {"chart.tar", "source-manifest.json"})
        self.assertEqual(self.requests, ["/health/ready"] * 4)
        self.assertFalse((self.release / "COMPLETE").exists())
        commands = [json.loads(line)["args"] for line in self.trace.read_text().splitlines()]
        self.assertTrue(all("get" in args or "port-forward" in args for args in commands))

    def test_old_evidence_is_never_overwritten(self):
        directory = self.release / "runtime-evidence"
        directory.mkdir()
        (directory / "old.txt").write_text("previous")
        result = self.run_cli()
        self.assertEqual(json.loads(result.stderr)["code"], "evidence_already_exists")
        self.assertEqual((directory / "old.txt").read_text(), "previous")
        self.assertFalse(self.trace.exists())

    def test_degraded_http200_is_not_success(self):
        self.response = b"Degraded"
        self.failure("pod_health_not_healthy")

    def test_oversized_health_body_is_not_success(self):
        self.response = b"Healthy" + b" " * 600
        self.failure("pod_health_not_healthy")

    def test_redirect_is_rejected_without_following(self):
        self.http_status = 302
        self.failure("evidence_capture_failed")
        self.assertEqual(self.requests, ["/health/ready"])

    def test_failed_health_http_status_is_rejected(self):
        self.http_status = 503
        self.failure("evidence_capture_failed")

    def test_tunnel_failure_does_not_echo_raw_output(self):
        self.config["tunnel_failure"] = True
        result = self.failure("health_tunnel_unavailable")
        self.assertNotIn("untrusted-private-output", result.stderr)

    def test_unobserved_generation(self):
        self.config["deployments"]["items"][0]["status"]["observedGeneration"] = 1
        self.failure("deployment_generation_not_observed")

    def test_partial_rollout(self):
        self.config["deployments"]["items"][0]["status"]["updatedReplicas"] = 1
        self.failure("deployment_rollout_incomplete")

    def test_wrong_candidate_image(self):
        self.config["deployments"]["items"][0]["spec"]["template"]["spec"]["containers"][0]["image"] = "old-image"
        self.failure("pod_image_or_container_invalid")

    def test_unknown_requested_candidate_image(self):
        result = self.run_cli("--api-image", "unpublished-candidate")
        self.assertEqual(json.loads(result.stderr)["code"], "candidate_image_mismatch")
        self.assertFalse(self.requests)

    def test_duplicate_role_deployment_rejected(self):
        self.config["deployments"]["items"].append(copy.deepcopy(self.config["deployments"]["items"][0]))
        self.failure("deployment_missing_or_duplicated")

    def test_missing_role_pod(self):
        self.config["pods"]["items"].pop()
        self.failure("pod_count_changed_or_terminating")

    def test_terminating_pod(self):
        self.config["pods"]["items"][0]["metadata"]["deletionTimestamp"] = "now"
        self.failure("pod_not_ready")

    def test_not_ready_pod(self):
        self.config["pods"]["items"][0]["status"]["conditions"][0]["status"] = "False"
        self.failure("pod_not_ready")

    def test_role_replicas_same_node_rejected(self):
        self.config["pods"]["items"][1]["spec"]["nodeName"] = "node-1"
        self.failure("role_replicas_not_on_distinct_nodes")

    def test_same_tag_different_pulled_image_ids_rejected(self):
        self.config["pods"]["items"][1]["status"]["containerStatuses"][0]["imageID"] = "sha256:other"
        self.failure("role_image_ids_differ")

    def test_pod_replaced_during_probe(self):
        self.config["pods_after"] = copy.deepcopy(self.config["pods"])
        self.config["pods_after"]["items"][0]["metadata"]["uid"] = "replacement"
        self.failure("runtime_identity_changed")

    def test_container_restarted_during_probe(self):
        self.config["pods_after"] = copy.deepcopy(self.config["pods"])
        self.config["pods_after"]["items"][0]["status"]["containerStatuses"][0]["containerID"] = "new-container"
        self.failure("runtime_identity_changed")

    def test_source_changed_during_probe(self):
        original = module.health
        def change(*_):
            (self.frontend / "index.html").write_text("changed")
            return {"status": "Healthy"}
        args = self.args()
        with mock.patch.object(module, "health", side_effect=change):
            with self.assertRaisesRegex(module.InvalidEvidence, "^release_inputs_changed$"):
                module.finalize(args)
        self.assertIs(module.health, original)
        self.assertFalse((self.release / "runtime-evidence").exists())

    def args(self):
        import argparse
        self.config_path.write_text(json.dumps(self.config))
        return argparse.Namespace(release_root=self.releases, release=self.release, frontend=None,
                                  lock=self.lock, kubectl=str(self.kubectl), namespace="fullnet-production",
                                  api_deployment="fullnet-api", worker_deployment="fullnet-worker",
                                  api_image="registry.invalid/fullnet-api:candidate",
                                  worker_image="registry.invalid/fullnet-worker:candidate", replicas=2, health_port=8080)

    def test_frontend_symlink_rejected(self):
        (self.frontend / "linked").symlink_to(self.root)
        self.failure("input_link_or_type_invalid")

    def test_hardlinked_artifact_rejected(self):
        os.link(self.release / "chart.tar", self.root / "linked.tar")
        self.failure("input_link_or_type_invalid")

    def test_missing_frontend_index_rejected(self):
        (self.frontend / "index.html").unlink()
        self.failure("frontend_index_missing")

    def test_missing_source_manifest_rejected(self):
        (self.release / "source-manifest.json").unlink()
        self.failure("release_artifact_missing")

    def test_release_path_escape_rejected_before_cluster_access(self):
        self.release = self.root
        self.failure("release_path_invalid")
        self.assertFalse(self.trace.exists())

    def test_external_frontend_rejected(self):
        result = self.run_cli("--frontend", self.root)
        self.assertEqual(json.loads(result.stderr)["code"], "frontend_path_invalid")
        self.assertFalse(self.trace.exists())

    def test_cluster_failure_is_sanitized(self):
        self.config["command_failure"] = True
        result = self.failure("cluster_command_failed")
        self.assertNotIn("private-cluster-secret", result.stderr)

    def test_malformed_cluster_json_rejected(self):
        self.config["malformed"] = True
        self.failure("evidence_capture_failed")

    def test_oversized_cluster_json_is_bounded(self):
        self.config["oversized"] = True
        self.failure("cluster_response_too_large")

    def test_publish_lock_blocks_evidence_capture(self):
        import fcntl
        with self.lock.open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.failure("release_in_progress")
        self.assertFalse(self.trace.exists())

    def test_seal_write_failure_leaves_no_output(self):
        args = self.args()
        with mock.patch.object(module.Path, "rename", side_effect=OSError("private-path-detail")):
            with self.assertRaises(OSError):
                module.finalize(args)
        self.assertFalse((self.release / "runtime-evidence").exists())
        self.assertEqual(list(self.release.glob(".runtime-evidence-*")), [])


if __name__ == "__main__":
    unittest.main()
