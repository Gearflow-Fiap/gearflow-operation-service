"""Build/verify an offline, allowlisted Operation integration bundle. No publication."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
MAX_FILE = 32 * 1024 * 1024
MAX_TOTAL = 64 * 1024 * 1024


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe_name(name):
    path = PurePosixPath(name)
    if not name or path.is_absolute() or any(p in ("", ".", "..") for p in name.split("/")) or "\\" in name or ":" in name or "\0" in name:
        raise ValueError("Unsafe artifact path")
    return name


def read_archive(path):
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len({i.filename for i in entries}) != len(entries) or sum(i.file_size for i in entries) > MAX_TOTAL:
            raise ValueError("Duplicate entries or oversized artifact")
        result = {}
        for entry in entries:
            safe_name(entry.filename)
            if entry.is_dir() or entry.file_size > MAX_FILE:
                raise ValueError("Unexpected directory or oversized entry")
            result[entry.filename] = archive.read(entry)
        return result


def contract_files(repo):
    folder = repo / "docs/contracts/runtime-v1"
    manifest_bytes = (folder / "manifest.json").read_bytes()
    manifest = json.loads(manifest_bytes)
    messages = manifest["messages"]
    if len({m["name"] for m in messages}) != len(messages):
        raise ValueError("Duplicate contract")
    files = {"contracts/runtime-v1/manifest.json": manifest_bytes}
    for message in messages:
        for key in ("schema", "example"):
            name = safe_name(message[key])
            file = folder / name
            if not file.resolve().is_relative_to(folder.resolve()) or file.is_symlink():
                raise ValueError("Contract outside allowlist")
            files["contracts/runtime-v1/" + name] = file.read_bytes()
    return manifest, files


def checked_package(path, manifest, contracts):
    entries = read_archive(path)
    package_id, version = manifest["package"], manifest["version"]
    nuspec = package_id + ".nuspec"
    if nuspec not in entries:
        raise ValueError("Wrong package identity")
    metadata = ET.fromstring(entries[nuspec])
    values = {node.tag.split("}")[-1]: node.text for node in metadata.iter()}
    if values.get("id") != package_id or values.get("version") != version:
        raise ValueError("Package identity/version differs from contract manifest")
    dll = "lib/net10.0/" + package_id + ".dll"
    if not entries.get(dll):
        raise ValueError("Missing contract assembly")
    allowed = set(contracts) | {nuspec, dll, "README.md", "_rels/.rels", "[Content_Types].xml"}
    for name in entries:
        if name not in allowed and not re.fullmatch(r"package/services/metadata/core-properties/(nuget|[a-fA-F0-9]+)\.psmdcp", name):
            raise ValueError("Unexpected file in contract package")
    for name, data in contracts.items():
        if name not in entries or json.loads(entries[name]) != json.loads(data):
            raise ValueError("Packaged contracts differ from source")
    return Path(path).read_bytes()


def build(repo, package, output, provenance):
    repo, output = Path(repo), Path(output)
    manifest, files = contract_files(repo)
    package_name = f"packages/{manifest['package']}.{manifest['version']}.nupkg"
    safe_name(package_name)
    files[package_name] = checked_package(package, manifest, files)
    for source, target in [("README.md", "README.md"), ("compatibility.json", "compatibility.json"),
                           ("decisions.json", "decisions.json"), ("scenarios.json", "scenarios.json")]:
        file = repo / "docs/integration" / source
        if file.is_symlink():
            raise ValueError("Symlink in handoff documentation")
        files[target] = file.read_bytes()
    metadata = {"formatVersion": 1, "status": "preview-pending-group-review", "source": provenance,
                "package": manifest["package"], "version": manifest["version"],
                "contractCount": len(manifest["messages"]),
                "files": {name: digest(data) for name, data in sorted(files.items())}}
    files["bundle.json"] = (json.dumps(metadata, ensure_ascii=False, indent=2) + "\n").encode()
    if sum(map(len, files.values())) > MAX_TOTAL or any(len(v) > MAX_FILE for v in files.values()):
        raise ValueError("Bundle too large")
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive create: never overwrite an earlier handoff or an unrelated file.
    with output.open("xb") as stream, zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            item = zipfile.ZipInfo(safe_name(name), date_time=(1980, 1, 1, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data)
    verify(output)
    return metadata


def verify(path):
    entries = read_archive(path)
    metadata = json.loads(entries["bundle.json"])
    if metadata["formatVersion"] != 1 or set(entries) != set(metadata["files"]) | {"bundle.json"}:
        raise ValueError("Bundle file list mismatch")
    for name, expected in metadata["files"].items():
        if digest(entries[name]) != expected:
            raise ValueError("Bundle checksum mismatch")
    return metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", type=Path)
    parser.add_argument("--package", type=Path)
    parser.add_argument("--generated", type=Path, help="Fresh CLR export to compare before packaging")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.verify:
        if args.package or args.generated or args.output:
            parser.error("Use --verify alone")
        metadata = verify(args.verify)
    else:
        if not all((args.package, args.generated, args.output)):
            parser.error("Build requires --package, --generated and --output")
        subprocess.run([sys.executable, str(ROOT / "scripts/validate_contracts.py"),
                        str(ROOT / "docs/contracts/runtime-v1"), "--compare", str(args.generated)], check=True)
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT))
        metadata = build(ROOT, args.package, args.output, {"commit": commit, "workingTreeDirty": dirty})
    print(json.dumps({"status": "verified", "contracts": metadata["contractCount"],
                      "package": metadata["package"], "version": metadata["version"]}))


if __name__ == "__main__":
    main()
