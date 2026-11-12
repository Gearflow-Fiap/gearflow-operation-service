"""Validate generated schemas/examples and optionally compare fresh CLR exports."""
import argparse
import copy
import json
from pathlib import Path
from datetime import datetime
from jsonschema import Draft202012Validator, FormatChecker

checker = FormatChecker()
@checker.checks("date-time", raises=(ValueError, TypeError))
def valid_datetime(value):
    if not isinstance(value, str):
        return True
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return "T" in value and parsed.tzinfo is not None

parser = argparse.ArgumentParser()
parser.add_argument("root", nargs="?", default="docs/contracts/runtime-v1")
parser.add_argument("--compare")
args = parser.parse_args()
root = Path(args.root)
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
messages = manifest["messages"]
assert messages and len({m["name"] for m in messages}) == len(messages), "Empty/duplicate manifest"
event_exchanges = [m["exchange"] for m in messages if m["kind"] == "event"]
assert len(set(event_exchanges)) == len(event_exchanges), "Duplicate event exchange"
assert all(isinstance(name, str) and name.startswith("operation.") and not name.endswith(".v1") for name in event_exchanges), "Invalid event exchange"
assert all(m["exchange"] is None for m in messages if m["kind"] == "command"), "Command topology changed"
for message in messages:
    schema = json.loads((root / message["schema"]).read_text(encoding="utf-8-sig"))
    example = json.loads((root / message["example"]).read_text(encoding="utf-8-sig"))
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=checker)
    validator.validate(example)
    assert not validator.is_valid(None), message["name"] + " permits null root"
    bad = copy.deepcopy(example)
    bad["messageId"] = "00000000-0000-0000-0000-000000000000"
    assert not validator.is_valid(bad), message["name"] + " permits empty ID"
    bad_time = copy.deepcopy(example)
    bad_time["occurredOn"] = "not-a-date+00:00"
    assert not validator.is_valid(bad_time), message["name"] + " permits invalid timestamp"
    future = copy.deepcopy(example)
    future["futureOptional"] = True
    validator.validate(future)
    if "operationVersion" in example:
        bad = copy.deepcopy(example)
        bad["operationVersion"] = 0
        assert not validator.is_valid(bad), message["name"] + " permits invalid version"
if args.compare:
    generated = Path(args.compare)
    expected_paths = {p.relative_to(root) for p in root.rglob("*.json")}
    generated_paths = {p.relative_to(generated) for p in generated.rglob("*.json")}
    assert expected_paths == generated_paths, "Generated contract file set differs"
    for path in expected_paths:
        assert json.loads((root / path).read_text(encoding="utf-8-sig")) == json.loads((generated / path).read_text(encoding="utf-8-sig")), str(path) + " differs from CLR export"
print(f"Validated {len(messages)} contracts, schemas, examples and compatibility guards")

