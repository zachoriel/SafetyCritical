using NUnit.Framework;
using PumpControllerLib;

namespace SafetyCritical.Tests;

public class ControllerSpec
{
    [Test, Category("REQ-005")]
    public void KeepsPumpOnInNormalOperation()
    {
        var c = new PumpController();
        var r = c.Evaluate(temperatureC: 250, pressureBar: 90, command: null);
        Assert.That(r.PumpOn, Is.True);
        Assert.That(r.Emergency, Is.False);
        Assert.That(r.Reason, Is.EqualTo("Normal"));
    }

    [Test, Category("REQ-002"), Category("REQ-006")]
    public void ShutsDownBelowMinPressureClamp()
    {
        var c = new PumpController(new PumpConfig { MinPressureBar = 70 });
        var r = c.Evaluate(temperatureC: 250, pressureBar: 60, command: null);
        Assert.That(r.PumpOn, Is.False);
        Assert.That(r.Emergency, Is.True);
        Assert.That(r.Reason, Is.EqualTo("LowPressure"));
    }


    [Test, Category("REQ-003"), Category("REQ-006")]
    public void ShutsDownAboveMaxTempClamp()
    {
        var c = new PumpController(new PumpConfig { MaxTempClampC = 335 });
        var r = c.Evaluate(temperatureC: 340, pressureBar: 90, command: null);
        Assert.Multiple(() =>
        {
            Assert.That(r.PumpOn, Is.False);
            Assert.That(r.Emergency, Is.True);
            Assert.That(r.Reason, Is.EqualTo("HighTempClamp"));
        });
    }

    [Test, Category("REQ-001"), Category("REQ-006"), Category("REQ-009")]
    public void ShutsDownAtLowSubcoolMargin()
    {
        var c = new PumpController(new PumpConfig { DeltaTSubcool = 25 });
        // Choose pressure so Tsat = 285C; set temp such that temp >= Tsat - 25
        var r = c.Evaluate(temperatureC: 265, pressureBar: 70, command: null);
        Assert.That(r.PumpOn, Is.False);
        Assert.That(r.Emergency, Is.True);
        Assert.That(r.Reason, Is.EqualTo("LowSubcooling"));
    }


    [Test, Category("REQ-004"), Category("REQ-006"), Category("REQ-007")]
    public void OperatorShutdownImmediate_WhenAuthorizedAndValidChecksum()
    {
        var cmd = MakeCmd("operatorA", "Shutdown");
        var c = new PumpController();
        var r = c.Evaluate(250, 90, cmd);
        Assert.That(r.PumpOn, Is.False);
        Assert.That(r.Emergency, Is.True);
        Assert.That(r.Reason, Is.EqualTo("OperatorShutdown"));
    }

    [Test, Category("REQ-007")]
    public void InvalidCommand_IsIgnored()
    {
        var bad = new OperatorCommand("intruder", "Shutdown", "00");
        var c = new PumpController();
        var r = c.Evaluate(250, 90, bad);
        Assert.That(r.PumpOn, Is.True);
        Assert.That(r.Emergency, Is.False);
        Assert.That(r.Reason, Is.EqualTo("Normal"));
    }

    private static OperatorCommand MakeCmd(string userId, string action)
    {
        int sum = 0; foreach (var ch in (userId + "|" + action)) sum = (sum + (byte)ch) & 0xFF;
        var hex = sum.ToString("X2");
        return new OperatorCommand(userId, action, hex);
    }
}

// Run C# tests locally:
// dotnet test tests/csharp/PumpController.Tests --logger "trx;LogFileName=results.trx"
// TRX output will be in TestResults/ under the test project directory.
