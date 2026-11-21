"""用已导入的真实候选在隔离内网完成 MySQL 迁移、Host 健康检查和正常停止。"""
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request

SOCKET = "unix:///var/run/docker.sock"


def run(*arguments, timeout=60):
    try:
        return subprocess.check_output(["docker", "--host", SOCKET, *arguments], text=True,
                                       stderr=subprocess.STDOUT, timeout=timeout).strip()
    except subprocess.SubprocessError:
        # Docker exec 的环境参数含本轮密码，异常不能回显原始命令和数据库响应。
        raise RuntimeError("candidate_docker_command_failed") from None


def names():
    run_id = os.environ["GITHUB_RUN_ID"]
    if not run_id.isdigit():
        raise RuntimeError("startup_run_id_invalid")
    prefix = "fullnet-candidate-startup-" + run_id
    return prefix, {role: prefix + "-" + role for role in ("database", "migrator", "api", "worker")}


def clean(root):
    """只回收预先登记且带本轮标签的独占资源，绝不执行全局 prune。"""
    network, containers = names()
    state = root / "host-owned.json"
    if not state.exists():
        return
    owned = json.loads(state.read_text())
    if owned.get("network") not in (None, network) or not set(owned["containers"]) <= set(containers.values()):
        raise RuntimeError("startup_cleanup_ownership_invalid")
    for name in reversed(owned["containers"]):
        if run("ps", "-aq", "--filter", "name=^/" + name + "$"):
            resource = json.loads(run("inspect", name))[0]
            if resource["Config"]["Labels"].get("fullnet.candidate-startup") != network:
                raise RuntimeError("startup_cleanup_label_invalid")
            run("rm", "-f", "-v", name)
    if owned["network"] and run("network", "ls", "-q", "--filter", "name=^" + network + "$"):
        resource = json.loads(run("network", "inspect", network))[0]
        if resource["Labels"].get("fullnet.candidate-startup") != network:
            raise RuntimeError("startup_cleanup_label_invalid")
        run("network", "rm", network)
    if run("ps", "-aq", "--filter", "label=fullnet.candidate-startup=" + network) or run(
            "network", "ls", "-q", "--filter", "label=fullnet.candidate-startup=" + network):
        raise RuntimeError("startup_cleanup_remaining")
    print("candidate_startup_cleanup: containers=0 networks=0", flush=True)


