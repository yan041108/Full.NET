"""复用现有 SQL Server 测试镜像，验证固定 OCI 候选的 Testing 启动闭环。"""
import json
import os
import re
import secrets
import time


def verify(root, run, network, containers, probe_hosts):
    if os.environ.get("HOST_ENVIRONMENT", "Testing") != "Testing":
        raise RuntimeError("sqlserver_candidate_testing_only")
    state = root / "host-owned.json"
    if state.exists() or run("network", "ls", "-q", "--filter", "name=^" + network + "$") or any(
            run("ps", "-aq", "--filter", "name=^/" + name + "$") for name in containers.values()):
        raise RuntimeError("startup_resources_already_exist")
    owned = {"network": None, "containers": []}
    password = "Fn9!" + secrets.token_hex(24)
    env, database_env, migrator_env = (root / name for name in ("host.env", "database.env", "migrator.env"))
    env.write_text("\n".join([
        "DOTNET_ENVIRONMENT=Testing", "ASPNETCORE_ENVIRONMENT=Testing", "ASPNETCORE_URLS=http://+:8080",
        "Database__Provider=SqlServer", "Database__CommandTimeoutSeconds=60",
        "Database__ConnectionString=Server=" + containers["database"] + ";Database=fullnet_candidate;User ID=sa;Password=" +
        password + ";Encrypt=True;TrustServerCertificate=True;Connect Timeout=5",
        "Identity__AllowDevelopmentEphemeralSigningKey=true", "Identity__RequireSecureCookies=false",
        "Identity__AllowedOrigins__0=http://localhost", "Tenancy__HostDomains__0=localhost", "Realtime__Enabled=false",
        "DataProtection__KeyRingPath=/tmp/keys", "Files__Local__RootPath=/tmp/files",
        "CodeGeneration__Artifacts__RootDirectory=/tmp/codegen",
    ]) + "\n")
    database_env.write_text("ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_MEMORY_LIMIT_MB=1024\nMSSQL_SA_PASSWORD=" + password + "\n")
    for path in (env, database_env):
        path.chmod(0o600)

    def save():
        state.write_text(json.dumps(owned))

    def start(role, *arguments):
        owned["containers"].append(containers[role])
        save()
        return run("run", "-d", "--name", containers[role], "--network", network,
                   "--label", "fullnet.candidate-startup=" + network, *arguments, timeout=90)

    try:
        reference = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
        run("pull", reference, timeout=300)
        database_image = json.loads(run("image", "inspect", reference))[0]
        owned["network"] = network
        save()
        run("network", "create", "--internal", "--label", "fullnet.candidate-startup=" + network, network)
        start("database", "--memory", "2304m", "--cpus", "2", "--pids-limit", "256", "--env-file", str(database_env),
              "--tmpfs", "/var/opt/mssql/data:rw,nosuid,nodev,uid=10001,gid=0,mode=0770,size=768m", database_image["Id"])
        sqlcmd = run("exec", containers["database"], "sh", "-c",
                     "if test -x /opt/mssql-tools18/bin/sqlcmd; then echo /opt/mssql-tools18/bin/sqlcmd; else echo /opt/mssql-tools/bin/sqlcmd; fi")

        def sql(query, timeout=60):
            return run("exec", "-e", "SQLCMDPASSWORD=" + password, containers["database"], sqlcmd,
                       "-S", "localhost", "-U", "sa", "-C", "-b", "-l", "3", "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + query,
                       timeout=timeout)

        deadline = time.monotonic() + 120
        while True:
            try:
                if sql("SELECT 1", timeout=5) == "1":
                    break
            except RuntimeError:
                pass
            if time.monotonic() >= deadline:
                raise RuntimeError("candidate_sqlserver_not_ready")
            time.sleep(1)
        sql("CREATE DATABASE fullnet_candidate")
        if sql("SELECT COUNT(*) FROM fullnet_candidate.sys.tables") != "0":
            raise RuntimeError("candidate_initial_database_not_empty")
        # 实际备份并恢复一次性空库后才供给 Contract 维护证据；不以 VERIFYONLY 替代恢复。
        backup = "/var/opt/mssql/data/candidate-initial.bak"
        sql("BACKUP DATABASE fullnet_candidate TO DISK=N'" + backup + "' WITH INIT,CHECKSUM; "
            "RESTORE VERIFYONLY FROM DISK=N'" + backup + "' WITH CHECKSUM; "
            "RESTORE DATABASE fullnet_backup_check FROM DISK=N'" + backup + "' WITH "
            "MOVE N'fullnet_candidate' TO N'/var/opt/mssql/data/backup-check.mdf', "
            "MOVE N'fullnet_candidate_log' TO N'/var/opt/mssql/data/backup-check.ldf'", timeout=120)
        if sql("SELECT COUNT(*) FROM fullnet_backup_check.sys.tables") != "0":
            raise RuntimeError("candidate_initial_restore_mismatch")
        sql("DROP DATABASE fullnet_backup_check")
        backup_digest = run("exec", containers["database"], "sha256sum", backup).split()[0]
        if not re.fullmatch(r"[0-9a-f]{64}", backup_digest):
            raise RuntimeError("candidate_initial_backup_digest_invalid")
        gates = []
        for section in ("UuidBinaryContract", "PreV1NamingContract"):
            for flag in ("MaintenanceMode", "BackupVerified", "LegacyWritersStopped"):
                gates.append(section + "__" + flag + "=true")
            gates.append(section + "__DestructiveDdlApprovalId=ci-empty-" + os.environ["GITHUB_RUN_ID"])
        gates.append("PreV1NamingContract__LegacyOutboxDrained=true")
        migrator_env.write_text(env.read_text() + "\n".join(gates) + "\n")
        migrator_env.chmod(0o600)
        shared = ["--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,mode=1777,size=128m", "--tmpfs",
                  "/tmp/codegen:rw,nosuid,nodev,uid=1654,gid=1654,mode=0700,size=16m", "--memory", "768m", "--cpus", "1",
                  "--pids-limit", "128", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--env-file", str(env)]

        def image(role):
            return "fullnet-oci-import-" + role + ":" + os.environ["GITHUB_RUN_ID"]

        start("migrator", *shared, "--env-file", str(migrator_env), image("migrator"))
        if run("wait", containers["migrator"], timeout=300) != "0":
            raise RuntimeError("candidate_migration_failed")
        count = sql("SELECT COUNT(*) FROM fullnet_candidate.dbo.SchemaVersions")
        if not count.isdigit() or int(count) == 0:
            raise RuntimeError("candidate_migration_journal_missing")
        checks = probe_hosts(containers, network, start, shared, image)
        return {"environment": "Testing", "provider": "SqlServer", "databaseImageId": database_image["Id"],
                "databaseRepoDigests": database_image["RepoDigests"], "migrationJournalCount": int(count),
                "initialEmptyDatabaseBackupSha256": backup_digest, "initialEmptyDatabaseRestoreVerified": True,
                "hosts": checks, "runtimeStartupVerified": True, "databaseMigrationVerified": True,
                "productionConfigurationVerified": False, "tlsServerIdentityVerified": False}
    except Exception:
        for role in ("database", "migrator", "api", "worker"):
            if containers[role] in owned["containers"] and run("ps", "-aq", "--filter", "name=^/" + containers[role] + "$"):
                print("candidate_host_diagnostic: " + role + "\n" + run("logs", "--tail", "35", containers[role]).replace(password, "[redacted]")[-16384:], flush=True)
        raise
    finally:
        for path in (env, database_env, migrator_env):
            path.unlink(missing_ok=True)
