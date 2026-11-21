"""用已导入的真实候选在隔离内网完成 MySQL 迁移、Host 健康检查和正常停止。"""
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request

SOCKET = "unix:///var/run/docker.sock"


def run(*arguments, timeout=60, input_text=None):
    try:
        return subprocess.check_output(["docker", "--host", SOCKET, *arguments], text=True,
                                       stderr=subprocess.STDOUT, timeout=timeout, input=input_text).strip()
    except subprocess.SubprocessError:
        # Docker exec 的环境参数含本轮密码，异常不能回显原始命令和数据库响应。
        raise RuntimeError("candidate_docker_command_failed") from None


def names():
    run_id = os.environ["GITHUB_RUN_ID"]
    if not run_id.isdigit():
        raise RuntimeError("startup_run_id_invalid")
    prefix = "fullnet-candidate-startup-" + run_id
    return prefix, {role: prefix + "-" + role for role in ("database", "redis", "migrator", "api", "worker")}


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
    """仅在隔离 CI 验证选定环境的启动，不把探针结果外推为现场或完整业务认证。"""
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
    environment = os.environ.get("HOST_ENVIRONMENT", "Testing")
    if environment not in ("Testing", "Production"):
        raise RuntimeError("candidate_host_environment_invalid")
    production = None
    if environment == "Production":
        import importlib.util
        spec = importlib.util.spec_from_file_location("production_assets", Path(__file__).with_name("oci_production_assets.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        production = module.prepare(root, containers["database"], containers["redis"], password)
    root.chmod(0o700)
    env = root / "host.env"
    env.write_text("\n".join([
        "DOTNET_ENVIRONMENT=" + environment, "ASPNETCORE_ENVIRONMENT=" + environment, "ASPNETCORE_URLS=http://+:8080",
        "Database__Provider=MySql", "Database__MySqlGuidStorageMode=Binary16",
        "Database__ConnectionString=Server=" + containers["database"] + ";Port=3306;Database=fullnet_candidate;User ID=root;Password=" + password +
        (";SslMode=VerifyFull;SslCa=/var/fullnet/pki/ca.crt" if production else ";SslMode=None;AllowPublicKeyRetrieval=True"),
        "Identity__AllowedOrigins__0=http://localhost", "Tenancy__HostDomains__0=localhost",
        "Realtime__Enabled=false", "Files__Local__RootPath=/tmp/files",
        "CodeGeneration__Artifacts__RootDirectory=/tmp/codegen",
    ]) + "\n")
    if not production:
        env.write_text(env.read_text() + "Identity__AllowDevelopmentEphemeralSigningKey=true\nIdentity__RequireSecureCookies=false\nDataProtection__KeyRingPath=/tmp/keys\n")
    else:
        # 云 Provider 的生产校验要求环境来源；这些仅为本轮占位，内网不允许实际调用。
        env.write_text(env.read_text() + "Files__S3__AccessKeyId=candidate-startup\nFiles__S3__SecretAccessKey=" + password +
                       "\nFiles__Oss__AccessKeyId=candidate-startup\nFiles__Oss__AccessKeySecret=" + password + "\n")
    env.chmod(0o600)
    database_env = root / "database.env"
    database_env.write_text("MYSQL_ROOT_PASSWORD=" + password + "\nMYSQL_ROOT_HOST=%\nMYSQL_DATABASE=fullnet_candidate\n")
    database_env.chmod(0o600)
    migrator_env = root / "migrator.env"

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
        redis_image = None
        if production:
            run("pull", "redis:7.4", timeout=180)
            redis_image = json.loads(run("image", "inspect", "redis:7.4"))[0]
        owned["network"] = network
        save()
        run("network", "create", "--internal", "--label", "fullnet.candidate-startup=" + network, network)
        start("database", "--memory", "1g", "--cpus", "2", "--pids-limit", "256", "--env-file", str(database_env),
              "--tmpfs", "/var/lib/mysql:rw,nosuid,nodev,size=512m", *(production["databaseArguments"] if production else []),
              database_image["Id"], "--log-bin-trust-function-creators=1", *(production["databaseOptions"] if production else []))
        if production:
            start("redis", "--memory", "128m", "--cpus", "1", "--pids-limit", "64", *production["redisArguments"],
                  redis_image["Id"], "redis-server", "/candidate-redis.conf")
        deadline = time.monotonic() + 120
        while True:
            try:
                # 官方镜像初始化时会启动禁用 TCP 的临时实例；只探测 TCP 正式实例，避免误报就绪。
                transport = ["--protocol=TCP", "-h", containers["database"]]
                if production:
                    transport += ["--ssl-mode=VERIFY_IDENTITY", "--ssl-ca=/candidate-pki/ca.crt"]
                value = run("exec", "-e", "MYSQL_PWD=" + password, containers["database"],
                            "mysql", "-uroot", *transport, "-Nse", "SELECT 1", timeout=5)
                if value == "1":
                    break
            except RuntimeError:
                pass
            if time.monotonic() >= deadline:
                raise RuntimeError("candidate_database_not_ready")
            time.sleep(1)
        mysql = ["exec", "-e", "MYSQL_PWD=" + password, containers["database"], "mysql", "-uroot", "-Nse"]
        if run(*mysql, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='fullnet_candidate'") != "0":
            raise RuntimeError("candidate_initial_database_not_empty")
        # 空库也必须提供真实可恢复备份；只在本轮数据库内恢复到独立校验库后启用迁移门禁。
        backup = run("exec", "-e", "MYSQL_PWD=" + password, containers["database"], "mysqldump", "-uroot",
                     "--no-tablespaces", "--set-gtid-purged=OFF", "fullnet_candidate")
        if not backup.startswith("-- MySQL dump") or len(backup) < 100:
            raise RuntimeError("candidate_initial_backup_invalid")
        run(*mysql, "CREATE DATABASE fullnet_backup_check")
        run("exec", "-i", "-e", "MYSQL_PWD=" + password, containers["database"], "mysql", "-uroot",
            "fullnet_backup_check", input_text=backup)
        if run(*mysql, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='fullnet_backup_check'") != "0":
            raise RuntimeError("candidate_initial_restore_mismatch")
        run(*mysql, "DROP DATABASE fullnet_backup_check")
        backup_digest = hashlib.sha256(backup.encode()).hexdigest()
        gates = []
        for section in ("UuidBinaryContract", "PreV1NamingContract"):
            for flag in ("MaintenanceMode", "BackupVerified", "LegacyWritersStopped"):
                gates.append(section + "__" + flag + "=true")
            gates.append(section + "__DestructiveDdlApprovalId=ci-empty-" + os.environ["GITHUB_RUN_ID"])
        gates.append("PreV1NamingContract__LegacyOutboxDrained=true")
        migrator_env.write_text(env.read_text() + "\n".join(gates) + "\n")
        migrator_env.chmod(0o600)
        shared = ["--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,mode=1777,size=128m",
                  "--tmpfs", "/tmp/codegen:rw,nosuid,nodev,uid=1654,gid=1654,mode=0700,size=16m", "--memory", "768m",
                  "--cpus", "1", "--pids-limit", "128", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                  "--env-file", str(env)]
        if production:
            shared += production["hostArguments"]
        def image(role):
            return "fullnet-oci-import-" + role + ":" + os.environ["GITHUB_RUN_ID"]
        start("migrator", *shared, "--env-file", str(migrator_env), image("migrator"))
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
        evidence = None
        if production:
            tls = run(*mysql, "SHOW GLOBAL STATUS LIKE 'Ssl_accepts'").split()
            if len(tls) != 2 or not tls[1].isdigit() or int(tls[1]) < 1:
                raise RuntimeError("candidate_mysql_tls_not_observed")
            evidence = {**production["evidence"], "mysqlObservedTlsAccepts": int(tls[1]),
                        "redisImageId": redis_image["Id"], "redisRepoDigests": redis_image["RepoDigests"]}
        return {"environment": environment, "provider": "MySql", "databaseImageId": database_image["Id"],
                "databaseRepoDigests": database_image["RepoDigests"], "migrationJournalCount": int(count),
                "initialEmptyDatabaseBackupSha256": backup_digest, "initialEmptyDatabaseRestoreVerified": True,
                "hosts": checks, "runtimeStartupVerified": True, "databaseMigrationVerified": True,
                "productionConfigurationVerified": bool(production), "productionEvidence": evidence}
    except Exception:
        # 只保留本轮有界尾日志，先替换随机凭据，避免将连接密码写入公开作业日志。
        for role in ("database", "redis", "migrator", "api", "worker"):
            if containers[role] in owned["containers"] and run("ps", "-aq", "--filter", "name=^/" + containers[role] + "$"):
                print("candidate_host_diagnostic: " + role + "\n" + run("logs", "--tail", "35", containers[role]).replace(password, "[redacted]")[-16384:], flush=True)
        raise
    finally:
        env.unlink(missing_ok=True)
        database_env.unlink(missing_ok=True)
        migrator_env.unlink(missing_ok=True)
