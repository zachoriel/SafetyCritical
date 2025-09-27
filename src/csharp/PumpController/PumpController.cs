using System.Text.Json.Serialization;

namespace PumpControllerLib;

public record OperatorCommand(
    string UserId,
    string Action, // e.g., "Shutdown" | "None"
    string Checksum // simple checksum for demo
);

public record PumpResult(
    bool PumpOn,
    bool Emergency,
    string Reason
);

public class PumpConfig
{
    public double DeltaTSubcool { get; init; } = 25.0; // °C
    public double MinPressureBar { get; init; } = 70.0; // bar
    public double MaxTempClampC { get; init; } = 335.0; // °C
    public HashSet<string> AuthorizedUsers { get; init; } = new(new[] { "operatorA", "operatorB" });
}

public class PumpController
{
    private readonly PumpConfig _cfg;
    private readonly TsatTable _tsat;

    public PumpController(PumpConfig? cfg = null)
    {
        _cfg = cfg ?? new PumpConfig();
        _tsat = new TsatTable();
    }

    public PumpResult Evaluate(double temperatureC, double pressureBar, OperatorCommand? command)
    {
        // REQ-004 & REQ-007: command handling first
        if (command is not null)
        {
            if (!IsValidCommand(command))
            {
                // REQ-007: ignore malformed/unauthorized
            }
            else if (string.Equals(command.Action, "Shutdown", StringComparison.OrdinalIgnoreCase))
            {
                return new PumpResult(false, true, "OperatorShutdown"); // REQ-004, REQ-006
            }
        }

        // REQ-002: minimum pressure clamp
        if (pressureBar < _cfg.MinPressureBar)
        {
            return new PumpResult(false, true, "LowPressure"); // REQ-006
        }

        // REQ-003: maximum temperature clamp
        if (temperatureC > _cfg.MaxTempClampC)
        {
            return new PumpResult(false, true, "HighTempClamp"); // REQ-006
        }

        // REQ-001: subcooling margin against saturation
        var tsat = _tsat.LookupTsatC(pressureBar); // REQ-009
        if (temperatureC >= tsat - _cfg.DeltaTSubcool)
        {
            return new PumpResult(false, true, "LowSubcooling"); // REQ-006
        }

        // Normal operation
        return new PumpResult(true, false, "Normal"); // REQ-005
    }

    private bool IsValidCommand(OperatorCommand cmd)
    {
        // Simple demo checksum: sum of bytes of UserId + Action (mod 256) in hex
        if (!_cfg.AuthorizedUsers.Contains(cmd.UserId)) { return false; } // auth list

        var payload = cmd.UserId + "|" + cmd.Action;
        int sum = 0;
        foreach (var ch in payload)
        {
            sum = (sum + (byte)ch) & 0xFF;
        }
        var expected = sum.ToString("X2");

        return string.Equals(expected, cmd.Checksum, stringComparison.OrdinalIgnoreCase);
    }
}

internal class TsatTable
{
    // Minimal pressure (bar) -> Tsat (°C) points; linear interpolation.
    // These values are illustrative for demo purposes only.
    private readonly (double p, double t)[] _pts = new (double, double)[]
    {
        (  1, 100),
        ( 10, 180),
        ( 20, 212),
        ( 40, 252),
        ( 70, 285),
        (100, 311)
    };

    public double LookupTsatC(double pressureBar)
    {
        if (pressureBar <= _pts[0].p) return _pts[0].t;
        for (int i = 0; i < _pts.Length - 1; i++)
        {
            var (p0, t0) = _pts[i];
            var (p1, t1) = _pts[i + 1];
            if (pressureBar <= p1)
            {
                var a = (pressureBar - p0) / (p1 - p0);
                return t0 + a * (t1 - t0);
            }
        }
        return _pts[^1].t;
    }
}