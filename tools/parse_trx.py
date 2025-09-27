# tools/parse_trx.py
import xml.etree.ElementTree as ET


def parse_trx(trx_path: str):
    root = ET.parse(trx_path).getroot()
    NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

    id_to_cats = {}
    for ut in root.findall(".//t:UnitTest", NS):
        test_id = ut.get("id")
        cats = []
        # Look for category items anywhere under this UnitTest, namespace-agnostic
        for node in ut.iter():
            tag = node.tag.split("}")[-1]  # strip any ns
            if tag in ("TestCategoryItem", "TestCategory"):
                val = (
                    node.attrib.get("TestCategory")
                    or node.attrib.get("name")
                    or (node.text or "").strip()
                )
                if val:
                    cats.append(val)
        id_to_cats[test_id] = [c for c in cats if c]

    results = []
    for r in root.findall(".//t:UnitTestResult", NS):
        test_id = r.get("testId")
        name = r.get("testName")
        outcome = r.get("outcome")
        results.append(
            {
                "name": name,
                "outcome": outcome,
                "categories": id_to_cats.get(test_id, []),
            }
        )
    return results


if __name__ == "__main__":
    import sys, json

    print(json.dumps(parse_trx(sys.argv[1]), indent=2))
