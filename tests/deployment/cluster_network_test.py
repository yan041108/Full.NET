"""跨节点工具的隔离命令回归；真实 Kind 场景使用独立 kubeconfig 和唯一集群。"""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import uuid

REPOSITORY = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / "eng/deploy/ubuntu/Verify-Cluster.py"
LABEL = "fullnet.io/network-check-run"
IMAGE = "rancher/mirrored-library-busybox:1.37.0"
NODE_IMAGE = "kindest/node:v1.34.0@sha256:7416a61b42b1662ca6ca89f02028ac133a309a2a30ba309614e8ec94d976dc5a"

STUB = r'''#!/usr/bin/env python3
import copy,json,os,pathlib,sys,time
path=pathlib.Path(os.environ["CLUSTER_STATE"])
state=json.loads(path.read_text())
args=sys.argv[1:]
verb=next(v for v in args if v in ("get","create","wait","exec","delete"))
tail=args[args.index(verb)+1:]
payload=json.load(sys.stdin) if verb=="create" else None
with pathlib.Path(os.environ["CLUSTER_TRACE"]).open("a") as stream:
    stream.write(json.dumps({"args":args,"payload":payload})+"\n")
def save(): path.write_text(json.dumps(state))
def fail():
    print("private-kubeconfig-sentinel",file=sys.stderr)
    sys.exit(1)
label="fullnet.io/network-check-run"
if verb=="get" and tail[0]=="node":
    name=tail[1]
    calls=state.setdefault("node_calls",{})
    calls[name]=calls.get(name,0)+1;save()
    uid=name+"-uid"+("-changed" if state.get("node_changed") and calls[name]>1 else "")
    print(json.dumps({"metadata":{"name":name,"uid":uid},"spec":{"unschedulable":state.get("unschedulable",False)},
                      "status":{"conditions":[{"type":"Ready","status":"False" if state.get("node_not_ready") else "True"}]}}))
elif verb=="create" and payload["kind"]=="Namespace":
    payload["metadata"]["uid"]="namespace-uid"
    state["namespace"]=payload;state["pods"]={};save()
    if state.get("response_lost"): fail()
    print(json.dumps(payload))
elif verb=="create":
    if state.get("pod_create_failure") and payload["metadata"]["name"]=="client": fail()
    name=payload["metadata"]["name"]
    payload["metadata"]["uid"]=name+"-uid"
    if state.get("wrong_placement") and name=="client": payload["spec"]["nodeName"]="node-a"
    payload["status"]={"phase":"Running","podIP":"10.244.0.2" if name=="server" else "10.244.1.2",
                       "conditions":[{"type":"Ready","status":"True"}],
                       "containerStatuses":[{"name":"probe","ready":True,"state":{"running":{}},
                                             "containerID":name+"-container","imageID":"sha256:busybox-fixture"}]}
    if state.get("bad_ip"): payload["status"]["podIP"]="127.0.0.1"
    state["pods"][name]=payload;save()
    print(json.dumps(payload))
elif verb=="wait":
    if state.get("wait_failure"): fail()
elif verb=="get" and tail[0]=="pods":
    count=state.get("pod_reads",0);state["pod_reads"]=count+1;save()
    items=copy.deepcopy(list(state["pods"].values()))
    if state.get("pod_changed") and count: items[0]["metadata"]["uid"]+="-changed"
    print(json.dumps({"items":items}))
elif verb=="get" and tail[0]=="namespace":
    namespace=state.get("namespace")
    if namespace:
        if state.get("foreign_namespace"):
            namespace["metadata"]["uid"]="foreign-uid";state["namespace"]=namespace;save()
        print(json.dumps(namespace))
elif verb=="exec":
    if "nslookup" in tail:
        if state.get("dns_failure"): fail()
        print("Name: kubernetes.default.svc.cluster.local")
    else:
        if state.get("http_failure"): fail()
        if state.get("wait_signal"):
            pathlib.Path(os.environ["CLUSTER_WAIT"]).touch();time.sleep(30)
        print("wrong-result" if state.get("wrong_http") else state["namespace"]["metadata"]["labels"][label])
elif verb=="delete":
    if state.get("delete_failure"): fail()
    if not state.get("false_delete"): state["namespace"]=None;save()
else: fail()
'''


class ClusterNetworkCommandTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fullnet-cluster-command-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state = self.root / "state.json"
        self.state.write_text("{}")
        self.trace = self.root / "trace.jsonl"
        self.wait = self.root / "waiting"
        self.kubectl = self.root / "kubectl"
        self.kubectl.write_text(STUB)
        self.kubectl.chmod(0o700)
        self.env = {**os.environ, "CLUSTER_STATE": str(self.state), "CLUSTER_TRACE": str(self.trace), "CLUSTER_WAIT": str(self.wait)}

    def configure(self, **values):
        self.state.write_text(json.dumps(values))

    def execute(self, first="node-a", second="node-b", *extra):
        return subprocess.run([sys.executable, "-O", str(SCRIPT), first, second, "--kubectl", str(self.kubectl), *extra],
                              env=self.env, capture_output=True, text=True, timeout=10)

    def calls(self):
        return [json.loads(line) for line in self.trace.read_text().splitlines()] if self.trace.exists() else []

    def failure(self):
        result = self.execute()
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, "")
        self.assertNotIn("private-kubeconfig-sentinel", result.stderr)
        self.assertEqual(json.loads(result.stderr)["status"], "failed")
        return json.loads(result.stderr)["code"]

    def test_success_uses_owned_namespace_distinct_nodes_and_confirmed_cleanup(self):
        result = self.execute("node-a", "node-b", "--context", "explicit-context")
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertTrue(report["namespaceDeleted"] and report["dnsResolved"] and report["crossNodeHttp"])
        self.assertNotEqual(report["pods"]["server"]["node"], report["pods"]["client"]["node"])
        self.assertIsNone(json.loads(self.state.read_text())["namespace"])
        calls = self.calls()
        self.assertTrue(all("--context" in call["args"] for call in calls))
        self.assertIn("delete", calls[-2]["args"])
        self.assertIn("--ignore-not-found", calls[-1]["args"])
        for call in calls:
            payload = call["payload"]
            if payload and payload["kind"] == "Pod":
                self.assertFalse(payload["spec"]["automountServiceAccountToken"])
                self.assertTrue(payload["spec"]["securityContext"]["runAsNonRoot"])
                self.assertEqual(payload["spec"]["containers"][0]["securityContext"]["capabilities"]["drop"], ["ALL"])

    def test_same_node_is_rejected_before_any_command(self):
        result = self.execute("node-a", "node-a")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stderr)["code"], "distinct_nodes_required")
        self.assertEqual(self.calls(), [])

    def test_not_ready_node_prevents_namespace_creation(self):
        self.configure(node_not_ready=True)
        self.assertEqual(self.failure(), "node_not_ready_or_schedulable")
        self.assertFalse(any(call["payload"] for call in self.calls()))

    def test_unschedulable_node_prevents_namespace_creation(self):
        self.configure(unschedulable=True)
        self.failure()
        self.assertFalse(any(call["payload"] for call in self.calls()))

    def test_node_replacement_during_probe_fails_and_cleans(self):
        self.configure(node_changed=True)
        self.assertTrue(self.failure().startswith("node_changed_during_check:"))
        self.assertIsNone(json.loads(self.state.read_text())["namespace"])

    def test_namespace_create_response_loss_still_cleans(self):
        self.configure(response_lost=True)
        self.assertTrue(self.failure().startswith("namespace_create_failed:"))
        self.assertIsNone(json.loads(self.state.read_text())["namespace"])

    def test_partial_pod_creation_still_cleans(self):
        self.configure(pod_create_failure=True)
        self.failure()
        self.assertIsNone(json.loads(self.state.read_text())["namespace"])

    def test_wrong_pod_node_fails(self):
        self.configure(wrong_placement=True)
        self.assertTrue(self.failure().startswith("pod_identity_or_readiness_invalid:"))

    def test_pod_replacement_during_check_fails(self):
        self.configure(pod_changed=True)
        self.assertTrue(self.failure().startswith("pod_changed_during_check:"))

    def test_dns_failure_is_not_http_success(self):
        self.configure(dns_failure=True)
        self.failure()
        self.assertFalse(any("wget" in call["args"] for call in self.calls()))

    def test_wrong_http_payload_is_rejected(self):
        self.configure(wrong_http=True)
        self.assertTrue(self.failure().startswith("cross_node_http_payload_invalid:"))

    def test_http_command_failure_is_redacted(self):
        self.configure(http_failure=True)
        self.failure()

    def test_failed_delete_prevents_success(self):
        self.configure(delete_failure=True)
        self.assertTrue(self.failure().startswith("namespace_cleanup_not_confirmed:"))
        self.assertIsNotNone(json.loads(self.state.read_text())["namespace"])

    def test_false_delete_is_detected_by_final_read(self):
        self.configure(false_delete=True)
        self.assertTrue(self.failure().startswith("namespace_cleanup_not_confirmed:"))

    def test_changed_namespace_uid_is_not_deleted(self):
        self.configure(foreign_namespace=True)
        self.failure()
        self.assertFalse(any("delete" in call["args"] for call in self.calls()))
        self.assertEqual(json.loads(self.state.read_text())["namespace"]["metadata"]["uid"], "foreign-uid")

    def test_loopback_pod_address_is_rejected(self):
        self.configure(bad_ip=True)
        self.assertTrue(self.failure().startswith("pod_address_invalid:"))

    def test_signal_cleans_namespace_without_success(self):
        self.configure(wait_signal=True)
        with subprocess.Popen(["bash", str(SCRIPT.with_suffix(".sh")), "node-a", "node-b", "--kubectl", str(self.kubectl)],
                              env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) as process:
            deadline = time.monotonic() + 5
            while not self.wait.exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(.02)
            self.assertTrue(self.wait.exists())
            process.terminate()
            output, error = process.communicate(timeout=5)
            self.assertEqual(process.returncode, 1)
            self.assertEqual(output, "")
        self.assertIsNone(json.loads(self.state.read_text())["namespace"])


