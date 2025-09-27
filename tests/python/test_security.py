from test_functional import invoke
import pytest


def checksum(user, action):
    s = sum((ord(c) for c in f"{user}|{action}")) & 0xFF
    return f"{s:02X}"


@pytest.mark.req("REQ-007")
def test_invalid_command_rejected():
    bad = {"UserId": "intruder", "Action": "Shutdown", "Checksum": "00"}
    result = invoke(250, 90, bad)
    assert (
        result["PumpOn"] is True
        and result["Emergency"] is False
        and result["Reason"] == "Normal"
    )


@pytest.mark.req("REQ-004")
@pytest.mark.req("REQ-006")
def test_authorized_shutdown():
    good = {
        "UserId": "operatorA",
        "Action": "Shutdown",
        "Checksum": checksum("operatorA", "Shutdown"),
    }
    result = invoke(250, 90, good)
    assert (
        result["PumpOn"] is False
        and result["Emergency"] is True
        and result["Reason"] == "OperatorShutdown"
    )
