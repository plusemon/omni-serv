using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniServ.App.Services;
using OmniServ.Core;

namespace OmniServ.App.Views;

public sealed partial class OnboardingView : UserControl
{
    private readonly OnboardingInstaller _installer = new();
    private int _currentStep = 1;

    public event Action? Completed;
    public event Action? Skipped;
    public event Action? CreateSiteRequested;

    public OnboardingView()
    {
        InitializeComponent();
        ComponentsList.ItemsSource = _installer.Items;
        _installer.PropertyChanged += Installer_PropertyChanged;
        _installer.InstallCompleted += OnInstallerFinished;
        Loaded += OnboardingView_Loaded;
    }

    private void OnboardingView_Loaded(object sender, RoutedEventArgs e)
    {
        var cfg = Config.Load();
        SitesRootBox.Text = cfg.SitesRoot;
        WebServerCombo.SelectedIndex = cfg.DefaultWeb == "apache" ? 1 : 0;

        PhpVersionCombo.SelectedIndex = cfg.DefaultPhp switch
        {
            "8.3" => 1,
            "8.2" => 2,
            "8.1" => 3,
            _     => 0, // 8.4
        };

        EngineHost.Instance.LogAppended += OnLogAppended;
        CheckDefenderStatus();
    }

    public void Reset()
    {
        _currentStep = 1;
        ShowStep(1);
        var cfg = Config.Load();
        SitesRootBox.Text = cfg.SitesRoot;
        CheckDefenderStatus();
    }

