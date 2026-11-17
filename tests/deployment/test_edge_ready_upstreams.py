import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import call, patch


SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Refresh-Edge.py"
SPEC = importlib.util.spec_from_file_location("refresh_edge", SCRIPT)
EDGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EDGE)


def endpoint(address, **conditions):
    return {"addresses": [address], "conditions": conditions}


def slices(*endpoints):
    return {"items": [{"endpoints": endpoints}]}


class EdgeReadyUpstreamTests(unittest.TestCase):
    def setUp(self):
        directory = self.enterContext(tempfile.TemporaryDirectory())
        self.target = Path(directory) / "fullnet-upstream.conf"
        self.previous = EDGE.render_upstream(["10.42.0.88"]).encode("utf-8")

    def test_selects_unique_ready_serving_endpoints_only(self):
        payload = slices(
            endpoint("10.42.0.91", ready=True, serving=True),
            endpoint("10.42.0.90", ready=True),
            endpoint("10.42.0.90", ready=True),
            endpoint("10.42.0.92", ready=True, serving=False),
            endpoint("10.42.0.93", ready=True, terminating=True),
            endpoint("10.42.0.94", ready=False),
            endpoint("10.42.0.95"),
        )
        self.assertEqual(EDGE.ready_addresses(payload), ["10.42.0.90", "10.42.0.91"])
        with self.assertRaises(ValueError):
            EDGE.ready_addresses(slices(endpoint("10.43.0.10", ready=True)))

    def test_empty_snapshot_disables_stale_routes_and_recovery_reopens_proxy(self):
        self.target.write_bytes(self.previous)
        empty = json.dumps(slices(endpoint("10.42.0.88", ready=False)))
        recovered = json.dumps(slices(endpoint("10.42.0.97", ready=True, serving=True)))
        with (patch.object(EDGE.subprocess, "check_output", side_effect=[empty, empty, recovered]),
              patch.object(EDGE.subprocess, "run") as commands):
            self.assertEqual(EDGE.refresh(self.target), 0)
            disabled = self.target.read_text(encoding="utf-8")
            self.assertIn("server 127.0.0.1:1 down;", disabled)
            self.assertNotIn("10.42.0.88", disabled)
            self.assertEqual(EDGE.refresh(self.target), 0)
            self.assertEqual(EDGE.refresh(self.target), 1)
            self.assertEqual(commands.call_args_list, [
                call(["nginx", "-t"], check=True, timeout=15),
                call(["systemctl", "reload-or-restart", "nginx"], check=True, timeout=15),
            ] * 2)
        current = self.target.read_text(encoding="utf-8")
        self.assertIn("server 10.42.0.97:8080 max_fails=2 fail_timeout=5s;", current)
        self.assertIn("ip_hash;", current)
        self.assertIn("keepalive 32;", current)
        self.assertNotIn(" down;", current)

    def test_failed_endpoint_lookup_preserves_last_valid_config(self):
        self.target.write_bytes(self.previous)
        with (patch.object(EDGE.subprocess, "check_output", side_effect=subprocess.TimeoutExpired("k3s", 15)),
              patch.object(EDGE.subprocess, "run") as commands):
            with self.assertRaises(subprocess.TimeoutExpired):
                EDGE.refresh(self.target)
            commands.assert_not_called()
        self.assertEqual(self.target.read_bytes(), self.previous)

    def test_nginx_check_or_reload_failure_restores_previous_config(self):
        for stage in ("validation", "reload"):
            with self.subTest(stage=stage):
                self.target.write_bytes(self.previous)
                error = subprocess.CalledProcessError(1, stage)
                commands = [error] if stage == "validation" else [None, error]
                with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
                      patch.object(EDGE.subprocess, "run", side_effect=commands)):
                    with self.assertRaises(subprocess.CalledProcessError):
                        EDGE.refresh(self.target)
                self.assertEqual(self.target.read_bytes(), self.previous)
                self.assertEqual(list(self.target.parent.iterdir()), [self.target])

    def test_first_write_is_removed_when_nginx_validation_fails(self):
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "nginx"))):
            with self.assertRaises(subprocess.CalledProcessError):
                EDGE.refresh(self.target)
        self.assertEqual(list(self.target.parent.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
