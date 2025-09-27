using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PumpControllerLib;

namespace PumpController.UI
{
    public partial class MainWindow : Window
    {
        record DemoCase(
            string Name,
            double TempC,
            double PressureBar,
            OperatorCommand? Command,
            bool ExpPumpOn,
            bool ExpEmergency,
            string ExpReason,
            string[] ReqIds
        );

        private readonly PumpControllerLib.PumpController _controller = new();
        private readonly List<DemoCase> _cases;

        public MainWindow()
        {
            InitializeComponent();

            // Requirements text
            ReqsText.Text =
@"REQ-001 — Shut off if T ≥ Tsat(P) – ΔTsubcool (Δ=25°C default)
REQ-002 — Shut off if pressure < min clamp (70 bar default)
REQ-003 — Shut off if temperature > max clamp (335°C default)
REQ-004 — Operator 'Shutdown' forces immediate shutdown
REQ-005 — Keep pump ON during normal operation
REQ-006 — Emergency flag active under any shutdown condition
REQ-007 — Reject malformed/unauthorized operator commands
REQ-008 — Load config at startup; values immutable at runtime
REQ-009 — Tsat lookup accurate to ±2°C over configured range";

            // Build demo sequence mirroring unit/system tests.
            // (Values taken directly from tests in tests/csharp/PumpController.TestsControllerSpec.cs.)
            _cases = new List<DemoCase>
            {
                new("Normal operation", 250, 90, null, true, false, "Normal", new[]{"REQ-005"}),
                new("Low pressure clamps OFF", 250, 60, null, false, true, "LowPressure", new[]{"REQ-002","REQ-006"}),
                new("High temp clamps OFF", 340, 90, null, false, true, "HighTempClamp", new[]{"REQ-003","REQ-006"}),
                new("Low subcooling trips OFF", 265, 70, null, false, true, "LowSubcooling", new[]{"REQ-001","REQ-006","REQ-009"}),
                new("Invalid command ignored", 250, 90, new OperatorCommand("intruder","Shutdown","00"), true, false, "Normal", new[]{"REQ-007"}),
                new("Authorized operator shutdown", 250, 90, MakeCmd("operatorA","Shutdown"), false, true, "OperatorShutdown", new[]{"REQ-004","REQ-006"})
            };

            LoadLatestArtifacts();
        }

        private static OperatorCommand MakeCmd(string userId, string action)
        {
            int sum = 0; foreach (var ch in (userId + "|" + action)) sum = (sum + (byte)ch) & 0xFF;
            var hex = sum.ToString("X2");
            return new OperatorCommand(userId, action, hex);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.Visibility = Visibility.Collapsed;
            CurrentTestText.Visibility = Visibility.Visible;

            await RunDemoSequence();
            PostRunButtons.Visibility = Visibility.Visible;

            // Try loading artifacts in case they were generated previously
            LoadLatestArtifacts();
        }

        private async Task RunDemoSequence()
        {
            OutcomeBanner.Visibility = Visibility.Collapsed;

            foreach (var c in _cases)
            {
                // Show which test + requirements
                CurrentTestText.Text = $"{c.Name}\n({string.Join(", ", c.ReqIds)})";
                OutcomeBanner.Visibility = Visibility.Collapsed;

                // Artificial delay before running (unless skip)
                if (SkipDelays.IsChecked == true) { /* no wait */ }
                else { await Task.Delay(1500); }

                // Evaluate
                var result = _controller.Evaluate(c.TempC, c.PressureBar, c.Command);

                // Update live header
                TempText.Text = c.TempC.ToString("0.#");
                PressureText.Text = c.PressureBar.ToString("0.#");
                PumpOnText.Text = result.PumpOn ? "True" : "False";
                EmergencyText.Text = result.Emergency ? "True" : "False";
                ReasonText.Text = result.Reason;

                // Decide pass/fail
                bool pass = result.PumpOn == c.ExpPumpOn &&
                            result.Emergency == c.ExpEmergency &&
                            string.Equals(result.Reason, c.ExpReason, StringComparison.Ordinal);

                OutcomeText.Text = pass ? "PASSED" : "FAILED";
                OutcomeBanner.Background = pass ? new SolidColorBrush(Color.FromRgb(0x17, 0x6F, 0x2C))
                                                : new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E));
                OutcomeBanner.Visibility = Visibility.Visible;