    private void OnLogAppended(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LogOutputText.Text += line + "\n";
            LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
        });
    }

    private void Installer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            OverallProgressBar.Value = _installer.OverallProgress;
            OverallStatusText.Text = _installer.StatusSummary;
            OverallCountText.Text = $"{_installer.CompletedCount} of {_installer.Items.Count} completed";

            if (_installer.HasFailures)
            {
                RetryAllBtn.Visibility = Visibility.Visible;
                ContinueAnywayBtn.Visibility = Visibility.Visible;
                Step3NextBtn.IsEnabled = false;
            }
            else if (_installer.AllSucceeded)
            {
                RetryAllBtn.Visibility = Visibility.Collapsed;
                ContinueAnywayBtn.Visibility = Visibility.Collapsed;
                Step3NextBtn.IsEnabled = true;
            }
            else
            {
                RetryAllBtn.Visibility = Visibility.Collapsed;
                ContinueAnywayBtn.Visibility = Visibility.Collapsed;
                Step3NextBtn.IsEnabled = false;
            }
        });
    }

    private void OnInstallerFinished()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_installer.AllSucceeded)
            {
                Step3NextBtn.IsEnabled = true;
                RetryAllBtn.Visibility = Visibility.Collapsed;
                ContinueAnywayBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                RetryAllBtn.Visibility = Visibility.Visible;
                ContinueAnywayBtn.Visibility = Visibility.Visible;
            }
        });
    }

    private void ShowStep(int step)
    {
        _currentStep = step;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        HighlightStepper(step);
    }

    private void HighlightStepper(int step)
    {
        Step1Pill.Opacity = step == 1 ? 1.0 : 0.5;
        Step2Pill.Opacity = step == 2 ? 1.0 : 0.5;
        Step3Pill.Opacity = step == 3 ? 1.0 : 0.5;
        Step4Pill.Opacity = step == 4 ? 1.0 : 0.5;

        Step1Icon.Opacity = step >= 1 ? 1.0 : 0.4;
        Step2Icon.Opacity = step >= 2 ? 1.0 : 0.4;
        Step3Icon.Opacity = step >= 3 ? 1.0 : 0.4;
        Step4Icon.Opacity = step >= 4 ? 1.0 : 0.4;
    }

    // ── Step 1 ───────────────────────────────────────────────────────────────
    private async void BrowseSitesRoot_Click(object sender, RoutedEventArgs e)
    {
        var picked = await Picker.FolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
            SitesRootBox.Text = picked;
    }

    private void Step1Next_Click(object sender, RoutedEventArgs e)
    {
        var cfg = Config.Load();
        if (!string.IsNullOrWhiteSpace(SitesRootBox.Text))
            cfg.SitesRoot = SitesRootBox.Text.Trim();

        var phpVer = PhpVersionCombo.SelectedIndex switch
        {
            1 => "8.3",
            2 => "8.2",
            3 => "8.1",
            _ => "8.4",
        };
        cfg.DefaultPhp = phpVer;
        cfg.DefaultWeb = WebServerCombo.SelectedIndex == 1 ? "apache" : "nginx";
        cfg.Save();

        // Build tools list
        var tools = new List<string>();

        // Web Server
        if (cfg.DefaultWeb == "apache")
        {
            tools.Add("nginx");   // Apache runs behind nginx
            tools.Add("apache");
        }
        else
        {
            tools.Add("nginx");
        }

        // PHP
        if (PhpCheck.IsChecked == true)
            tools.Add($"php@{phpVer}");

        // Database
        if (DbCheck.IsChecked == true)
        {
            var db = DbEngineCombo.SelectedIndex switch
            {
                1 => "mysql",
                2 => "postgresql",
                _ => "mariadb",
            };
            tools.Add(db);
        }

        // HTTPS
        if (MkcertCheck.IsChecked == true)
            tools.Add("mkcert");

        // Optional Tools
        if (MailpitCheck.IsChecked == true) tools.Add("mailpit");
        if (RedisCheck.IsChecked == true)   tools.Add("redis");
        if (NodeCheck.IsChecked == true)    tools.Add("fnm");
        if (PythonCheck.IsChecked == true)  tools.Add("python");

        _installer.Populate(tools, cfg);

        ShowStep(2);
    }

    // ── Step 2: Defender / Antivirus ─────────────────────────────────────────
    private void CheckDefenderStatus()
    {
        Task.Run(() =>
        {
            var isExcluded = WindowsDefender.AllExcluded(new[] { AppContext.BaseDirectory, Paths.Home });
            DispatcherQueue.TryEnqueue(() =>
            {
                if (isExcluded)
                {
                    AvStatusText.Text = "Protected (Exclusions Active ✓)";
                    AvStatusText.Opacity = 1.0;
                    AvFeedbackText.Text = "OmniServ folders are already excluded in Windows Defender.";
                    AddAvExclusionsBtn.IsEnabled = false;
                }
                else
                {
                    AvStatusText.Text = "Not yet excluded";
                    AvStatusText.Opacity = 0.7;
                    AddAvExclusionsBtn.IsEnabled = true;
                }
            });
        });
    }

    private async void AddAvExclusions_Click(object sender, RoutedEventArgs e)
    {
        AvBusy.IsActive = true;
        AddAvExclusionsBtn.IsEnabled = false;
        AvFeedbackText.Text = "Waiting for Windows permission prompt…";

        var (ok, msg) = await Task.Run(() =>
            WindowsDefender.AddExclusions(AppContext.BaseDirectory, Paths.Home));

        AvBusy.IsActive = false;
        if (ok)
        {
            AvStatusText.Text = "Protected (Exclusions Active ✓)";
            AvFeedbackText.Text = "Exclusions successfully added to Windows Defender.";
            AddAvExclusionsBtn.IsEnabled = false;
        }
        else
        {
            AvFeedbackText.Text = $"Could not add automatically: {msg}. You can continue and add them later.";
            AddAvExclusionsBtn.IsEnabled = true;
        }
    }

    private void Step2Back_Click(object sender, RoutedEventArgs e) => ShowStep(1);

    private void Step2Next_Click(object sender, RoutedEventArgs e)
    {
        ShowStep(3);
        _ = _installer.StartInstallAsync();
    }

    // ── Step 3: Installation & Progress ──────────────────────────────────────
    private async void RetryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            var item = _installer.Items.FirstOrDefault(i => i.Key == key);
            if (item is not null)
                await _installer.RetrySingleAsync(item);
        }
    }

    private async void RetryAll_Click(object sender, RoutedEventArgs e)
    {
        await _installer.StartInstallAsync();
    }

    private void ContinueAnyway_Click(object sender, RoutedEventArgs e)
    {
        PopulateStep4Summary();
        ShowStep(4);
    }

    private void Step3Next_Click(object sender, RoutedEventArgs e)
    {
        PopulateStep4Summary();
        ShowStep(4);
    }

    private void PopulateStep4Summary()
    {
        var cfg = Config.Load();
        SummWebServer.Text = cfg.DefaultWeb == "apache"
            ? "Apache (port 8080) behind Nginx (port 80/443)"
            : "Nginx (Port 80 / 443 HTTPS)";
        SummPhp.Text = $"PHP {cfg.DefaultPhp} (FastCGI)";
        SummDb.Text = "MariaDB / MySQL (127.0.0.1:3306, user: root)";
        SummSitesFolder.Text = cfg.SitesRoot;
    }

    // ── Step 4: Ready & Navigation ───────────────────────────────────────────
    private void CreateSite_Click(object sender, RoutedEventArgs e)
    {
        MarkCompleted();
        CreateSiteRequested?.Invoke();
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        MarkCompleted();
        Completed?.Invoke();
    }

    private void SkipAll_Click(object sender, RoutedEventArgs e)
    {
        MarkCompleted();
        Skipped?.Invoke();
    }

    private void MarkCompleted()
    {
        try
        {
            var cfg = Config.Load();
            cfg.OnboardingCompleted = true;
            cfg.Save();
        }
        catch { }
    }
}
