# Safety-Critical QA Demonstration Project

![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=csharp&logoColor=white)
![Python](https://img.shields.io/badge/python-3670A0?style=for-the-badge&logo=python&logoColor=ffdd54)
![Last Commit](https://img.shields.io/github/last-commit/zachoriel/SafetyCritical)

<img width="1041" height="709" alt="image" src="https://github.com/user-attachments/assets/33e487bf-4ea7-4cef-91a0-2842ac720a33" />

## Table of Contents
- [I. Overview](#i-overview)
- [II. Quickstart](#ii-quickstart)
- [III. Explanation of Terminology](#iii-explanation-of-terminology)
- [IV. System Overview](#iv-system-overview)
- [V. Requirements Specification](#v-requirements-specification)
- [VI. Technical Architecture](#vi-technical-architecture)
- [VII. Repository Layout](#vii-repository-layout)
- [VIII. Development Steps](#viii-development-steps)
- [IX. Example Outputs](#ix-example-outputs)
- [X. Extensions & Future Work](#x-extensions--future-work)

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

## II. Quickstart
<details>
<summary>Click to expand</summary>

A graphical UI dashboard is available to step through tests manually or auto-run the full suite, with live status updates and a final rollup of results.

Alternatively, if you have Python 3.10+, pytest, and .NET8.0+, you can run a few CLI commands.

### Option A: UI Dashboard

Simply run the packaged UI executable (`PumpController.UI.exe`) - no Python or .NET install required.

(Or open a command window at the project root and run `dotnet run --project src/csharp/PumpController.UI`.)

<img width="1041" height="711" alt="image" src="https://github.com/user-attachments/assets/e93931fd-8ebe-4511-b686-4bdd64d8bf9d" />

**Key:**

- **Header:** shows live Temp/Pressure/PumpOn/Emergency/Reason text.
- **Manual mode:** click "Run Test" -> "Next Test" -> view scrollable results list.
- **Auto mode:** check the "Instantly complete test suite" box and click "Begin Demo" (or "Run Test" if you're part-way through). Runs the whole suite instantly and displays scrollable results.
- **End:** scrollable rollup of all tests + buttons to rerun, generate artifacts, or view artifact file location.
- **Right-side panel:** a list of all requirements for the software - can be used to compare individual test results with the header panel display.
- **Bottom panels:** displays the latest traceability matrix and validation report - created via the Generate Artifacts button at the end of testing.

### Option B: CLI (for developers)

Run the following:

```bash
# Build
dotnet build SafetyCriticalQA.sln
# Run Python Tests (find in tests/python/junit_results.xml)
py -m pytest -q --junitxml=tests/python/junit_results.xml
# Run C# Tests (find in tests/csharp/PumpController.Tests/TestResults)
dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=dotnet_tests.trx"
# Run Traceability Matrix & Validation Report (find in artifacts/)
python tools/generate_traceability.py
```

</details>

---

## III. Explanation of Terminology
<details>
<summary>Click to expand</summary>

- **Safety-critical system:** Software where failure could cause injury, death, or major financial or environmental damage.  
- **System Under Test (SUT):** The software being evaluated – in this case, a pump controller.  
- **Requirements Specification:** A set of “the system shall…” statements that define intended behavior. Each has a unique ID.
- **Tsat (Saturation Temperature):** The temperature at which a liquid will start to boil at a specific pressure. For water, Tsat increases with pressure. Safety systems use Tsat to determine whether the coolant is at risk of boiling.
- **ΔTsubcool ("Delta T subcool" - Subcooling Margin):** The temperature difference between the coolant's saturation temperature (Tsat) at a given pressure and the actual measured coolant temperature. It indicates how far below boiling the coolant is. A larger ΔTsubcool means more safety margin before boiling begins.
- **Traceability Matrix:** A table linking requirements → tests → test results. Ensures complete coverage.  
- **TRX / JUnit XML:** Standard output formats from C# test runners (TRX) and Python’s pytest (JUnit XML), used by CI/CD pipelines.  
- **Fault Injection:** Deliberately providing invalid or extreme inputs to verify the system fails safely.  
- **Checksum Validation:** A simple way of verifying that a command hasn’t been tampered with, representing a basic cybersecurity safeguard.  
- **Malicious/Malformed Input:** Input that is incorrectly formatted or deliberately crafted to break the system.  
- **Validation Report:** A summary of testing results, suitable for review by regulators or managers.

**Reference Tsat Table:**
| Pressure (bar) | Tsat (°C) |
| -------------- | --------- |
| 1 | 100 |
| 10 | 180 |
| 20 | 212 |
| 40 | 252 |
| 70 | 285 |
| 100 | 311 |

*The above table values were used for this project, but should not be taken as true-to-life for any given reactor.

</details>

---

## IV. System Overview  
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

## V. Requirements Specification  
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

## VI. Technical Architecture  
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

## VII. Repository Layout
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

## VIII. Development Steps  
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

**Step 8: Add UI Dashboard**
- Manual mode for individual test observation.
- Auto mode for quick full-suite completion.
- Requirements panel.
- Artifacts panel.
- Generate artifacts sequence.

</details>

---

## IX. Example Outputs  
<details>
<summary>Click to expand</summary>

**UI Dashboard -- Individual Test Run**

<img width="1043" height="706" alt="image" src="https://github.com/user-attachments/assets/d5bf68a8-d156-499e-b383-084a7e6308ba" />

**UI Dashboard -- Completed Test Suite**

<img width="1044" height="709" alt="image" src="https://github.com/user-attachments/assets/9a5f850f-14dd-42a0-a2f3-492fbdb6216c" />

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
- **Passed**: 9 (100%)
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

## X. Extensions & Future Work  
<details>
<summary>Click to expand</summary>

- Expand to multiple pumps → test redundancy/failover.  
- Add timing constraints (performance tests).  
- Add fuzz testing (random string/byte injection).  
- Collect code coverage metrics from C#.  
- Expand cybersecurity REQ into session tokens, replay protection.  
- Add watchdog monitoring for missed sensor updates.  

</details>

---

## Notes
- All operating limits are illustrative, not operational guidance.
- Security features are intentionally minimal, to demonstrate validation and rejection of malformed/unauthorized commands.