def real_command(arguments, environment=None, seconds=180):
    result = subprocess.run(["timeout", "--kill-after=5s", str(seconds) + "s", *map(str, arguments)],
                            env=environment, capture_output=True, text=True, timeout=seconds + 10)
    if result.returncode:
        raise RuntimeError(str(arguments[0]) + ": " + (result.stdout + result.stderr)[-4000:])
    return result.stdout


class ClusterNetworkRealTests(unittest.TestCase):
    def test_two_real_kind_workers_dns_http_and_namespace_removal(self):
        self.assertEqual(os.environ.get("FULLNET_CLUSTER_REAL"), "isolated")
        self.assertTrue(sys.platform.startswith("linux"))
        context = json.loads(real_command(["docker", "context", "inspect"]))[0]
        self.assertTrue(context["Endpoints"]["docker"]["Host"].startswith("unix://"))
        self.assertIn(os.environ.get("DOCKER_HOST", context["Endpoints"]["docker"]["Host"]),
                      ("", context["Endpoints"]["docker"]["Host"]))
        name = "fullnet-network-" + uuid.uuid4().hex[:10]
        self.assertNotIn(name, real_command(["kind", "get", "clusters"]).split())
        with tempfile.TemporaryDirectory(prefix="fullnet-kind-network-") as work:
            root = Path(work)
            configuration = root / "kind.json"
            configuration.write_text(json.dumps({"kind": "Cluster", "apiVersion": "kind.x-k8s.io/v1alpha4",
                                                "nodes": [{"role": "control-plane"}, {"role": "worker"}, {"role": "worker"}]}))
            env = {**os.environ, "KUBECONFIG": str(root / "kubeconfig")}
            try:
                print("cluster_network_stage: creating isolated kind cluster", flush=True)
                real_command(["kind", "create", "cluster", "--name", name, "--config", configuration,
                              "--image", NODE_IMAGE, "--kubeconfig", env["KUBECONFIG"], "--wait", "180s"], env, seconds=240)
                real_command(["kubectl", "wait", "--for=condition=Ready", "nodes", "--all", "--timeout=180s"], env, seconds=195)
                print("cluster_network_stage: loading probe image", flush=True)
                real_command(["docker", "pull", IMAGE], seconds=120)
                digest = json.loads(real_command(["docker", "image", "inspect", IMAGE]))[0]["RepoDigests"]
                print("cluster_network_probe_image: " + json.dumps(digest), flush=True)
                real_command(["kind", "load", "docker-image", IMAGE, "--name", name], env, seconds=120)
                output = real_command(["bash", SCRIPT.with_suffix(".sh"), name + "-worker", name + "-worker2",
                                       "--kubectl", "kubectl", "--context", "kind-" + name, "--image", IMAGE], env, seconds=300)
                report = json.loads(output)
                self.assertEqual(report["status"], "passed")
                self.assertTrue(report["namespaceDeleted"] and report["dnsResolved"] and report["crossNodeHttp"])
                self.assertEqual(report["pods"]["server"]["node"], name + "-worker")
                self.assertEqual(report["pods"]["client"]["node"], name + "-worker2")
                remaining = json.loads(real_command(["kubectl", "get", "namespaces", "-l", LABEL + "=" + report["runId"], "-o", "json"], env))
                self.assertEqual(remaining["items"], [])
                print("cluster_network_real_receipt: " + json.dumps(report, sort_keys=True), flush=True)
            finally:
                real_command(["kind", "delete", "cluster", "--name", name], env, seconds=120)
                self.assertNotIn(name, real_command(["kind", "get", "clusters"]).split())
                containers = real_command(["docker", "ps", "-aq", "--filter", "label=io.x-k8s.kind.cluster=" + name]).strip()
                self.assertEqual(containers, "")
                print("cluster_network_cleanup: own_cluster_containers=0", flush=True)


if __name__ == "__main__":
    unittest.main()
