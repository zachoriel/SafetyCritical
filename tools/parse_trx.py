# Minimal TRX parser to extract test name, outcome, and categories.
import xml.etree.ElementTree as ET
from pathlib import Path

def parse_trx(trx_path: str):
    root = ET.parse(trx_path).getroot()
    NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    # Map testId -> categories
    categories = {}
    for ut in root.findall(".//t:UnitTest", NS):
        test_id = ut.get("id")
        cs = [c.get("name") for c in ut.findall(".//t:TestCategoryItem", NS)]
        categories[test_id] = cs
    results = []
    for r in root.findall(".//t:UnitTestResult", NS):
        test_id = r.get("testId"); name = r.get("testName"); outcome = r.get("outcome")
        results.append({"name": name, "outcome": outcome, "categories": categories.get(test_id, [])})
    return results

if __name__ == "__main__":
    import sys, json
    print(json.dumps(parse_trx(sys.argv[1]), indent=2))