def verify(root):
    """仅在隔离 CI 使用 Testing 配置，不把探针结果外推为生产或完整业务认证。"""
    network, containers = names()
    if os.environ.get("CI") != "true" or not Path("/var/run/docker.sock").is_socket():
        raise RuntimeError("startup_isolated_ci_required")
    owned = {"network": None, "containers": []}
    state = root / "host-owned.json"
    if state.exists() or run("network", "ls", "-q", "--filter", "name=^" + network + "$"):
        raise RuntimeError("startup_resources_already_exist")
    for name in containers.values():
        if run("ps", "-aq", "--filter", "name=^/" + name + "$"):
            raise RuntimeError("startup_resources_already_exist")
    password = "Fn9!" + secrets.token_hex(24)
    root.chmod(0o700)
    env = root / "host.env"
    env.write_text("\n".join([
        "DOTNET_ENVIRONMENT=Testing", "ASPNETCORE_ENVIRONMENT=Testing", "ASPNETCORE_URLS=http://+:8080",
        "Database__Provider=MySql", "Database__MySqlGuidStorageMode=Binary16",
        "Database__ConnectionString=Server=" + containers["database"] + ";Port=3306;Database=fullnet_candidate;User ID=root;Password=" + password + ";SslMode=None;AllowPublicKeyRetrieval=True",
        "Identity__AllowDevelopmentEphemeralSigningKey=true", "Identity__RequireSecureCookies=false",
        "Identity__AllowedOrigins__0=http://localhost", "Tenancy__HostDomains__0=localhost",
        "Realtime__Enabled=false", "DataProtection__KeyRingPath=/tmp/keys", "Files__Local__RootPath=/tmp/files",
        "CodeGeneration__Artifacts__RootDirectory=/tmp/codegen",
    ]) + "\n")
    env.chmod(0o600)
    database_env = root / "database.env"
    database_env.write_text("MYSQL_ROOT_PASSWORD=" + password + "\nMYSQL_ROOT_HOST=%\nMYSQL_DATABASE=fullnet_candidate\n")
    database_env.chmod(0o600)

    def save():
        state.write_text(json.dumps(owned))

    def start(role, *arguments):
        name = containers[role]
        owned["containers"].append(name)
        save()
        return run("run", "-d", "--name", name, "--label", "fullnet.candidate-startup=" + network,
                   "--network", network, *arguments, timeout=90)

    try:
        # 拉取仅供一次性数据库使用的镜像，实际摘要写入回执；内网创建后禁止容器外联。
        run("pull", "mysql:8.0", timeout=240)
        database_image = json.loads(run("image", "inspect", "mysql:8.0"))[0]
        owned["network"] = network
        save()
        run("network", "create", "--internal", "--label", "fullnet.candidate-startup=" + network, network)
        start("database", "--memory", "1g", "--cpus", "2", "--pids-limit", "256", "--env-file", str(database_env),
              "--tmpfs", "/var/lib/mysql:rw,nosuid,nodev,size=512m", database_image["Id"],
              "--log-bin-trust-function-creators=1")
        deadline = time.monotonic() + 120
        while True:
            try:
                value = run("exec", "-e", "MYSQL_PWD=" + password, containers["database"],
                            "mysql", "-uroot", "-Nse", "SELECT 1", timeout=5)
                if value == "1":
                    break
            except RuntimeError:
                pass
            if time.monotonic() >= deadline:
                raise RuntimeError("candidate_database_not_ready")
            time.sleep(1)
        shared = ["--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,mode=1777,size=128m", "--memory", "768m",
                  "--cpus", "1", "--pids-limit", "128", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                  "--env-file", str(env)]
        def image(role):
            return "fullnet-oci-import-" + role + ":" + os.environ["GITHUB_RUN_ID"]
        start("migrator", *shared, image("migrator"))
        if run("wait", containers["migrator"], timeout=240) != "0":
            raise RuntimeError("candidate_migration_failed")
        count = run("exec", "-e", "MYSQL_PWD=" + password, containers["database"], "mysql", "-uroot", "-Nse",
                    "SELECT COUNT(*) FROM fullnet_candidate.schemaversions")
        if not count.isdigit() or int(count) == 0:
            raise RuntimeError("candidate_migration_journal_missing")
        checks = {}
        client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        for role in ("api", "worker"):
            start(role, *shared, image(role))
            deadline = time.monotonic() + 90
            address = json.loads(run("inspect", containers[role]))[0]["NetworkSettings"]["Networks"][network]["IPAddress"]
            for endpoint in ("live", "startup", "ready"):
                while True:
                    if not json.loads(run("inspect", containers[role]))[0]["State"]["Running"]:
                        raise RuntimeError("candidate_host_exited_" + role)
                    try:
                        with client.open("http://" + address + ":8080/health/" + endpoint, timeout=3) as response:
                            if response.status == 200 and response.read(64) == b"Healthy":
                                break
                    except (OSError, urllib.error.URLError):
                        pass
                    if time.monotonic() >= deadline:
                        raise RuntimeError("candidate_health_failed_" + role + "_" + endpoint)
                    time.sleep(1)
            checks[role] = {"live": 200, "startup": 200, "ready": 200}
            print("candidate_host_verified: role=" + role + " health=live+startup+ready", flush=True)
        for role in ("worker", "api"):
            run("stop", "--time", "20", containers[role], timeout=30)
            status = json.loads(run("inspect", containers[role]))[0]["State"]
            if status["Running"] or status["ExitCode"] != 0 or status["OOMKilled"]:
                raise RuntimeError("candidate_host_shutdown_failed_" + role)
            checks[role]["gracefulStopExitCode"] = 0
        return {"environment": "Testing", "provider": "MySql", "databaseImageId": database_image["Id"],
                "databaseRepoDigests": database_image["RepoDigests"], "migrationJournalCount": int(count),
                "hosts": checks, "runtimeStartupVerified": True, "databaseMigrationVerified": True,
                "productionConfigurationVerified": False}
    except Exception:
        # 只保留本轮有界尾日志，先替换随机凭据，避免将连接密码写入公开作业日志。
        for role in ("migrator", "api", "worker"):
            if containers[role] in owned["containers"] and run("ps", "-aq", "--filter", "name=^/" + containers[role] + "$"):
                print("candidate_host_diagnostic: " + role + "\n" + run("logs", "--tail", "35", containers[role]).replace(password, "[redacted]"), flush=True)
        raise
    finally:
        env.unlink(missing_ok=True)
        database_env.unlink(missing_ok=True)
