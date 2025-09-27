# Safety-Critical QA Demonstration Project

This repository demonstrates a safety-critical testing mindset using a simplified reactor coolant pump controller implemented in C# with tests in both C# and Python.

## Quickstart
```bash
# Build
dotnet build SafetyCriticalQA.sln
# Run Python Tests (find in tests/python/junit_results.xml)
py -m -pytest -q --junitxml=tests/python/junit_results.xml
# Run C# Tests (find in tests/csharp/PumpController.Tests/TestResults)
dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=dotnet_tests.trx"
# Run Traceability Matrix & Validation Report (find in artifacts/)
python tools/generate_traceability.py

Artifacts will be in ./artifacts and raw test logs under tests/...

## Notes
- All operating limits are illustrative, not operational guidance.
- Security features are intentionally minimal, to demonstrate validation and rejection of malformed/unauthorized commands.
