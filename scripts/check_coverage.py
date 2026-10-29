"""Union line hits across unit/integration reports; enforce 80% per assembly."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

reports = list(Path(sys.argv[1] if len(sys.argv) > 1 else "TestResults").rglob("*.cobertura.xml"))
if not reports:
    raise SystemExit("No coverage reports found")
assemblies = {}
for report in reports:
    for package in ET.parse(report).getroot().findall("./packages/package"):
        lines = assemblies.setdefault(package.attrib["name"], {})
        for cls in package.findall("./classes/class"):
            for line in cls.findall("./lines/line"):
                key = (cls.attrib["name"], cls.attrib["filename"].replace("\\", "/"), int(line.attrib["number"]))
                lines[key] = lines.get(key, False) or int(line.attrib["hits"]) > 0
if not assemblies:
    raise SystemExit("No assemblies in coverage reports")
failed = False
for name, lines in sorted(assemblies.items()):
    covered = sum(lines.values())
    rate = covered / len(lines) if lines else 0
    print(f"{name}: {covered}/{len(lines)} ({rate:.2%})")
    failed |= rate < 0.8
raise SystemExit(1 if failed else 0)
