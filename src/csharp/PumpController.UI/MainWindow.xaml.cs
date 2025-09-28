using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using PumpControllerLib;

namespace PumpController.UI
{
    public partial class MainWindow : Window
    {
        record DisplayOutcome(string Name, string ReqText, bool Pass)
        {
            public string OutcomeText => Pass ? "PASSED" : "FAILED";
            public System.Windows.Media.Brush BannerBrush => Pass ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x17, 0x6F, 0x2C)) : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB0, 0x1E, 0x1E));
        }

        private int _currentIndex = -1;
        private bool _testHasRun = false;
        private readonly List<DisplayOutcome> _history = new();

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

        private bool RunTestAndUpdateUI(int index)
        {
            var c = _cases[index];
            var result = _controller.Evaluate(c.TempC, c.PressureBar, c.Command);

            // Update header readouts
            TempText.Text = c.TempC.ToString("0.#");
            PressureText.Text = c.PressureBar.ToString("0.#");
            PumpOnText.Text = result.PumpOn ? "True" : "False";
            EmergencyText.Text = result.Emergency ? "True" : "False";
            ReasonText.Text = result.Reason;

            // Pass/fail
            bool pass = result.PumpOn == c.ExpPumpOn && result.Emergency == c.ExpEmergency && string.Equals(result.Reason, c.ExpReason, StringComparison.Ordinal);

            // Update per-test banner (used during manual mode)
            OutcomeText.Text = pass ? "PASSED" : "FAILED";
            OutcomeBanner.Background = pass ? new SolidColorBrush(Color.FromRgb(0x17, 0x6F, 0x2C)) : new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E));
            OutcomeBanner.Visibility = Visibility.Visible;

            // Record for final rollup
            _history.Add(new DisplayOutcome(c.Name, $"Requirements: {string.Join(", ", c.ReqIds)}", pass));

            return pass;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            PostRunButtons.Visibility = Visibility.Collapsed;
            ResultsRollup.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            _history.Clear();
            OutcomeBanner.Visibility = Visibility.Collapsed;

            TempText.Text = PressureText.Text = PumpOnText.Text = EmergencyText.Text = ReasonText.Text = "—";

            if (AutocompleteTests.IsChecked == true)
            {
                AutocompleteTestRuns();
            }
            else
            {
                // Manual mode: show first test card, but do NOT run it yet
                StartButton.Visibility = Visibility.Collapsed;
                _currentIndex = 0;
                _testHasRun = false;

                var c = _cases[_currentIndex];
                CurrentTestText.Text = $"{c.Name}\n({string.Join(", ", c.ReqIds)})";
                OutcomeBanner.Visibility = Visibility.Collapsed;
                TestCard.Visibility = Visibility.Visible;
                RunNextButton.Content = "Run Test";
            }
        }

        private void AutocompleteTestRuns()
        {
            // Auto-run full suite quickly
            StartButton.Visibility = Visibility.Collapsed;
            TestCard.Visibility = Visibility.Collapsed;

            for (int i = 0; i < _cases.Count; i++)
            {
                // Update the current test label so users see progress as it runs
                var c = _cases[i];
                CurrentTestText.Text = $"{c.Name}\n({string.Join(", ", c.ReqIds)})";
                RunTestAndUpdateUI(i);
            }

            // Show rollup + post-run buttons
            ResultsList.ItemsSource = _history;
            ResultsRollup.Visibility = Visibility.Visible;
            PostRunButtons.Visibility = Visibility.Visible;
            CurrentTestText.Text = "All demo tests complete.";
            _currentIndex = _cases.Count; // mark as finished
            _testHasRun = true;
        }

        private void RunNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < 0 || _currentIndex >= _cases.Count) return;

            if (AutocompleteTests.IsChecked == true)
            {
                AutocompleteTestRuns();
                return;
            }

            if (!_testHasRun)
            {
                // Run the current test
                RunTestAndUpdateUI(_currentIndex);

                _testHasRun = true;
                RunNextButton.Content = "Next Test";
            }
            else
            {
                // Advance to next test (or finish)
                _currentIndex++;
                _testHasRun = false;

                if (_currentIndex >= _cases.Count)
                {
                    // Done — show final rollup + post-run buttons
                    TestCard.Visibility = Visibility.Collapsed;
                    ResultsList.ItemsSource = _history;
                    ResultsRollup.Visibility = Visibility.Visible;
                    PostRunButtons.Visibility = Visibility.Visible;
                    CurrentTestText.Text = "All demo tests complete.";
                    return;
                }

                // Prepare next test card (do not run yet)
                var c = _cases[_currentIndex];
                CurrentTestText.Text = $"{c.Name}\n({string.Join(", ", c.ReqIds)})";
                OutcomeBanner.Visibility = Visibility.Collapsed;
                TempText.Text = PressureText.Text = PumpOnText.Text = EmergencyText.Text = ReasonText.Text = "—";
                RunNextButton.Content = "Run Test";
            }
        }

        private void Rerun_Click(object sender, RoutedEventArgs e)
        {
            // Reset UI for another demo run
            PostRunButtons.Visibility = Visibility.Collapsed;
            OutcomeBanner.Visibility = Visibility.Collapsed;
            ResultsRollup.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            _history.Clear();

            TempText.Text = PressureText.Text = PumpOnText.Text = EmergencyText.Text = ReasonText.Text = "—";
            StartButton.Visibility = Visibility.Visible;
            TestCard.Visibility = Visibility.Collapsed;

            _currentIndex = -1;
            _testHasRun = false;
        }

        private async void RunRealTests_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;

            try
            {
                string repoRoot = FindRepoRoot() ?? throw new InvalidOperationException("Could not locate repo root.");

                // Try Python first (dev environment)
                // pytest via Python module (so it can run without a pytest.exe on PATH)
                // Try common Windows python executables in order: repo .venv, python, py
                string? pythonExe = FindPython(repoRoot);
                if (pythonExe != null)
                {
                    // Produce junit_results.xml in repo root
                    await RunProcess(pythonExe, "-m pytest -q --junitxml=\"tests/python\"/junit_results.xml", repoRoot);

                    // dotnet test
                    await RunProcess("dotnet", "test tests/csharp/PumpController.Tests --logger trx;LogFileName=dotnet_tests.trx", repoRoot);

                    // Generate traceability & validation logs
                    await RunProcess(pythonExe, "tools/generate_traceability.py", repoRoot);

                    CurrentTestText.Text = "Automated tests & artifacts updated.";
                }
                else
                {
                    // No Python - run the C# functional pass & generate artifacts locally
                    // Re-map existing _cases into NonDevArtifacts.Case
                    var nonDevCases = _cases.Select(c => new NonDevArtifacts.Case(c.Name, c.TempC, c.PressureBar, c.Command, c.ExpPumpOn, c.ExpEmergency,
                        c.ExpReason, c.ReqIds)).ToList();

                    var (dir, junit) = NonDevArtifacts.GenerateAll(nonDevCases, repoRoot);
                    CurrentTestText.Text = $"Artifacts generated (no Python): {Path.GetFileName(dir)}";
                }

                LoadLatestArtifacts(); // Refresh bottom panes
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

    static class NonDevArtifacts
    {
        public sealed record Case(
            string Name,
            double TempC,
            double PressureBar,
            OperatorCommand? Command,
            bool ExpPumpOn,
            bool ExpEmergency,
            string ExpReason,
            string[] ReqIds);

        public static (string artifactsDir, string junitPath) GenerateAll(
            IEnumerable<Case> cases,
            string repoRoot)
        {
            // Evaluate all cases using the real controller
            var controller = new PumpControllerLib.PumpController();
            var results = new List<(Case c, PumpResult r, bool pass)>();
            foreach (var c in cases)
            {
                var r = controller.Evaluate(c.TempC, c.PressureBar, c.Command);
                var pass = r.PumpOn == c.ExpPumpOn && r.Emergency == c.ExpEmergency && string.Equals(r.Reason, c.ExpReason, StringComparison.Ordinal);
                results.Add((c, r, pass));
            }

            // Ensure artifacts folder
            string artifactsRoot = Path.Combine(repoRoot, "artifacts");
            Directory.CreateDirectory(artifactsRoot);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string artifactsDir = Path.Combine(artifactsRoot, stamp);
            Directory.CreateDirectory(artifactsDir);

            // Write traceability_matrix.log
            var traceLines = new List<string> {
                $"Traceability Matrix — {DateTime.Now}",
                new string('=', 64),
                ""
            };
            foreach (var t in results)
            {
                traceLines.Add($"Test: {t.c.Name}");
                traceLines.Add($"  Requirements: {string.Join(", ", t.c.ReqIds)}");
                traceLines.Add($"  Expected: PumpOn={t.c.ExpPumpOn} Emergency={t.c.ExpEmergency} Reason={t.c.ExpReason}");
                traceLines.Add($"  Actual:   PumpOn={t.r.PumpOn} Emergency={t.r.Emergency} Reason={t.r.Reason}");
                traceLines.Add($"  Result:   {(t.pass ? "PASS" : "FAIL")}");
                traceLines.Add("");
            }
            File.WriteAllLines(Path.Combine(artifactsDir, "traceability_matrix.log"), traceLines);

            // Write validation_report.log
            int passed = results.Count(x => x.pass);
            int total = results.Count;
            var validation = new List<string> {
                $"Validation Report — {DateTime.Now}",
                new string('=', 64),
                $"Total: {total}  Passed: {passed}  Failed: {total - passed}",
                (total == passed) ? "STATUS: VALIDATED (all tests passed)" : "STATUS: NOT VALIDATED (some tests failed)",
                ""
            };
            foreach (var t in results)
            {
                validation.Add($"{(t.pass ? "[PASS]" : "[FAIL]")} {t.c.Name} — [{string.Join(", ", t.c.ReqIds)}]");
            }
            File.WriteAllLines(Path.Combine(artifactsDir, "validation_report.log"), validation);

            // Write junit_results.xml
            string junitPath = Path.Combine($"{repoRoot}/tests/python/", "junit_results.xml");
            var suite = new XElement("testsuite",
                new XAttribute("name", "NonDevFunctional"),
                new XAttribute("tests", total),
                new XAttribute("failures", total - passed),
                new XAttribute("time", "0.0"));

            foreach (var t in results)
            {
                var tc = new XElement("testcase",
                    new XAttribute("classname", "NonDev"),
                    new XAttribute("name", t.c.Name),
                    new XAttribute("time", "0.0"));

                if (!t.pass)
                {
                    var msg = $"Expected PumpOn={t.c.ExpPumpOn},Emergency={t.c.ExpEmergency},Reason={t.c.ExpReason} " +
                              $"but got PumpOn={t.r.PumpOn},Emergency={t.r.Emergency},Reason={t.r.Reason}";
                    tc.Add(new XElement("failure", new XAttribute("message", msg), new XCData(msg)));
                }
                suite.Add(tc);
            }

            var doc = new XDocument(new XElement("testsuites", suite));
            doc.Save(junitPath);

            return (artifactsDir, junitPath);
        }
    }
}
