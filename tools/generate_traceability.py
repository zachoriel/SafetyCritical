#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Generate traceability artifacts:
  - artifacts/traceability_matrix.md
  - artifacts/validation_report.md

It correlates requirement IDs (e.g., REQ-001) to test results across:
  * Python (pytest -> JUnit XML)
  * C# (NUnit/MSTest -> TRX)
…and is robust even when TRX doesn't include categories/traits:
  - It scans C# test source to discover [Category("REQ-xxx")] and joins by method name.
  - It scans Python test source for REQ-xxx in decorators/names/docstrings and joins by function name.

Project layout:
  - C#:   tests/csharp/**/.cs            (test methods live here)
  - Py:   tests/python/**/test_*.py      (pytest tests live here)
  - XML:  any **/*.trx and **/*.xml are discovered recursively
"""

from __future__ import annotations
from pathlib import Path
from collections import defaultdict, Counter
import re
import xml.etree.ElementTree as ET
from typing import Dict, List, Tuple, Iterable

# ---- repo roots & constants -------------------------------------------------

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts"
ARTIFACTS.mkdir(parents=True, exist_ok=True)

CS_TEST_GLOB = [
    "tests/csharp/**/*.cs",
    "tests/csharp/*.cs",
    "tests/**/*.cs",
    "**/test_*.cs"
]
PY_TEST_GLOB = [
    "tests/python/*.py"
    "tests/python/**/*.py",
    "tests/**/*.py",
    "**/test_*.py",
]
TRX_GLOB = ["**/*.trx"]
JUNIT_GLOB = [
    "tests/python/**/*.xml",
    "**/junit*.xml",
    "**/test-results*.xml",
    "**/TestResult*.xml",
]

REQ_RE = re.compile(r"\bREQ-(\d{3})\b", re.IGNORECASE)

# ---- Small helpers ----------------------------------------------------------


def read_text(p: Path) -> str:
    return p.read_text(encoding="utf-8", errors="ignore")


def uniq(seq: Iterable[str]) -> List[str]:
    seen = set()
    out = []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


def outcome_rank(outcome: str) -> int:
    # Lower is better; used to compute overall per-REQ status
    order = {
        "Passed": 0,
        "Success": 0,
        "OK": 0,
        "Skipped": 1,
        "Unknown": 2,
        "Failed": 3,
        "Error": 3,
    }
    return order.get(outcome, 2)


# ---- discover results: TRX (C#) --------------------------------------------


def parse_trx_file(path: Path) -> List[Dict[str, str]]:
    """
    Returns list of rows: {"name": testName, "outcome": outcome}
    Tries to be adapter-agnostic.
    """
    rows: List[Dict[str, str]] = []
    try:
        tree = ET.parse(path)
        root = tree.getroot()
    except Exception:
        return rows

    # Canonical: Results/UnitTestResult
    for res in root.findall(".//{*}UnitTestResult"):
        name = res.attrib.get("testName") or res.attrib.get("testname") or ""
        outcome = res.attrib.get("outcome", "") or res.attrib.get("result", "")
        if not name:
            # Fallback: use executionId or id if present
            name = (
                res.attrib.get("executionId") or res.attrib.get("testId") or "Unknown"
            )
        if not outcome:
            # Guess from child nodes
            if res.find(".//{*}Output/{*}ErrorInfo/{*}Message") is not None:
                outcome = "Failed"
            else:
                outcome = "Unknown"
        rows.append({"name": name.strip(), "outcome": outcome.strip()})
    return rows


def load_all_trx_rows() -> List[Dict[str, str]]:
    files: List[Path] = []
    for pat in TRX_GLOB:
        files.extend(ROOT.rglob(pat))
    rows: List[Dict[str, str]] = []
    for f in files:
        rows.extend(parse_trx_file(f))
    return rows


# ---- discover results: JUnit (pytest) --------------------------------------


def parse_junit_file(path: Path) -> List[Dict[str, str]]:
    """
    Returns list of rows: {"name": testName, "outcome": outcome}
    For pytest, testcase @name usually includes function[..param] or "ClassName::func".
    """
    rows: List[Dict[str, str]] = []
    try:
        tree = ET.parse(path)
        root = tree.getroot()
    except Exception:
        return rows

    for tc in root.findall(".//testcase"):
        name = tc.attrib.get("name") or ""
        # Normalize pytest names like "test_func[param]" -> "test_func"
        norm = re.split(r"[\[\(]", name, 1)[0]
        outcome = "Passed"
        if tc.find("skipped") is not None:
            outcome = "Skipped"
        elif tc.find("failure") is not None or tc.find("error") is not None:
            outcome = "Failed"
        rows.append({"name": norm.strip(), "outcome": outcome})
    return rows


def load_all_junit_rows() -> List[Dict[str, str]]:
    files: List[Path] = []
    for pat in JUNIT_GLOB:
        files.extend(ROOT.rglob(pat))
    rows: List[Dict[str, str]] = []
    for f in files:
        rows.extend(parse_junit_file(f))
    return rows


# ---- discover REQs from source: C# -----------------------------------------


def map_cs_test_reqs() -> Dict[str, List[str]]:
    """
    Map C# test method -> [REQ-xxx].
    - Accepts any attribute order (Category before/after Test).
    - Supports class-level [Category("REQ-xxx")] applied to all methods in the class.
    - Works with [Test], [TestMethod], [Theory], etc.
    """
    mapping: Dict[str, List[str]] = {}
    files: List[Path] = []
    for pat in CS_TEST_GLOB:
        files.extend(ROOT.rglob(pat))

    # Helpers
    cat_rgx = re.compile(r'Category\s*\(\s*"?\b(REQ-\d{3})\b"?\s*\)', re.I)
    # Matches a method with any attribute block above it; allows async/Task/void signatures
    method_rgx = re.compile(
        r"(?P<attrs>(?:\s*\[[^\]]+\]\s*)*)\s*"
        r"(?:public|private|internal|protected)\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?)\s+"
        r"(?P<name>[A-Za-z0-9_]+)\s*\(",
        re.S,
    )
    # Consider these as "test" attributes
    test_attr_hint = re.compile(
        r"\[\s*(Test|TestMethod|Theory|TestCase|TestCaseSource|TestOf)\b", re.I
    )
    class_rgx = re.compile(
        r"(?P<classattrs>(?:\s*\[[^\]]+\]\s*)*)\s*(?:public|internal)\s+class\s+(?P<classname>[A-Za-z0-9_]+)\b(.*?)\{(?P<body>.*)\}",
        re.S,
    )

    for f in files:
        try:
            text = f.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue

        # Gather class-level categories (apply to all test methods in the class)
        class_level: Dict[str, List[str]] = {}
        for cm in class_rgx.finditer(text):
            class_attrs = cm.group("classattrs") or ""
            class_reqs = [r.upper() for r in cat_rgx.findall(class_attrs)]
            if not class_reqs:
                continue
            body = cm.group("body") or ""
            for mm in method_rgx.finditer(body):
                attrs = mm.group("attrs") or ""
                name = mm.group("name")
                # Only treat as test if we see a test-ish attribute somewhere in attrs
                if test_attr_hint.search(attrs):
                    class_level.setdefault(name, []).extend(class_reqs)

        # Method-level categories (any order relative to [Test])
        for mm in method_rgx.finditer(text):
            attrs = mm.group("attrs") or ""
            name = mm.group("name")
            # Only accept methods with any test-ish attribute
            if not test_attr_hint.search(attrs):
                continue
            reqs = [r.upper() for r in cat_rgx.findall(attrs)]
            if reqs:
                mapping.setdefault(name, []).extend(reqs)

        # Merge class-level categories
        for k, v in class_level.items():
            mapping.setdefault(k, []).extend(v)

    # De-dupe
    for k, v in list(mapping.items()):
        mapping[k] = sorted(set(v))
    return mapping


# ---- discover REQs from source: Python -------------------------------------


def map_py_test_reqs() -> Dict[str, List[str]]:
    """
    Heuristics for pytest:
      - @pytest.mark.<something>("REQ-xxx") or @pytest.mark.req("REQ-xxx")
      - Function name or docstring contains REQ-xxx
    Returns: testFunctionName -> [REQ-xxx, ...]
    """
    mapping: Dict[str, List[str]] = {}
    files: List[Path] = []
    for pat in PY_TEST_GLOB:
        files.extend(ROOT.rglob(pat))

    # Rough function finder
    func_rgx = re.compile(
        r"@?[^\n]*\ndef\s+(test_[A-Za-z0-9_]+)\s*\("  # decorator lines allowed
    )
    deco_block_rgx = re.compile(
        r"(?:^\s*@.*\n)+\s*def\s+(test_[A-Za-z0-9_]+)\s*\(", re.M
    )
    req_in_deco_rgx = re.compile(
        r'@pytest\.mark\.[A-Za-z_]+\s*\(\s*["\']\b(REQ-\d{3})\b["\']', re.I
    )
    triple_rgx = re.compile(r'^\s*[ru]?"""(.*?)"""', re.S | re.M)

    for f in files:
        try:
            text = read_text(f)
        except Exception:
            continue

        # Decorator blocks
        for m in deco_block_rgx.finditer(text):
            name = m.group(1)
            # Grab preceding lines (up to 5) to catch the actual markers
            start = m.start()
            prefix = text[max(0, start - 500) : start]
            reqs = [r.upper() for r in req_in_deco_rgx.findall(prefix)]
            if reqs:
                mapping.setdefault(name, []).extend(uniq(reqs))

        # Function names
        for m in func_rgx.finditer(text):
            name = m.group(1)
            # If name contains REQ-xxx (rare), add
            name_reqs = [f"REQ-{num:0>3}" for num in map(int, REQ_RE.findall(name))]
            if name_reqs:
                mapping.setdefault(name, []).extend(
                    uniq([r.upper() for r in name_reqs])
                )

        # Docstrings right after def
        for m in re.finditer(
            r"def\s+(test_[A-Za-z0-9_]+)\s*\([^)]*\)\s*:\s*(?:\n\s*)+([rRuU]?\"\"\".*?\"\"\")",
            text,
            re.S,
        ):
            name = m.group(1)
            doc = m.group(2)
            reqs = [f"REQ-{n:0>3}" for n in map(int, REQ_RE.findall(doc))]
            if reqs:
                mapping.setdefault(name, []).extend(uniq([r.upper() for r in reqs]))

        # Fallback: anywhere in file, map last seen test_ function if line mentions REQ
        last_fn = None
        for line in text.splitlines():
            mfn = re.match(r"\s*def\s+(test_[A-Za-z0-9_]+)\s*\(", line)
            if mfn:
                last_fn = mfn.group(1)
            for rid_num in REQ_RE.findall(line):
                if last_fn:
                    rid = f"REQ-{int(rid_num):0>3}"
                    mapping.setdefault(last_fn, []).append(rid)

    # de-dup
    for k, v in list(mapping.items()):
        mapping[k] = uniq([x.upper() for x in v])
    return mapping


def outcome_for_csharp_method(method_name: str, csharp_out: Dict[str, str]) -> str:
    """
    Find TRX outcome for a C# method name, tolerating fully-qualified names.
    Prefers exact, then suffix matches like *.Class.Method or ::Method.
    """
    if method_name in csharp_out:
        return csharp_out[method_name]
    # Common TRX shapes: Namespace.Class.Method, Namespace.Class.Method(param...), Class.Method
    # Try dot / double-colon / suffix forms
    for k, v in csharp_out.items():
        if (
            k.endswith(f".{method_name}")
            or k.endswith(f"::{method_name}")
            or k == method_name
        ):
            return v
        # Also tolerate parameterized names like Method(...)
        if k.endswith(f".{method_name}") or re.search(
            rf"(^|[.:]){re.escape(method_name)}\s*\(", k
        ):
            return v
    return "Unknown"


# ---- build coverage ---------------------------------------------------------


def build_coverage() -> Tuple[Dict[str, List[Tuple[str, str, str]]], Dict[str, str]]:
    """
    Returns:
      coverage: REQ -> list of (Lang, TestName, Outcome)
      overall_outcome: REQ -> aggregate outcome (Passed/Failed/Skipped/Unknown)
    """
    # Results
    trx_rows = load_all_trx_rows()
    junit_rows = load_all_junit_rows()

    # Index outcomes by test name
    csharp_out = {r["name"]: r["outcome"] for r in trx_rows}
    py_out = {r["name"]: r["outcome"] for r in junit_rows}

    # Source mappings
    cs_map = map_cs_test_reqs()  # C# method -> [REQs]
    py_map = map_py_test_reqs()  # py func  -> [REQs]

    # Collect all REQs seen
    reqs = set()
    for v in cs_map.values():
        reqs.update(v)
    for v in py_map.values():
        reqs.update(v)

    coverage: Dict[str, List[Tuple[str, str, str]]] = defaultdict(list)

    # Join C#
    for test_name, rids in cs_map.items():
        outcome = outcome_for_csharp_method(test_name, csharp_out)
        for rid in rids:
            coverage[rid].append(("C#", test_name, outcome))

    # Join Py
    for test_name, rids in py_map.items():
        outcome = py_out.get(test_name, "Unknown")
        for rid in rids:
            coverage[rid].append(("Py", test_name, outcome))

    # Overall outcome per REQ
    overall: Dict[str, str] = {}
    for rid in reqs:
        if rid not in coverage or not coverage[rid]:
            overall[rid] = "Unknown"
            continue
        # If any failed -> Failed, else if any skipped and none failed -> Skipped, else Passed
        ranked = sorted((outcome_rank(o) for _, _, o in coverage[rid]))
        best = min(ranked) if ranked else outcome_rank("Unknown")
        # Invert
        inv = {0: "Passed", 1: "Skipped", 2: "Unknown", 3: "Failed"}
        overall[rid] = inv.get(best, "Unknown")

    return coverage, overall


# ---- writers ----------------------------------------------------------------


def write_traceability_matrix(coverage: Dict[str, List[Tuple[str, str, str]]]) -> None:
    lines: List[str] = []
    lines.append("| Requirement | Lang | Test | Outcome |")
    lines.append("|---|---|---|---|")
    for rid in sorted(coverage.keys()):
        rows = coverage[rid]
        if not rows:
            lines.append(f"| {rid} | – | – | – |")
            continue
        # one row per test
        for lang, test, outcome in rows:
            lines.append(f"| {rid} | {lang} | {test} | {outcome} |")
    # Also add any REQs that never appeared (edge case)
    if not coverage:
        lines.append("| – | – | – | – |")
    (ARTIFACTS / "traceability_matrix.md").write_text(
        "\n".join(lines), encoding="utf-8"
    )


def write_validation_report(
    coverage: Dict[str, List[Tuple[str, str, str]]], overall: Dict[str, str]
) -> None:
    all_reqs = sorted(overall.keys())
    total = len(all_reqs)
    covered = sum(1 for r in all_reqs if coverage.get(r))
    passed = sum(1 for r in all_reqs if overall.get(r) == "Passed")
    failed = sum(1 for r in all_reqs if overall.get(r) == "Failed")
    skipped = sum(1 for r in all_reqs if overall.get(r) == "Skipped")
    unknown = sum(1 for r in all_reqs if overall.get(r) == "Unknown")

    cov_pct = (covered / total * 100.0) if total else 0.0
    pass_pct = (passed / total * 100.0) if total else 0.0

    lines: List[str] = []
    lines.append("# Validation Report")
    lines.append("")
    lines.append(f"- **Requirements**: {total}")
    lines.append(f"- **Covered**: {covered} ({cov_pct:.0f}%)")
    lines.append(f"- **Passed**: {passed} ({pass_pct:.0f}%)")
    lines.append(f"- **Failed**: {failed}")
    lines.append(f"- **Skipped**: {skipped}")
    lines.append(f"- **Unknown**: {unknown}")
    lines.append("")
    if failed:
        lines.append("## Failing Requirements")
        for r in all_reqs:
            if overall.get(r) == "Failed":
                tests = "; ".join(
                    f"{lang}:{name}({out})" for lang, name, out in coverage[r]
                )
                lines.append(f"- {r}: {tests}")
        lines.append("")
    if any(not coverage.get(r) for r in all_reqs):
        lines.append("## Uncovered Requirements")
        for r in all_reqs:
            if not coverage.get(r):
                lines.append(f"- {r}")
        lines.append("")

    # Per-REQ rollup
    lines.append("## Per-Requirement Status")
    lines.append("| Requirement | Overall | Tests |")
    lines.append("|---|---|---|")
    for r in all_reqs:
        tests = coverage.get(r, [])
        test_summ = (
            "<none>"
            if not tests
            else "<br/>".join(f"{lang}:{name} — {out}" for lang, name, out in tests)
        )
        lines.append(f"| {r} | {overall.get(r, 'Unknown')} | {test_summ} |")

    (ARTIFACTS / "validation_report.md").write_text("\n".join(lines), encoding="utf-8")


# ---- main -------------------------------------------------------------------


def main():
    coverage, overall = build_coverage()

    # If no REQs discovered at all, try to salvage by scanning text for “REQ-xxx”
    if not overall:
        all_req_ids = set()
        for p in ROOT.rglob("*.*"):
            if p.is_file() and p.suffix.lower() in {
                ".cs",
                ".py",
                ".md",
                ".yml",
                ".yaml",
                ".txt",
            }:
                for n in REQ_RE.findall(read_text(p)):
                    all_req_ids.add(f"REQ-{int(n):0>3}")
        # Seed empty coverage/overall
        for rid in sorted(all_req_ids):
            coverage.setdefault(rid, [])
            overall.setdefault(rid, "Unknown")

    print("[debug] junit_rows:", len(load_all_junit_rows()))
    print("[debug] trx_rows:", len(load_all_trx_rows()))
    cs_map_dbg = map_cs_test_reqs()
    py_map_dbg = map_py_test_reqs()
    print("[debug] cs_map keys:", list(cs_map_dbg.keys())[:5])
    print("[debug] py_map keys:", list(py_map_dbg.keys())[:5])

    write_traceability_matrix(coverage)
    write_validation_report(coverage, overall)

    # Friendly CLI print
    total = len(overall)
    covered = sum(1 for r in overall if coverage.get(r))
    print(
        f"[generate_traceability] Requirements: {total}, Covered: {covered} ({(covered/total*100 if total else 0):.0f}%)"
    )
    print(f"Artifacts written to: {ARTIFACTS}")


if __name__ == "__main__":
    main()
