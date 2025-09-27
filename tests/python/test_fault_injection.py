from test_functional import invoke


def test_subcool_trip():
    # pressure ~70 bar -> Tsat = 285C; with Delta T = 25C, trip if temp >= 260C
    result = invoke(265, 70)
    assert (
        result["PumpOn"] is False
        and result["Emergency"] is True
        and result["Reason"] == "LowSubcooling"
    )
