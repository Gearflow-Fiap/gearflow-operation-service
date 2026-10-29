"""Regression: VS-generated labels must not replace the actual published DLL path."""
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOCKER = shutil.which("docker")


@unittest.skipUnless(DOCKER, "Docker Compose CLI is required; no daemon is needed")
class VisualStudioComposeTests(unittest.TestCase):
    def test_final_override_keeps_debugger_at_published_dll_after_generated_file(self):
        dockerfile = (ROOT / "src/Operation.Api/Dockerfile").read_text(encoding="utf-8")
        runtime_directory = re.findall(r"^WORKDIR (.+)$", dockerfile, re.M)[-1]
        entrypoint = json.loads(re.findall(r"^ENTRYPOINT (.+)$", dockerfile, re.M)[-1])
        expected_dll = str(PurePosixPath(runtime_directory) / entrypoint[-1])
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            env_file = folder / ".env"
            env_file.write_text("OPERATION_POSTGRES_PASSWORD=compose_test_password_only\nOPERATION_JWT_SECRET=compose_test_secret_only_at_least_32_bytes\n", encoding="utf-8")
            generated = folder / "docker-compose.vs.release.g.yml"
            generated.write_text('''services:
  api:
    entrypoint: dotnet /VSTools/DistrolessHelper/DistrolessHelper.dll --wait
    labels:
      com.microsoft.visualstudio.debuggee.program: dotnet
      com.microsoft.visualstudio.debuggee.arguments: /app/Operation.Api.dll
      com.microsoft.visualstudio.debuggee.workingdirectory: /app
''', encoding="utf-8")
            command = [DOCKER, "compose", "--env-file", str(env_file), "-f", "compose.local.yml", "-f", "compose.visualstudio.yml", "-f", str(generated)]
            environment = dict(os.environ, OPERATION_POSTGRES_PASSWORD="compose_test_password_only", OPERATION_JWT_SECRET="compose_test_secret_only_at_least_32_bytes")
            def config(extra):
                result = subprocess.run(command + extra + ["config", "--format", "json"], cwd=ROOT,
                                        env=environment, capture_output=True, text=True, timeout=30)
                self.assertEqual(0, result.returncode, "Compose configuration failed; resolved environment is intentionally not printed")
                return json.loads(result.stdout)["services"]["api"]
            broken = config([])
            self.assertNotEqual(expected_dll, broken["labels"]["com.microsoft.visualstudio.debuggee.arguments"])
            fixed = config(["-f", "docker-compose.vs.release.yml"])
            self.assertEqual(expected_dll, fixed["labels"]["com.microsoft.visualstudio.debuggee.arguments"])
            self.assertEqual(runtime_directory, fixed["labels"]["com.microsoft.visualstudio.debuggee.workingdirectory"])
            self.assertEqual("service_completed_successfully", fixed["depends_on"]["migrate"]["condition"])
            self.assertEqual("dotnet", fixed["labels"]["com.microsoft.visualstudio.debuggee.program"])


if __name__ == "__main__":
    unittest.main()
