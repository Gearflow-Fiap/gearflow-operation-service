"""Render an ignored Kustomize overlay using the exact tested/published GHCR digest."""
from pathlib import Path
import re
import sys
import yaml

match = re.fullmatch(r"(ghcr\.io/[a-z0-9][a-z0-9._/-]*)@sha256:([0-9a-f]{64})", sys.argv[1])
if not match:
    raise SystemExit("Expected immutable GHCR image@sha256 digest")
root = Path(__file__).resolve().parent.parent
folder = root / "k8s" / "overlays" / "release"
folder.mkdir(parents=True, exist_ok=True)
data = {"apiVersion": "kustomize.config.k8s.io/v1beta1", "kind": "Kustomization", "namespace": "gearflow",
        "resources": ["../../base"], "images": [{"name": "gearflow-operation-service", "newName": match[1], "digest": "sha256:" + match[2]}]}
(folder / "kustomization.yaml").write_text(yaml.safe_dump(data, sort_keys=False), encoding="utf-8")
print("Release overlay prepared with immutable digest; no resources applied")
