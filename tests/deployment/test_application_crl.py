import importlib.util
import ipaddress
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Manage-ApplicationCrl.py"
SPEC = importlib.util.spec_from_file_location("application_crl", SCRIPT)
APP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(APP)


def create_pki(root):
    certificates = root / "certificates"
    secrets = root / "secrets"
    certificates.mkdir(parents=True)
    secrets.mkdir()
    ca, key = certificates / "application-ca.pem", secrets / "application-ca.key"
    APP.openssl("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "30", "-subj", "/CN=CRL test CA",
                "-keyout", key, "-out", ca)
    APP.openssl("req", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=FullNET-web",
                "-keyout", certificates / "web.key", "-out", certificates / "web.csr")
    (certificates / "web.extensions").write_text("subjectAltName=IP:127.0.0.1,DNS:ubuntu-24-01\nextendedKeyUsage=serverAuth\n")
    APP.openssl("x509", "-req", "-in", certificates / "web.csr", "-CA", ca, "-CAkey", key,
                "-days", "7", "-extfile", certificates / "web.extensions", "-out", certificates / "web.pem")
    return certificates, ca, key


class ApplicationCrlTests(unittest.TestCase):
    def setUp(self):
        directory = self.enterContext(tempfile.TemporaryDirectory())
        self.root, self.public = Path(directory) / "fullnet", Path(directory) / "public"
        self.enterContext(patch.multiple(APP, ROOT=self.root, PUBLIC=self.public))
        self.certificates, self.ca, self.key = create_pki(self.root)
        self.address = ipaddress.IPv4Address("127.0.0.1")
        self.web = self.certificates / "web.pem"
        self.extensions = self.certificates / "web.extensions"
        self.crl = self.public / "application.crl"
        self.ca_config = self.root / "application-pki" / "ca.cnf"
        self.service_calls = []
        self.reload_fails = False
        run = subprocess.run

        def controlled_run(args, **kwargs):
            if args[0] == "openssl":
                return run(args, **kwargs)
            self.service_calls.append(args)
            if self.reload_fails and args[0] == "systemctl":
                raise subprocess.CalledProcessError(1, args)
            return subprocess.CompletedProcess(args, 0)

        self.enterContext(patch.object(APP.subprocess, "run", controlled_run))

    def verify(self, certificate):
        return APP.openssl("verify", "-CAfile", self.ca, "-CRLfile", self.crl, "-crl_check", "-verify_ip", str(self.address), certificate)

    def test_initial_switch_and_retry_preserve_key_and_other_public_files(self):
        self.public.mkdir()
        mysql = self.public / "mysql.crl"
        mysql.write_bytes(b"existing MySQL CRL")
        original_key, original_ca = (self.certificates / "web.key").read_bytes(), self.ca.read_bytes()
        old = self.web.read_bytes()
        APP.manage(self.address)
        self.assertNotEqual(self.web.read_bytes(), old)
        self.assertEqual((self.certificates / "web.key").read_bytes(), original_key)
        self.assertEqual(self.ca.read_bytes(), original_ca)
        self.assertEqual(mysql.read_bytes(), b"existing MySQL CRL")
        self.assertIn("http://127.0.0.1/pki/application.crl", APP.openssl("x509", "-in", self.web, "-noout", "-ext", "crlDistributionPoints"))
        self.assertIn("ubuntu-24-01", APP.openssl("x509", "-in", self.web, "-noout", "-ext", "subjectAltName"))
        self.verify(self.web)
        active = self.web.read_bytes()
        APP.manage(self.address)
        self.assertEqual(self.web.read_bytes(), active)
        self.assertEqual(self.service_calls, [["nginx", "-t"], ["systemctl", "reload", "nginx"]] * 2)

    def test_failed_reload_restores_previous_certificate_and_template(self):
        self.reload_fails = True
        old_web, old_extensions = self.web.read_bytes(), self.extensions.read_bytes()
        with self.assertRaises(subprocess.CalledProcessError):
            APP.manage(self.address)
        self.assertEqual(self.web.read_bytes(), old_web)
        self.assertEqual(self.extensions.read_bytes(), old_extensions)
        self.reload_fails = False
        APP.manage(self.address)
        self.verify(self.web)

    def test_refresh_retains_revocation_and_later_issuance_uses_same_distribution_point(self):
        APP.manage(self.address)
        APP.openssl("ca", "-config", self.ca_config, "-revoke", self.web)
        APP.manage(None)
        with self.assertRaisesRegex(subprocess.CalledProcessError, "non-zero") as failure:
            self.verify(self.web)
        self.assertIn("certificate revoked", failure.exception.stderr)
        replacement = self.certificates / "web-next.pem"
        APP.openssl("x509", "-req", "-in", self.certificates / "web.csr", "-CA", self.ca, "-CAkey", self.key,
                    "-days", "7", "-extfile", self.extensions, "-out", replacement)
        self.verify(replacement)
        self.assertIn("http://127.0.0.1/pki/application.crl", APP.openssl("x509", "-in", replacement, "-noout", "-ext", "crlDistributionPoints"))

    def test_failed_refresh_or_missing_ledger_keeps_last_publication(self):
        APP.manage(self.address)
        prior = self.crl.read_bytes()
        self.key.rename(self.key.with_suffix(".missing"))
        with self.assertRaises(subprocess.CalledProcessError):
            APP.manage(None)
        self.assertEqual(self.crl.read_bytes(), prior)
        self.ca_config.parent.rename(self.ca_config.parent.with_suffix(".missing"))
        with self.assertRaisesRegex(ValueError, "缺少吊销账本"):
            APP.manage(self.address)
        self.assertEqual(self.crl.read_bytes(), prior)


if __name__ == "__main__":
    unittest.main()
