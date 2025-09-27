from test_functional import invoke


def test_boundary_low_pressure():
    result = invoke(250, 60)
    assert result["PumpOn"] is False and result["Emergency"] is True and result["Reason"] == "LowPressure"


def test_boundary_high_temp():
    result = invoke(336, 90)
    assert result["PumpOn"] is False and result["Emergency"] is True and result["Reason"] == "HighTempClamp"