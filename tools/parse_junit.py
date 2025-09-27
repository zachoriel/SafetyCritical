# Read JUnit XML from pytest and emit simple list of {name, outcome}
from junitparser import JUnitXml
import sys, json

xml = JUnitXml.fromfile(sys.argv[1])
rows = []
for suite in xml:
    for case in suite:
        name = case.name
        outcome = "Passed"
        if case.result:
            outcome = case.result._tag  # skipped / failure / error
        rows.append({"name": name, "outcome": outcome})
print(json.dumps(rows, indent=2))
