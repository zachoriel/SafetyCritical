from test_functional import invoke
import pytest


@pytest.mark.req("REQ-001")
@pytest.mark.req("REQ-006")
@pytest.mark.req("REQ-009")
def test_subcool_trip():
    # pressure ~70 bar -> Tsat = 285C; with Delta T = 25C, trip if temp >= 260C
    result = invoke(265, 70)
    assert (
        result["PumpOn"] is False
        and result["Emergency"] is True
        and result["Reason"] == "LowSubcooling"
    )
