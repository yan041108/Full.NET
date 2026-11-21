"""为隔离候选启动准备真实密钥、证书和配置，不消费或导出生产凭据。"""
import hashlib
import json
import os
import subprocess


def prepare(root, database, redis, password):
    private = root / "production-private"
    private.mkdir(mode=0o700)
    database_pki, host_pki = private / "database", private / "host"
    database_pki.mkdir(mode=0o755)
    host_pki.mkdir(mode=0o755)

    def openssl(*args, input_text=None):
        try:
            return subprocess.check_output(["openssl", *map(str, args)], text=True, input=input_text,
                                           stderr=subprocess.PIPE, timeout=30)
        except subprocess.SubprocessError:
            raise RuntimeError("candidate_certificate_generation_failed") from None

    ca_key, ca = private / "ca.key", database_pki / "ca.crt"
    openssl("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", ca_key, "-out", ca,
            "-days", "1", "-subj", "/CN=Full.NET.Candidate.CA", "-addext", "basicConstraints=critical,CA:TRUE")
    key, request, certificate = database_pki / "server.key", private / "server.csr", database_pki / "server.crt"
    openssl("req", "-newkey", "rsa:2048", "-nodes", "-keyout", key, "-out", request, "-subj", "/CN=" + database)
    extension = private / "server.ext"
    extension.write_text("subjectAltName=DNS:" + database + "\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n")
    openssl("x509", "-req", "-in", request, "-CA", ca, "-CAkey", ca_key, "-CAcreateserial", "-out", certificate,
            "-days", "1", "-extfile", extension)
    openssl("verify", "-CAfile", ca, "-verify_hostname", database, certificate)
    host_ca = host_pki / "ca.crt"
    host_ca.write_bytes(ca.read_bytes())
    protection_key, protection_certificate = private / "protection.key", private / "protection.crt"
    openssl("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", protection_key, "-out", protection_certificate,
            "-days", "1", "-subj", "/CN=Full.NET.Candidate.DataProtection")
    protection = host_pki / "protection.pfx"
    openssl("pkcs12", "-export", "-inkey", protection_key, "-in", protection_certificate, "-out", protection,
            "-passout", "stdin", input_text=password + "\n")
    signing = private / "signing.key"
    openssl("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:3072", "-out", signing)
    public_key = openssl("pkey", "-in", signing, "-pubout")
    keyring = private / "keyring"
    # 额外组只用于本轮共享挂载；根目录仍为 0700，容器主 UID 保持 1654。
    keyring.mkdir(mode=0o770)
    keyring.chmod(0o770)
    settings = {
        "DataProtection": {"KeyRingPath": "/var/fullnet/keyring", "CertificatePath": "/var/fullnet/pki/protection.pfx",
                           "CertificatePassword": password},
        "Identity": {"AllowDevelopmentEphemeralSigningKey": False, "RequireSecureCookies": True,
                     "EnableRemoteSuperAdministratorManagement": False, "ActiveKeyId": "candidate",
                     "SigningKeys": {"candidate": {"PrivateKeyPem": signing.read_text(), "PublicKeyPem": public_key}}},
        "Cache": {"RedisConnectionString": redis + ":6379,password=" + password + ",abortConnect=false"},
        "Realtime": {"Enabled": False, "AllowSharedRedisInDevelopment": False},
        "Files": {"Storage": {"DefaultProviderKey": "s3"},
                  "S3": {"BucketName": "candidate-startup", "Region": "us-east-1",
                         "AccessKeyId": "candidate-startup", "SecretAccessKey": password},
                  "Oss": {"BucketName": "candidate-startup", "Endpoint": "https://oss.example.invalid",
                          "AccessKeyId": "candidate-startup", "AccessKeySecret": password}},
    }
    configuration = private / "appsettings.Production.json"
    configuration.write_text(json.dumps(settings))
    redis_configuration = private / "redis.conf"
    redis_configuration.write_text('bind 0.0.0.0\nprotected-mode yes\nrequirepass ' + password + '\nsave ""\nappendonly no\n')
    # 外层任务目录 0700 限制宿主访问；只读挂载仅向各容器提供必要的子集。
    for path in (*database_pki.iterdir(), *host_pki.iterdir(), configuration, redis_configuration):
        path.chmod(0o444)
    return {
        "databaseArguments": ["--mount", "type=bind,source=" + str(database_pki) + ",target=/candidate-pki,readonly"],
        "databaseOptions": ["--ssl-ca=/candidate-pki/ca.crt", "--ssl-cert=/candidate-pki/server.crt",
                            "--ssl-key=/candidate-pki/server.key", "--require-secure-transport=ON"],
        "hostArguments": ["--mount", "type=bind,source=" + str(host_pki) + ",target=/var/fullnet/pki,readonly",
                          "--mount", "type=bind,source=" + str(keyring) + ",target=/var/fullnet/keyring",
                          "--group-add", str(os.getgid()), "--mount",
                          "type=bind,source=" + str(configuration) + ",target=/app/appsettings.Production.json,readonly"],
        "redisArguments": ["--mount", "type=bind,source=" + str(redis_configuration) + ",target=/candidate-redis.conf,readonly"],
        "evidence": {"mysqlTlsMode": "VerifyFull", "mysqlSecureTransportRequired": True,
                     "mysqlCaSha256": hashlib.sha256(ca.read_bytes()).hexdigest(),
                     "dataProtectionCertificateSha256": hashlib.sha256(protection.read_bytes()).hexdigest(),
                     "ephemeralSigningEnabled": False, "sharedCertificateProtectedKeyRingConfigured": True,
                     "cloudProviderCallsVerified": False},
    }
