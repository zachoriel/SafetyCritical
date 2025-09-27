import json, subprocess, sys, pathlib, pytest

CLI = ["dotnet", "run", "--project", "src/csharp/PumpController.CLI"]


def invoke(temperatureC, pressureBar, command=None):
    payload = {
        "temperatureC": temperatureC,
        "pressureBar": pressureBar,
        "command": command,
    }
    process = subprocess.run(
        CLI, input=json.dumps(payload).encode(), stdout=subprocess.PIPE, check=True
    )
    return json.loads(process.stdout.decode())


@pytest.mark.req("REQ-005")
def test_normal_operation():
    result = invoke(250, 90, None)
    assert (
        result["PumpOn"] is True
        and result["Emergency"] is False
        and result["Reason"] == "Normal"
    )


@pytest.mark.req("REQ-006")
def test_emergency_flag_consistency():
    result = invoke(340, 90, None)
    assert result["PumpOn"] is False and result["Emergency"] is True
