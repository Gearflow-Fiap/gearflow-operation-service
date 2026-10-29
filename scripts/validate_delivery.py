"""Static checks for this service's rendered Kubernetes resources. Never contacts a cluster."""
from pathlib import Path
import sys
import yaml

base = list(yaml.safe_load_all(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
kong = list(yaml.safe_load_all(Path(sys.argv[2]).read_text(encoding="utf-8-sig")))
job = yaml.safe_load(Path(sys.argv[3]).read_text(encoding="utf-8-sig"))
assert {d["kind"] for d in base} == {"Deployment", "Service", "ConfigMap"}
assert all(d["metadata"]["namespace"] == "gearflow" for d in base + kong)
deployment = next(d for d in base if d["kind"] == "Deployment")
service = next(d for d in base if d["kind"] == "Service")
config = next(d for d in base if d["kind"] == "ConfigMap")
pod = deployment["spec"]["template"]["spec"]
container = pod["containers"][0]
assert pod["automountServiceAccountToken"] is False and pod["securityContext"]["runAsNonRoot"] is True
assert container["securityContext"]["readOnlyRootFilesystem"] is True
assert container["livenessProbe"]["httpGet"]["path"] == "/health/live"
assert container["readinessProbe"]["httpGet"]["path"] == "/health/ready"
assert service["spec"]["selector"] == deployment["spec"]["selector"]["matchLabels"]
assert any(v.get("secretRef", {}).get("name") == "gearflow-operation-secret" for v in container["envFrom"])
assert not any("secret" in key.lower() or "connectionstrings" in key.lower() or "headers" in key.lower() for key in config["data"])
assert job["kind"] == "Job" and job["metadata"]["namespace"] == "gearflow"
assert job["spec"]["template"]["spec"]["containers"][0]["command"] == ["dotnet", "/app/migrate/Operation.Migrate.dll", "--apply"]
ingress = next(d for d in kong if d["kind"] == "Ingress")
paths = ingress["spec"]["rules"][0]["http"]["paths"]
assert {p["path"] for p in paths} == {"/api/operation", "/api/catalog/jobs"}
assert all(p["backend"]["service"]["name"] == service["metadata"]["name"] for p in paths)
print("Validated isolated namespace resources, probes, secret references, migration job and optional routes")
