import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("handoff", ROOT / "scripts/build_integration_handoff.py")
handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handoff)


class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        shutil.copytree(ROOT / "docs/contracts/runtime-v1", self.root / "docs/contracts/runtime-v1")
        shutil.copytree(ROOT / "docs/integration", self.root / "docs/integration")
        self.manifest, self.contracts = handoff.contract_files(self.root)
        self.package = self.root / "contracts.nupkg"
        self.entries = dict(self.contracts)
        name, version = self.manifest["package"], self.manifest["version"]
        self.entries.update({name + ".nuspec": f'<package><metadata><id>{name}</id><version>{version}</version></metadata></package>'.encode(),
                             f"lib/net10.0/{name}.dll": b"test-contract-assembly",
                             "package/services/metadata/core-properties/nuget.psmdcp": b"<properties/>"})
        self.write_package()

    def write_package(self):
        with zipfile.ZipFile(self.package, "w") as z:
            for name, data in self.entries.items():
                z.writestr(name, data)

    def build(self, name="handoff.zip"):
        output = self.root / name
        handoff.build(self.root, self.package, output, {"commit": "test", "workingTreeDirty": True})
        return output

    def test_bundle_is_deterministic_and_never_copies_private_or_unlisted_files(self):
        for name in [".env.local", "docs/integration/.env", "docs/contracts/runtime-v1/private.json"]:
            (self.root / name).write_text("DO_NOT_SHARE_SECRET", encoding="utf-8")
        first, second = self.build(), self.build("again.zip")
        self.assertEqual(first.read_bytes(), second.read_bytes())
        metadata = handoff.verify(first)
        self.assertEqual(18, metadata["contractCount"])
        self.assertFalse(any(".env" in name or "private.json" in name for name in metadata["files"]))
        with zipfile.ZipFile(first) as z:
            self.assertFalse(any(b"DO_NOT_SHARE_SECRET" in z.read(name) for name in z.namelist()))

    def test_stale_packaged_contract_is_rejected(self):
        name = next(name for name in self.contracts if "/examples/" in name)
        self.entries[name] = b"{}"
        self.write_package()
        with self.assertRaisesRegex(ValueError, "differ"):
            self.build()

    def test_wrong_package_version_is_rejected(self):
        name = self.manifest["package"] + ".nuspec"
        self.entries[name] = self.entries[name].replace(self.manifest["version"].encode(), b"9.9.9")
        self.write_package()
        with self.assertRaisesRegex(ValueError, "identity/version"):
            self.build()

    def test_extra_private_file_inside_package_is_rejected(self):
        self.entries[".env"] = b"DO_NOT_SHARE_SECRET"
        self.write_package()
        with self.assertRaisesRegex(ValueError, "Unexpected file"):
            self.build()

    def test_path_traversal_in_contract_manifest_is_rejected(self):
        self.manifest["messages"][0]["schema"] = "../../private.json"
        (self.root / "docs/contracts/runtime-v1/manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.build()

    def test_archive_paths_and_duplicate_entries_are_rejected(self):
        for name in ["../escape", "/absolute", "C:/absolute", "dir\\escape", "dir/./file", "dir//file", "file\0hidden"]:
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Unsafe"):
                handoff.safe_name(name)
        archive = self.root / "duplicate.zip"
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(archive, "w") as z:
                z.writestr("same", b"1"); z.writestr("same", b"2")
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            handoff.read_archive(archive)

    def test_tampered_or_extra_bundle_entry_fails_verification(self):
        original = handoff.read_archive(self.build())
        for change in ["tamper", "extra"]:
            entries = dict(original)
            entries["README.md" if change == "tamper" else "extra.txt"] = b"tampered"
            output = self.root / (change + ".zip")
            with zipfile.ZipFile(output, "w") as z:
                for name, data in entries.items(): z.writestr(name, data)
            with self.subTest(change=change), self.assertRaises(ValueError):
                handoff.verify(output)

    def test_existing_output_is_preserved(self):
        output = self.root / "handoff.zip"
        output.write_bytes(b"previous delivery")
        with self.assertRaises(FileExistsError):
            self.build()
        self.assertEqual(b"previous delivery", output.read_bytes())


if __name__ == "__main__":
    unittest.main()
