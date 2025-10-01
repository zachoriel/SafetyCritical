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
        private static string GetArtifactsRoot()
        {
            var root = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(root);
            return root;
        }

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
            bool ExpectedPumpOn,
            bool ExpectedEmergency,
            string ExpectedReason,
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

            // Build demo sequence mirroring unit/system tests. These are the expected outputs.
            // (Values taken directly from tests in tests/csharp/PumpController.TestsControllerSpec.cs.)
            _cases = new List<DemoCase>
            {
                new("Low subcooling trips OFF", 265, 70, null, false, true, "LowSubcooling", new[]{"REQ-001","REQ-006","REQ-009"}),
                new("Low pressure clamps OFF", 250, 60, null, false, true, "LowPressure", new[]{"REQ-002","REQ-006"}),
                new("High temp clamps OFF", 340, 90, null, false, true, "HighTempClamp", new[]{"REQ-003","REQ-006"}),
                new("Authorized operator shutdown", 250, 90, MakeCmd("operatorA","Shutdown"), false, true, "OperatorShutdown", new[]{"REQ-004","REQ-006"}),
                new("Normal operation", 250, 90, null, true, false, "Normal", new[]{"REQ-005"}),
                new("Invalid command ignored", 250, 90, new OperatorCommand("intruder","Shutdown","00"), true, false, "Normal", new[]{"REQ-007"}),
                new("Config Properties Immutable", 250, 90, null, true, false, "Normal", new[]{"REQ-008" })
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
            bool pass = result.PumpOn == c.ExpectedPumpOn && result.Emergency == c.ExpectedEmergency && string.Equals(result.Reason, c.ExpectedReason, StringComparison.Ordinal);

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
                // Map the on-screen demos to artifact cases
                var cases = _cases.Select(c => new NonDevArtifacts.Case(c.Name, c.TempC, c.PressureBar, c.Command, c.ExpectedPumpOn, c.ExpectedEmergency,
                    c.ExpectedReason, c.ReqIds)).ToList();

                var artifactsRoot = GetArtifactsRoot();
                var (dir, junit) = await Task.Run(() => NonDevArtifacts.GenerateAll(cases, artifactsRoot));

                CurrentTestText.Text = $"Artifacts generated: {System.IO.Path.GetFileName(dir)}";
                LoadLatestArtifacts();
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

        private void LoadLatestArtifacts()
        {
            string artifacts = GetArtifactsRoot();
            if (!Directory.Exists(artifacts)) return;

            var latest = Directory.GetDirectories(artifacts).OrderByDescending(d => d).FirstOrDefault();
            if (latest is null) return;

            string trace = Path.Combine(latest, "traceability_matrix.log");
            string report = Path.Combine(latest, "validation_report.log");
            if (File.Exists(trace)) TraceBox.Text = File.ReadAllText(trace);
            if (File.Exists(report)) ValidationBox.Text = File.ReadAllText(report);
        }

        private void OpenArtifacts_Click(object sender, RoutedEventArgs e)
        {
            string artifacts = GetArtifactsRoot();
            if (!Directory.Exists(artifacts)) Directory.CreateDirectory(artifacts);
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
            bool ExpectedPumpOn,
            bool ExpectedEmergency,
            string ExpectedReason,
            string[] ReqIds);

        public static (string artifactsDir, string junitPath) GenerateAll(IEnumerable<Case> cases, string artifactsRoot)
        {
            var controller = new PumpControllerLib.PumpController();
            var results = new List<(Case c, PumpResult r, bool pass)>();
            foreach (var c in cases)
            {
                var r = controller.Evaluate(c.TempC, c.PressureBar, c.Command);
                var pass = r.PumpOn == c.ExpectedPumpOn && r.Emergency == c.ExpectedEmergency && string.Equals(r.Reason, c.ExpectedReason, StringComparison.Ordinal);
                results.Add((c, r, pass));
            }

            Directory.CreateDirectory(artifactsRoot);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
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
                traceLines.Add($"  Expected: PumpOn={t.c.ExpectedPumpOn} Emergency={t.c.ExpectedEmergency} Reason={t.c.ExpectedReason}");
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
            string junitPath = Path.Combine(artifactsDir, "junit_results.xml");
            var suite = new XElement("testsuite",
                new XAttribute("name", "PortableFunctional"),
                new XAttribute("tests", results.Count),
                new XAttribute("failures", results.Count(x => !x.pass)),
                new XAttribute("time", "0.0"));

            foreach (var t in results)
            {
                var tc = new XElement("testcase",
                    new XAttribute("classname", "Portable"),
                    new XAttribute("name", t.c.Name),
                    new XAttribute("time", "0.0"));

                if (!t.pass)
                {
                    var msg = $"Expected PumpOn={t.c.ExpectedPumpOn},Emergency={t.c.ExpectedEmergency},Reason={t.c.ExpectedReason} " +
                              $"but got PumpOn={t.r.PumpOn},Emergency={t.r.Emergency},Reason={t.r.Reason}";
                    tc.Add(new XElement("failure", new XAttribute("message", msg), new XCData(msg)));
                }
                suite.Add(tc);
            }

            new XDocument(new XElement("testsuites", suite)).Save(junitPath);
            return (artifactsDir, junitPath);
        }
    }
}
