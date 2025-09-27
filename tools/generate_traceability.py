import yaml, json, subprocess, pathlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TRX_DIR = ROOT / "tests/csharp/PumpController.Tests/TestResults"
PY_JUNIT = ROOT / "tests/python/junit_results.xml"

REQS = yaml.safe_load((ROOT / "requirements/requirements.yaml").read_text())[
    "requirements"
]
req_ids = [r["id"] for r in REQS]


# Gather latest TRX file
trx_files = sorted(TRX_DIR.glob("**/*.trx"))
if not trx_files:
    raise SystemExit("No TRX files found. Run dotnet test first.")
trx_latest = trx_files[-1]

# Parse C#
cs_rows = json.loads(
    subprocess.check_output(
        ["python", str(ROOT / "tools/parse_trx.py"), str(trx_latest)]
    )
)
# Parse Python
py_rows = json.loads(
    subprocess.check_output(
        ["python", str(ROOT / "tools/parse_junit.py"), str(PY_JUNIT)]
    )
)

# Build matrix: REQ -> tests (by category match or heuristic name contains REQ)
from collections import defaultdict

matrix = defaultdict(list)

for row in cs_rows:
    for category in row["categories"]:
        if category in req_ids:
            matrix[category].append(
                {"source": "C#", "name": row["name"], "result": row["outcome"]}
            )

for row in py_rows:
    # Heuristic: map a few known python tests to REQs
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
        outcome = "Passed" if row["outcome"] in ("Passed", None) else row["outcome"]
        matrix[req].append({"source": "Py", "name": name, "result": outcome})

# Emit traceability_matrix.md and validation_report.md
out_dir = ROOT / "artifacts"
out_dir.mkdir(exist_ok=True)

# Traceability matrix
lines = ["| Requirement | Source | Test Name | Result |", "|---|---|---|---|"]
for req in req_ids:
    tests = matrix.get(req, [])
    if not tests:
        lines.append(f"| {req} | – | – | – |")
    else:
        for i, t in enumerate(tests):
            rid = req if i == 0 else ""
            lines.append(f"| {rid} | {t['source']} | {t['name']} | {t['result']} |")
(trace := (out_dir / "traceability_matrix.md")).write_text("\n".join(lines))

# Validation report
total_tests = sum(len(v) for v in matrix.values())
passes = sum(
    1
    for v in matrix.values()
    for t in v
    if str(t["result"]).lower() in ("passed", "passed!", "success")
)
report = f"""
# Validation Report – Pump Controller


Requirements: {len(req_ids)}
Tests Executed: {total_tests}
Pass: {passes}
Fail: {total_tests - passes}
Coverage: {int(100 * sum(1 for v in matrix.values() if v)/len(req_ids))}%

All results above are generated artifacts for demo purposes.
"""
(ROOT / "artifacts/validation_report.md").write_text(report)
print("Wrote:", trace, "and validation_report.md")

# Generate artifacts locally

# 1) Run C# tests to produce TRX
# dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=results.trx"

# 2) Run Python tests to produce JUnit XML
# py pytest -q --junitxml=tests/python/junit_results.xml

# 3) Build traceability + validation report
# py python tools/generate_traceability.py

# See outputs in ./artifacts
