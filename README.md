# Safety-Critical QA Demonstration Project

This repository demonstrates a safety-critical testing mindset using a simplified reactor coolant pump controller implemented in C# with tests in both C# and Python.

## Quickstart
```bash
dotnet build
# C# tests -> TRX
dotnet test tests/csharp/PumpController.Tests /logger:trx
# Python tests -> JUnit
pytest -q --junitxml=tests/python/junit_results.xml
# Reports
python tools/generate_traceability.py

Artifacts will be in ./artifacts and raw test logs under tests/...

## Notes
- All operating limits are illustrative, not operational guidance.
- Security features are intentionally minimal, to demonstrate validation and rejection of malformed/unauthorized commands.
