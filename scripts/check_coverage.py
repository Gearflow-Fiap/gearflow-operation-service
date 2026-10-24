"""Fail CI when an existing production assembly has line coverage below 80%."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

reports = list(Path(sys.argv[1] if len(sys.argv) > 1 else "TestResults").rglob("*.cobertura.xml"))
if len(reports) != 1:
    raise SystemExit(f"Expected one coverage report; found {len(reports)}")
packages = ET.parse(reports[0]).getroot().findall("./packages/package")
if not packages:
    raise SystemExit("No assemblies in coverage report")
failed = False
for package in packages:
    rate = float(package.attrib["line-rate"])
    print(f"{package.attrib['name']}: {rate:.2%}")
    failed |= rate < 0.8
raise SystemExit(1 if failed else 0)

