import os, sys, json, yaml, subprocess
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[1]

# Allow CI to override locations
TRX_SEARCH_ROOT = Path(os.getenv("TRX_SEARCH_ROOT", ROOT / "TestResults"))
JUNIT_XML = Path(os.getenv("JUNIT_XML", ROOT / "tests/python/junit_results.xml"))

def read_text_any(p: Path) -> str:
    for enc in ("utf-8", "utf-8-sig", "cp1252"):
        try:
            return p.read_text(encoding=enc)
        except UnicodeDecodeError:
            continue
    return p.read_bytes().decode("utf-8", errors="replace")

# Load requirements
req_text = read_text_any(ROOT / "requirements/requirements.yaml")
REQS = yaml.safe_load(req_text)["requirements"]
req_ids = [r["id"] for r in REQS]

# Find TRX files
search_roots = [
    TRX_SEARCH_ROOT,
    ROOT / "tests/csharp/PumpController.Tests/TestResults",
    ROOT,
]
trx_files = []
for base in search_roots:
    if base.exists():
        trx_files += list(base.rglob("*.trx"))
trx_files = sorted(set(trx_files), key=lambda p: p.stat().st_mtime)

if not trx_files:
    print("No TRX files found. Searched:", *map(str, search_roots), sep="\n - ")
    raise SystemExit("No TRX files found. Run dotnet test first.")

trx_latest = trx_files[-1]
print(f"[tracegen] Using TRX: {trx_latest}")
print(f"[tracegen] Using JUnit: {JUNIT_XML}")

PY = sys.executable

# Parse C#
cs_rows = json.loads(
    subprocess.check_output([PY, str(ROOT / "tools/parse_trx.py"), str(trx_latest)])
)

# Parse Python (optional)
py_rows = []
if JUNIT_XML.exists():
    py_rows = json.loads(
        subprocess.check_output([PY, str(ROOT / "tools/parse_junit.py"), str(JUNIT_XML)])
    )
else:
    print("[tracegen] WARNING: JUnit XML not found; Python tests will be absent in the matrix")

# Build matrix
matrix = defaultdict(list)

for row in cs_rows:
    for category in row.get("categories", []):
        if category in req_ids:
            matrix[category].append({"source": "C#", "name": row["name"], "result": row["outcome"]})

for row in py_rows:
    name = row["name"]
    if name == "test_normal_operation":
        req = "REQ-005"
    elif name == "test_emergency_flag_consistency":
        req = "REQ-006"
    elif name == "test_invalid_command_rejected":
        req = "REQ-007"
    elif name == "test_authorized_shutdown":
        req = "REQ-004"
    elif name == "test_subcool_trip":
        req = "REQ-001"
    elif name.startswith("test_boundary_low_pressure"):
        req = "REQ-002"
    elif name.startswith("test_boundary_high_temp"):
        req = "REQ-003"
    else:
        req = None
    if req:
        outcome = "Passed" if row.get("outcome") in ("Passed", None) else row["outcome"]
        matrix[req].append({"source": "Py", "name": name, "result": outcome})

# Emit files
out_dir = ROOT / "artifacts"
out_dir.mkdir(exist_ok=True)

lines = ["| Requirement | Source | Test Name | Result |", "|---|---|---|---|"]
for req in req_ids:
    tests = matrix.get(req, [])
    if not tests:
        lines.append(f"| {req} | – | – | – |")
    else:
        for i, t in enumerate(tests):
            rid = req if i == 0 else ""
            lines.append(f"| {rid} | {t['source']} | {t['name']} | {t['result']} |")

trace_path = out_dir / "traceability_matrix.md"
trace_path.write_text("\n".join(lines), encoding="utf-8")

total_tests = sum(len(v) for v in matrix.values())
passes = sum(1 for v in matrix.values() for t in v if str(t["result"]).lower() in ("passed", "passed!", "success"))
coverage = int(100 * sum(1 for v in matrix.values() if v) / len(req_ids))

report = f"""# Validation Report – Pump Controller

Requirements: {len(req_ids)}
Tests Executed: {total_tests}
Pass: {passes}
Fail: {total_tests - passes}
Coverage: {coverage}%

All results above are generated artifacts for demo purposes.
"""
(out_dir / "validation_report.md").write_text(report, encoding="utf-8")
print("[tracegen] Wrote:", trace_path, "and validation_report.md")

# Generate artifacts locally

# 1) Run C# tests to produce TRX
# dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=results.trx"

# 2) Run Python tests to produce JUnit XML
# py pytest -q --junitxml=tests/python/junit_results.xml

# 3) Build traceability + validation report
# py python tools/generate_traceability.py

# See outputs in ./artifacts
