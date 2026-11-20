"""复用正式缓存探针和运维编排器，在两个 Kind 工作节点与独立认证 Redis 中验收。"""
import base64
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid

from cluster_network_test import NODE_IMAGE, real_command

REPOSITORY = Path(__file__).resolve().parents[2]
VERIFIER = REPOSITORY / "eng/deploy/ubuntu/Verify-CacheBackplane.py"
PROJECT = REPOSITORY / "tests/Tools/Full.NET.CacheBackplaneProbe/Full.NET.CacheBackplaneProbe.csproj"
RUNTIME_IMAGE = "mcr.microsoft.com/dotnet/aspnet:10.0"
REDIS_IMAGE = "redis:8.6"


class CacheClusterRuntimeTests(unittest.TestCase):
    @classmethod
    def command(cls, arguments, seconds=180):
        try:
            return real_command(arguments, cls.environment, seconds)
        except RuntimeError as error:
            # 仅使用本轮合成凭据；即使 Kubernetes 错误回显清单，也不让凭据进入运行日志。
            message = str(error)
            for value in cls.secret_masks:
                message = message.replace(value, "[redacted]")
            raise RuntimeError(message) from None

    @classmethod
    def setUpClass(cls):
        if not sys.platform.startswith("linux") or os.environ.get("FULLNET_CACHE_CLUSTER") != "isolated":
            raise RuntimeError("真实缓存集群验收需要明确的隔离 Linux 入口")
        context = json.loads(real_command(["docker", "context", "inspect"]))[0]
        endpoint = context["Endpoints"]["docker"]["Host"]
        if not endpoint.startswith("unix://") or os.environ.get("DOCKER_HOST", endpoint) not in ("", endpoint):
            raise RuntimeError("拒绝操作远程或不一致的 Docker context")
        cls.temporary = tempfile.TemporaryDirectory(prefix="fullnet-cache-kind-")
        cls.root = Path(cls.temporary.name)
        cls.name = "fullnet-cache-" + uuid.uuid4().hex[:10]
        cls.password = uuid.uuid4().hex
        cls.connection = "redis:6379,password=" + cls.password + ",abortConnect=false"
        cls.redis_configuration = ("bind 0.0.0.0\nprotected-mode yes\nrequirepass " + cls.password +
                                   "\nsave \"\"\nappendonly no\ndir /tmp\nmaxmemory 64mb\nmaxmemory-policy noeviction\n")
        cls.secret_masks = [value for plain in (cls.password, cls.connection, cls.redis_configuration)
                            for value in (plain, base64.b64encode(plain.encode()).decode())]
        cls.created = False
        cls.environment = {**os.environ, "KUBECONFIG": str(cls.root / "kubeconfig"),
                           "DOTNET_CLI_HOME": str(cls.root / "dotnet-home"),
                           "NUGET_PACKAGES": str(cls.root / "nuget/packages"),
                           "NUGET_HTTP_CACHE_PATH": str(cls.root / "nuget/http"),
                           "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
        cls.addClassCleanup(cls.clean_cluster)
        cls.publish = cls.root / "publish"
        print("cache_cluster_stage: publishing official cache probe", flush=True)
        output = cls.command(["dotnet", "publish", PROJECT, "-c", "Release", "-r", "linux-x64",
                              "--self-contained", "false", "--artifacts-path", cls.root / "build",
                              "--output", cls.publish, "--disable-build-servers", "--nologo",
                              "-p:UseSharedCompilation=false", "-p:UseAppHost=false", "-m:2"], seconds=300)
        print(output[-2000:], flush=True)
        if cls.name in cls.command(["kind", "get", "clusters"]).split():
            raise RuntimeError("本轮集群名称已存在")
        configuration = cls.root / "kind.json"
        configuration.write_text(json.dumps({"kind": "Cluster", "apiVersion": "kind.x-k8s.io/v1alpha4",
                                            "nodes": [{"role": "control-plane"}, {"role": "worker"}, {"role": "worker"}]}))
        cls.created = True
        print("cache_cluster_stage: creating private two-worker cluster", flush=True)
        cls.command(["kind", "create", "cluster", "--name", cls.name, "--config", configuration,
                     "--image", NODE_IMAGE, "--kubeconfig", cls.environment["KUBECONFIG"], "--wait", "180s"], seconds=240)
        cls.command(["kubectl", "wait", "--for=condition=Ready", "nodes", "--all", "--timeout=180s"], seconds=195)
        for image in (RUNTIME_IMAGE, REDIS_IMAGE):
            cls.command(["docker", "pull", image], seconds=180)
            digest = json.loads(cls.command(["docker", "image", "inspect", image]))[0]["RepoDigests"]
            print("cache_cluster_image: " + json.dumps({"image": image, "repoDigests": digest}), flush=True)
            cls.command(["kind", "load", "docker-image", image, "--name", cls.name], seconds=180)

    @classmethod
    def clean_cluster(cls):
        try:
            if cls.created:
                cls.command(["kind", "delete", "cluster", "--name", cls.name], seconds=120)
                if cls.name in cls.command(["kind", "get", "clusters"]).split():
                    raise RuntimeError("本轮集群未删除")
                if cls.command(["docker", "ps", "-aq", "--filter", "label=io.x-k8s.kind.cluster=" + cls.name]).strip():
                    raise RuntimeError("本轮集群容器未回收")
                print("cache_cluster_cleanup: own_cluster_containers=0", flush=True)
        finally:
            cls.temporary.cleanup()

    def setUp(self):
        self.namespace = "cache-check-" + uuid.uuid4().hex[:12]
        self.sentinel = "fullnet:cluster-sentinel:" + uuid.uuid4().hex
        self.addCleanup(self.clean_namespace)
        self.command(["kubectl", "create", "namespace", self.namespace])
        self.receipt = self.root / (self.namespace + ".json")

    def clean_namespace(self):
        self.command(["kubectl", "delete", "namespace", self.namespace, "--ignore-not-found",
                      "--wait=true", "--timeout=60s"], seconds=75)
        if self.command(["kubectl", "get", "namespace", self.namespace, "--ignore-not-found", "-o", "json"]).strip():
            raise RuntimeError("本轮缓存命名空间未删除")

    def prepare(self, colocated=False):
        secret_name = "cache-fixture-auth"
        secret = {"apiVersion": "v1", "kind": "Secret", "metadata": {"name": secret_name, "namespace": self.namespace},
                  "type": "Opaque", "stringData": {"redis.conf": self.redis_configuration, "password": self.password,
                  "connection": self.connection}}
        service = {"apiVersion": "v1", "kind": "Service", "metadata": {"name": "redis", "namespace": self.namespace},
                   "spec": {"selector": {"app": "cache-fixture-redis"}, "ports": [{"port": 6379, "targetPort": 6379}]}}
        def pod(name, image, node, container, user):
            return {"apiVersion": "v1", "kind": "Pod", "metadata": {"name": name, "namespace": self.namespace,
                    "labels": {"app": "cache-fixture-redis"} if name == "redis" else
                              {"app.kubernetes.io/instance": "cache-fixture", "app.kubernetes.io/component": "api"}},
                    "spec": {"nodeName": node, "restartPolicy": "Never", "activeDeadlineSeconds": 600,
                             "terminationGracePeriodSeconds": 1, "automountServiceAccountToken": False,
                             "securityContext": {"runAsNonRoot": True, "runAsUser": user, "runAsGroup": user, "fsGroup": user,
                                                 "seccompProfile": {"type": "RuntimeDefault"}},
                             "volumes": [{"name": "temporary", "emptyDir": {"sizeLimit": "64Mi"}}],
                             "containers": [{"name": container, "image": image, "imagePullPolicy": "IfNotPresent",
                                             "securityContext": {"allowPrivilegeEscalation": False,
                                                "readOnlyRootFilesystem": True, "capabilities": {"drop": ["ALL"]}},
                                             "resources": {"requests": {"cpu": "50m", "memory": "64Mi"},
                                                           "limits": {"cpu": "500m", "memory": "256Mi"}},
                                             "volumeMounts": [{"name": "temporary", "mountPath": "/tmp"}]}]}}
        redis = pod("redis", REDIS_IMAGE, self.name + "-worker", "redis", 1000)
        redis["spec"]["volumes"].append({"name": "configuration", "secret": {"secretName": secret_name, "defaultMode": 0o440}})
        container = redis["spec"]["containers"][0]
        container.update({"args": ["redis-server", "/etc/cache-fixture/redis.conf"],
                          "env": [{"name": "REDISCLI_AUTH", "valueFrom": {"secretKeyRef": {"name": secret_name, "key": "password"}}}],
                          "readinessProbe": {"exec": {"command": ["redis-cli", "ping"]}, "periodSeconds": 1}})
        container["volumeMounts"].append({"name": "configuration", "mountPath": "/etc/cache-fixture", "readOnly": True})
        objects = [secret, service, redis]
        for name, node in (("api-a", self.name + "-worker"),
                           ("api-b", self.name + ("-worker" if colocated else "-worker2"))):
            current = pod(name, RUNTIME_IMAGE, node, "api", 1654)
            current["spec"]["containers"][0].update({"command": ["sleep", "600"], "env": [
                {"name": "Cache__RedisConnectionString", "valueFrom": {"secretKeyRef": {"name": secret_name, "key": "connection"}}},
                {"name": "Cache__ApplicationName", "value": self.namespace},
                {"name": "Cache__RedisOperationTimeout", "value": "00:00:05"},
                {"name": "DOTNET_EnableDiagnostics", "value": "0"},
                {"name": "NODE_NAME", "valueFrom": {"fieldRef": {"fieldPath": "spec.nodeName"}}}]})
            objects.append(current)
        manifest = self.root / (self.namespace + "-resources.json")
        manifest.write_text(json.dumps({"apiVersion": "v1", "kind": "List", "items": objects}))
        manifest.chmod(0o600)
        self.command(["kubectl", "create", "-f", manifest])
        self.command(["kubectl", "wait", "-n", self.namespace, "--for=condition=Ready",
                      "pod/redis", "pod/api-a", "pod/api-b", "--timeout=180s"], seconds=195)
        for name in ("api-a", "api-b"):
            self.command(["kubectl", "exec", "-n", self.namespace, name, "-c", "api", "--", "timeout", "--version"])
            self.command(["kubectl", "exec", "-n", self.namespace, name, "-c", "api", "--", "tar", "--version"])
        self.assertEqual(self.redis("SET", self.sentinel, "unchanged").strip(), "OK")

    def redis(self, *arguments):
        return self.command(["kubectl", "exec", "-n", self.namespace, "redis", "-c", "redis",
                             "--", "redis-cli", "--raw", *arguments])

    def verify(self):
        return subprocess.run(["timeout", "--kill-after=5s", "180s", sys.executable, "-O", str(VERIFIER),
                               str(self.publish), str(self.receipt), "--namespace", self.namespace,
                               "--release", "cache-fixture", "--container", "api", "--kubectl", "kubectl",
                               "--context", "kind-" + self.name], env=self.environment,
                              capture_output=True, text=True, timeout=190)

    def assert_no_remote_directories(self):
        for name in ("api-a", "api-b"):
            self.command(["kubectl", "exec", "-n", self.namespace, name, "-c", "api", "--", "sh", "-c",
                          'for directory in /tmp/fullnet-cache-verification-*; do [ ! -e "$directory" ] || exit 1; done'])

    def test_real_cross_node_cache_invalidation_refill_and_ttl_convergence(self):
        self.prepare()
        result = self.verify()
        self.assertNotIn(self.password, result.stdout + result.stderr)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["results"], 2)
        report = json.loads(self.receipt.read_text())
        self.assertEqual(report["outcome"], "passed")
        self.assertEqual(len({record["instance"] for record in report["results"]}), 2)
        self.assertEqual({record["node"] for record in report["results"]}, {self.name + "-worker", self.name + "-worker2"})
        for record in report["results"]:
            self.assertEqual(record["health"], "Healthy")
            self.assertTrue(record["l2Removal"] and record["authoritativeRefill"])
            self.assertEqual(record["l1TtlSeconds"], 120)
            self.assertLess(record["receivedInvalidationMilliseconds"][0], 5000)
        b = next(record for record in report["results"] if record["role"] == "b")
        self.assertGreater(b["missedNotificationConvergenceMilliseconds"], 0)
        self.assertLess(b["missedNotificationConvergenceMilliseconds"], 4000)
        self.assertEqual(self.redis("GET", self.sentinel).strip(), "unchanged")
        self.assertEqual(self.redis("--scan", "--pattern", "*fullnet:health:verification:" + report["run"] + ":*").strip(), "")
        self.assert_no_remote_directories()
        print("cache_cluster_real_receipt: " + json.dumps(report, sort_keys=True), flush=True)
        print("cache_cluster_isolation: sentinel=unchanged coordination_keys=0 remote_directories=0", flush=True)

    def test_real_same_node_pods_are_rejected_without_writing_probe_state(self):
        self.prepare(colocated=True)
        result = self.verify()
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, "")
        self.assertIn("不同节点", result.stderr)
        self.assertNotIn(self.password, result.stderr)
        self.assertFalse(self.receipt.exists())
        self.assertEqual(self.redis("GET", self.sentinel).strip(), "unchanged")
        self.assertEqual(self.redis("--scan", "--pattern", "*fullnet:health:verification:*").strip(), "")
        self.assert_no_remote_directories()
        print("cache_cluster_negative_control: colocated_pods=rejected probe_state=absent", flush=True)


if __name__ == "__main__":
    unittest.main()
