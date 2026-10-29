"""Exercise local credential initialization without touching the developer's .env.local."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import base64
import hashlib
import hmac
import json
import time

ROOT = Path(__file__).resolve().parents[2]
POWERSHELL = shutil.which("pwsh") or shutil.which("powershell")


@unittest.skipUnless(POWERSHELL, "PowerShell is required to exercise the local initializer")
class LocalEnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.file = Path(self.temp.name) / ".env.local"

    def run_initializer(self):
        return subprocess.run([POWERSHELL, "-NoProfile", "-File", str(ROOT / "scripts/Initialize-LocalEnvironment.ps1"),
                               "-EnvFile", str(self.file)], capture_output=True, text=True, timeout=30)

    def test_initialization_generates_distinct_credentials_without_printing_them(self):
        result = self.run_initializer()
        self.assertEqual(0, result.returncode, result.stderr)
        values = dict(line.split("=", 1) for line in self.file.read_text(encoding="utf-8").splitlines())
        password, secret = values["OPERATION_POSTGRES_PASSWORD"], values["OPERATION_JWT_SECRET"]
        self.assertEqual(48, len(password)); self.assertEqual(96, len(secret))
        self.assertNotEqual(password, secret[:48])
        self.assertNotIn(password, result.stdout + result.stderr)
        self.assertNotIn(secret, result.stdout + result.stderr)

    def test_repeated_initialization_preserves_credentials_and_optional_settings(self):
        self.assertEqual(0, self.run_initializer().returncode)
        with self.file.open("a", encoding="utf-8") as stream:
            stream.write("CUSTOM_SETTING=keep-me\n")
        original = self.file.read_bytes()
        result = self.run_initializer()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(original, self.file.read_bytes())

    def test_incomplete_existing_environment_fails_without_overwriting_it(self):
        self.file.write_text("OPERATION_POSTGRES_PASSWORD=short\n", encoding="utf-8")
        original = self.file.read_bytes()
        self.assertNotEqual(0, self.run_initializer().returncode)
        self.assertEqual(original, self.file.read_bytes())

    def test_local_staff_token_has_valid_signature_and_bounded_local_claims(self):
        self.assertEqual(0, self.run_initializer().returncode)
        values = dict(line.split("=", 1) for line in self.file.read_text(encoding="utf-8").splitlines())
        result = subprocess.run([POWERSHELL, "-NoProfile", "-File", str(ROOT / "scripts/New-LocalStaffToken.ps1"),
                                 "-EnvFile", str(self.file), "-ExpiresInMinutes", "5"], capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, "Local token generation failed")
        header, body, signature = result.stdout.strip().split(".")
        decode = lambda value: base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))
        self.assertEqual("HS256", json.loads(decode(header))["alg"])
        claims = json.loads(decode(body))
        self.assertEqual("gearflow-local", claims["iss"])
        self.assertEqual("gearflow-operation", claims["aud"])
        self.assertEqual("staff", claims["actor_type"])
        self.assertTrue(240 < claims["exp"] - time.time() <= 300)
        expected = hmac.new(values["OPERATION_JWT_SECRET"].encode(), f"{header}.{body}".encode(), hashlib.sha256).digest()
        self.assertTrue(hmac.compare_digest(expected, decode(signature)))
        self.assertNotIn(values["OPERATION_JWT_SECRET"], result.stdout)


if __name__ == "__main__":
    unittest.main()