                if (SkipDelays.IsChecked == true) { /* no wait */ }
                else { await Task.Delay(1500); }
            }

            CurrentTestText.Text = "All demo tests complete.";
        }

        private void Rerun_Click(object sender, RoutedEventArgs e)
        {
            // Reset UI for another demo run
            PostRunButtons.Visibility = Visibility.Collapsed;
            OutcomeBanner.Visibility = Visibility.Collapsed;
            CurrentTestText.Text = "";
            TempText.Text = PressureText.Text = PumpOnText.Text = EmergencyText.Text = ReasonText.Text = "—";
            StartButton.Visibility = Visibility.Visible;
        }

        private async void RunRealTests_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;

            try
            {
                string repoRoot = FindRepoRoot() ?? throw new InvalidOperationException("Could not locate repo root.");

                // pytest via Python module (so it can run without a pytest.exe on PATH)
                // Try common Windows python executables in order: repo .venv, python, py
                string? pythonExe = FindPython(repoRoot);
                if (pythonExe is null)
                    throw new Exception(
                        "Could not find Python. Install Python 3 and ensure it's on PATH, " +
                        "or create a .venv at the repo root.\n\nQuick fix:\n  winget install Python.Python.3\n  python -m pip install pytest");

                // Produce junit_results.xml in repo root
                await RunProcess(pythonExe, "-m pytest -q --junitxml=\"tests/python\"/junit_results.xml", repoRoot);

                // dotnet test
                await RunProcess("dotnet", "test tests/csharp/PumpController.Tests --logger trx;LogFileName=dotnet_tests.trx", repoRoot);

                // Generate traceability & validation logs
                await RunProcess(pythonExe, "tools/generate_traceability.py", repoRoot);

                LoadLatestArtifacts(); // refresh lower panes
                CurrentTestText.Text = "Automated tests & artifacts updated.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Test Run Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartButton.IsEnabled = true;
            }
        }

        private string? FindPython(string repoRoot)
        {
            // Prefer repo-local venv if present
            var venvPython = System.IO.Path.Combine(repoRoot, ".venv", "Scripts", "python.exe");
            if (File.Exists(venvPython)) return venvPython;

            // Fall back to common Windows launchers
            // (UseShellExecute=false requires an actual exe; 'py' works fine as a launcher)
            var candidates = new[] { "python.exe", "python", "py.exe", "py" };
            foreach (var c in candidates)
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = c,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    if (p == null) continue;
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0) return c;
                }
                catch { /* ignore and try next */ }
            }
            return null;
        }

        private async Task RunProcess(string fileName, string args, string workingDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi) ?? throw new Exception($"Failed to start: {fileName} {args}");

            string stdout = await p.StandardOutput.ReadToEndAsync();
            string stderr = await p.StandardError.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode != 0)
                throw new Exception($"{fileName} {args}\nExit {p.ExitCode}\n{stderr}\n{stdout}");
        }

        private string? FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "README.md")) ||
                    File.Exists(Path.Combine(dir, ".gitignore")) ||
                    Directory.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                var parent = Directory.GetParent(dir)?.FullName;
                if (parent is null) break;
                dir = parent;
            }
            return null;
        }

        private void LoadLatestArtifacts()
        {
            // Find newest folder in artifacts and load *.log
            string? root = FindRepoRoot();
            if (root is null) return;

            string artifacts = Path.Combine(root, "artifacts");
            if (!Directory.Exists(artifacts)) return;

            var latest = Directory.GetDirectories(artifacts)
                                  .OrderByDescending(d => d)
                                  .FirstOrDefault();
            if (latest is null) return;

            string trace = Path.Combine(latest, "traceability_matrix.log");
            string report = Path.Combine(latest, "validation_report.log");

            if (File.Exists(trace))
                TraceBox.Text = File.ReadAllText(trace);
            if (File.Exists(report))
                ValidationBox.Text = File.ReadAllText(report);
        }

        private void OpenArtifacts_Click(object sender, RoutedEventArgs e)
        {
            string? root = FindRepoRoot();
            if (root is null) return;

            string artifacts = Path.Combine(root, "artifacts");
            if (!Directory.Exists(artifacts)) Directory.CreateDirectory(artifacts);

            // Open in Explorer
            Process.Start(new ProcessStartInfo { FileName = artifacts, UseShellExecute = true });
        }
    }
}
