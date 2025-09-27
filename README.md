# Safety-Critical QA Demonstration Project

## Table of Contents
- [I. Overview](#i-overview)
- [II. Explanation of Terminology](#ii-explanation-of-terminology)
- [III. System Overview](#iii-system-overview)
- [IV. Requirements Specification](#iv-requirements-specification)
- [V. Technical Architecture](#v-technical-architecture)
- [VI. Repository Layout](#vi-repository-layout)
- [VII. Development Steps](#vii-development-steps)
- [VIII. Example Outputs](#viii-example-outputs)
- [IX. Extensions & Future Work](#ix-extensions--future-work)
- [X. Quickstart](#x-quickstart)

---

## I. Overview
<details>
<summary>Click to expand</summary>

This project demonstrates how safety-critical software can be tested and verified in ways inspired by highly-regulated industries such as nuclear power, aerospace, and medicine.  

At its core, the system simulates a reactor coolant pump controller – software that decides whether to keep the pump running or to shut it down to prevent dangerous conditions. The simulation is deliberately simplistic, with emphasis on:  

- Writing clear, testable requirements.  
- Implementing the controller in C#, as many industrial systems use compiled languages for reliability.  
- Developing automated tests in both C# (unit-level) and Python (system-level, fault injection, compliance checks).  
- Producing traceability and compliance reports using Python, showing which requirements were tested and whether they passed.  
- Adding a basic cybersecurity check to ensure malformed or unauthorized operator commands are rejected safely.  

The goal is to showcase systematic testing, automation, and compliance mindset to safety-critical domains.

</details>

---

## II. Explanation of Terminology
<details>
<summary>Click to expand</summary>

- **Safety-critical system:** Software where failure could cause injury, death, or major financial or environmental damage.  
- **System Under Test (SUT):** The software being evaluated – in this case, a pump controller.  
- **Requirements Specification:** A set of “the system shall…” statements that define intended behavior. Each has a unique ID.  
- **Traceability Matrix:** A table linking requirements → tests → test results. Ensures complete coverage.  
- **TRX / JUnit XML:** Standard output formats from C# test runners (TRX) and Python’s pytest (JUnit XML), used by CI/CD pipelines.  
- **Fault Injection:** Deliberately providing invalid or extreme inputs to verify the system fails safely.  
- **Checksum Validation:** A simple way of verifying that a command hasn’t been tampered with, representing a basic cybersecurity safeguard.  
- **Malicious/Malformed Input:** Input that is incorrectly formatted or deliberately crafted to break the system.  
- **Validation Report:** A summary of testing results, suitable for review by regulators or managers.  

</details>

---

## III. System Overview  
<details>
<summary>Click to expand</summary>

**System Under Test (SUT):** Reactor Coolant Pump Controller (C# library)  

**Inputs:**  
- Temperature sensor (°C)  
- Pressure sensor (bar)  
- Operator command (with UserId, Action, and Checksum)  

**Outputs:**  
- Pump state (ON / OFF)  
- Emergency shutdown flag (True / False)  
- Shutdown reason (string, e.g., “HighTemp”)  

**Controller Logic (simplified):**  
- If temperature is too close to saturation → pump OFF + emergency flag ON  
- If pressure < configured minimum clamp (e.g., 70 bar) → pump OFF + emergency flag ON  
- If temperature > configured maximum clamp (e.g., 335°C) → pump OFF + emergency flag ON  
- If operator issues shutdown → pump OFF immediately  
- If operator command malformed/unauthorized → ignore it  
- Otherwise → pump stays ON  

</details>

---

## IV. Requirements Specification  
<details>
<summary>Click to expand</summary>

Example requirements (with IDs for traceability):  

- **REQ-001:** The system shall shut off pump if coolant suction temperature ≥ Tsat(P) – ΔTsubcool, where ΔTsubcool is a configurable safety margin (default: 25°C).  
- **REQ-002:** The system shall shut off pump if coolant pressure drops below a configurable minimum clamp (default: 70 bar).  
- **REQ-003:** The system shall shut off pump if coolant temperature exceeds a configurable maximum clamp (default: 335°C).  
- **REQ-004:** The system shall shut off pump immediately if operator issues shutdown command.  
- **REQ-005:** The system shall keep pump on during normal operation.  
- **REQ-006:** The system shall activate an emergency shutdown flag under any shutdown condition.  
- **REQ-007:** The system shall reject malformed or unauthorized operator commands.  
- **REQ-008:** The system shall load configuration values at startup which are immutable at runtime.  
- **REQ-009:** The system shall contain a Tsat lookup accurate to ±2°C over the configured pressure range.  

</details>

---

## V. Technical Architecture  
<details>
<summary>Click to expand</summary>

**Languages and Tools:**  
- **C# / .NET:** Core pump controller + unit tests (NUnit).  
- **Python:**  
  - System-level tests (pytest).  
  - Fault injection and malformed input tests.  
  - Traceability + validation report generation.  
- **Interop:**  
  - C# CLI wrapper accepts JSON input, outputs JSON results.  
  - Python harness calls CLI via subprocess.  
- **CI/CD:** GitHub Actions for automated builds, tests, and artifact reporting.  

</details>

---

## VI. Repository Layout
<details>
<summary>Click to expand</summary>

```
/.github/
  workflows/
    ci.yml

/requirements/
  requirements.yaml

/src/csharp/PumpController/
  PumpController.csproj
  PumpController.cs

/src/csharp/PumpController.CLI/
  PumpController.CLI.csproj
  Program.cs # JSON in -> JSON out

/tests/csharp/PumpController.Tests/
  PumpController.Tests.csproj
  ControllerSpec.cs # NUnit, tagged with REQ IDs

/tests/python/
  test_functional.py
  test_boundaries.py
  test_fault_injection.py
  test_security.py

/tools/
  generate_traceability.py
  parse_junit.py

.editorconfig
.gitattributes
.gitignore
README.md
SafetyCriticalQA.sln
pytest.ini
```

</details>

---

## VII. Development Steps  
<details>
<summary>Click to expand</summary>

**Step 1: Define Requirements**  
- Store in `requirements.yaml`.  
- Each REQ ID maps to at least one test.  

**Step 2: Implement Pump Controller (C#)**  
- Class `PumpController` with `Evaluate(temperature, pressure, command)` method.  
- Includes simple checksum validation for operator commands.  
- Includes Tsat table with interpolation for subcooling trip logic.  

**Step 3: Build CLI Wrapper (C#)**  
- Reads JSON input, evaluates controller, prints JSON output.  
- Enables Python orchestration without complex bindings.  

**Step 4: Write Unit Tests (C# / NUnit)**  
- One or more tests per REQ.  
- Use `[Category("REQ-xxx")]` to tag each test.  
- Export results as `.trx`.  

**Step 5: Write System & Fault Tests (Python / pytest)**  
- Call C# CLI with valid/invalid inputs.  
- Cover normal ops, boundary conditions, and malformed commands.  
- Export results as JUnit XML.  

**Step 6: Generate Traceability Matrix (Python)**  
- Parse `requirements.yaml`.  
- Parse `.trx` and JUnit XML.  
- Produce:  
  - `traceability_matrix.log`  
  - `validation_report.log`  

**Step 7: Automate in CI/CD**  
- GitHub Actions runs `dotnet` + `pytest`.  
- Uploads artifacts (matrix, reports, raw test logs).  

</details>

---

## VIII. Example Outputs  
<details>
<summary>Click to expand</summary>

**Traceability Matrix -- Generated: yyyy-MM-dd HH-mm-ss**  

| Requirement | Source | Test Name                        | Result |
|-------------|--------|----------------------------------|--------|
| REQ-001     | C#     | ShutsDownAtLowSubcoolMargin      | PASS   |
| REQ-002     | C#     | ShutsDownBelowMinPressureClamp   | PASS   |
| REQ-003     | C#     | ShutsDownAboveMaxTempClamp       | PASS   |
| REQ-004     | C#     | OperatorShutdownImmediate        | PASS   |
| REQ-005     | Py     | test_normal_operation            | PASS   |
| REQ-006     | Py     | test_emergency_flag_consistency  | PASS   |
| REQ-007     | Py     | test_invalid_command_rejected    | PASS   |
| REQ-008     | C#     | ConfigImmutableAtRuntime         | PASS   |
| REQ-009     | C#     | TsatLookupAccuracy               | PASS   |

**Validation Report -- Generated: yyyy-MM-dd HH-mm-ss**

- **Requirements**: 9
- **Covered**: 9 (100%)
- **Passed**: 9 (100%
- **Failed**: 0
- **Skipped**: 0
- **Unknown**: 0

Per-Requirement Status
| Requirement | Overall | Tests |
| ----------- | ------- | ----- |
| REQ-001 | Passed | C#:ShutsDownAtLowSubcoolMargin - Passed |
| REQ-002 | Passed | C#: ShutsDownBelowMinPressureClamp - Passed<br/>Py:test_boundary_high_temp - Passed |
| REQ-003 | Passed | C#:ShutsDownAboveMaxTempClamp — Passed<br/>Py:test_boundary_low_pressure — Passed |
| REQ-004 | Passed | C#:OperatorShutdownImmediate_WhenAuthorizedAndValidChecksum — Passed<br/>Py:test_invalid_command_rejected — Passed |
| REQ-005 | Passed | C#:KeepsPumpOnInNormalOperation — Passed<br/>Py:test_emergency_flag_consistency — Passed |
| REQ-006 | Passed | C#:ShutsDownBelowMinPressureClamp — Passed<br/>C#:ShutsDownAboveMaxTempClamp — Passed<br/>C#:ShutsDownAtLowSubcoolMargin — Passed<br/>C#:OperatorShutdownImmediate_WhenAuthorizedAndValidChecksum — Passed<br/>Py:test_boundary_high_temp — Passed<br/>Py:test_boundary_low_pressure — Passed<br/>Py:test_normal_operation — Passed<br/>Py:test_invalid_command_rejected — Passed |
| REQ-007 | Passed | C#:InvalidCommand_IsIgnored — Passed<br/>Py:test_authorized_shutdown — Passed |
| REQ-008 | Passed | C#:ConfigProperties_AreInitOnly — Passed |
| REQ-009 | Passed | C#:ShutsDownAtLowSubcoolMargin — Passed<br/>C#:TsatLookupAccuracy_Within2C — Passed |

**Status**: All requirements verified. System is validated.

</details>

---

## IX. Extensions & Future Work  
<details>
<summary>Click to expand</summary>

- Expand to multiple pumps → test redundancy/failover.  
- Add timing constraints (performance tests).  
- Add fuzz testing (random string/byte injection).  
- Collect code coverage metrics from C#.  
- Expand cybersecurity REQ into session tokens, replay protection.  
- Add watchdog monitoring for missed sensor updates.  

</details>

## X. Quickstart
<details>
<summary>Click to expand</summary>

### UI Dashboard
```bash
# Launch UI
dotnet run --project src/csharp/PumpController.UI
```

### Full-CLI
```bash
# Build
dotnet build SafetyCriticalQA.sln
# Run Python Tests (find in tests/python/junit_results.xml)
py -m -pytest -q --junitxml=tests/python/junit_results.xml
# Run C# Tests (find in tests/csharp/PumpController.Tests/TestResults)
dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=dotnet_tests.trx"
# Run Traceability Matrix & Validation Report (find in artifacts/)
python tools/generate_traceability.py
```

</details>

## Notes
- All operating limits are illustrative, not operational guidance.
- Security features are intentionally minimal, to demonstrate validation and rejection of malformed/unauthorized commands.
