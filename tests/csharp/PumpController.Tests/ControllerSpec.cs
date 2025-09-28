using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        // Choose pressure so Tsat = 285C; set temp such that temp >= Tsat - DeltaTSubcool
        var r = c.Evaluate(temperatureC: 265, pressureBar: 70, command: null);
        Assert.That(r.PumpOn, Is.False);
        Assert.That(r.Emergency, Is.True);
        Assert.That(r.Reason, Is.EqualTo("LowSubcooling"));
    }


    [Test, Category("REQ-004"), Category("REQ-006")]
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

    [Test, Category("REQ-008")]
    public void ConfigProperties_AreInitOnly()
    {
        var props = typeof(PumpConfig).GetProperties();
        foreach (var p in props)
        {
            // PumpConfig properties must have an init-only setter
            Assert.That(p.SetMethod, Is.Not.Null, $"{p.Name} should have a setter (init)");
            var mods = p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers();
            Assert.That(mods, Does.Contain(typeof(IsExternalInit)), $"{p.Name} setter should be init-only");
        }
    }

    [Test, Category("REQ-009")]
    public void TsatLookupAccuracy_Within2C()
    {
        // Reflect to call internal TsatTable.LookupTsatC(double)
        var asm = typeof(PumpController).Assembly;
        var t = asm.GetType("PumpControllerLib.TsatTable", throwOnError: true)!;
        var inst = Activator.CreateInstance(t, nonPublic: true)!;
        var m = t.GetMethod("LookupTsatC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        // Sample a few pressures; (bar -> degrees Celsius): 10 -> 180, 70 -> 285, 100 -> 311
        double tsat10 = (double)m.Invoke(inst, new object[] { 10.0 })!;
        double tsat70 = (double)m.Invoke(inst, new object[] { 70.0 })!;
        double tsat100 = (double)m.Invoke(inst, new object[] { 100.0 })!;

        Assert.That(tsat10, Is.InRange(178, 182));
        Assert.That(tsat70, Is.InRange(283, 287));
        Assert.That(tsat100, Is.InRange(309, 313));
    }

    private static OperatorCommand MakeCmd(string userId, string action)
    {
        int sum = 0; foreach (var ch in (userId + "|" + action)) sum = (sum + (byte)ch) & 0xFF;
        var hex = sum.ToString("X2");
        return new OperatorCommand(userId, action, hex);
    }
}
