using OmniServ.App.Services;
using OmniServ.App.Views;
using OmniServ.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OmniServ.App;

public sealed partial class MainWindow : Window
{
    private readonly TrayIcon _tray;
    private bool _reallyQuit;
    private bool _trayHintShown;
    private Updater.Result? _pendingUpdate;   // an available update waiting to be offered (shown when the window is visible)
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };   // daily re-check for long-running (tray) instances

    public MainWindow()
    {
        InitializeComponent();

        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(icon)) { try { AppWindow.SetIcon(icon); } catch { } }

        try { SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); } catch { }

        OnboardingOverlay.Completed += () => DispatcherQueue.TryEnqueue(() =>
        {
            HideOnboarding();
            Nav.SelectedItem = Nav.MenuItems[0];
            ContentFrame.Navigate(typeof(DashboardPage));
        });
        OnboardingOverlay.Skipped += () => DispatcherQueue.TryEnqueue(() =>
        {
            HideOnboarding();
            Nav.SelectedItem = Nav.MenuItems[0];
            ContentFrame.Navigate(typeof(DashboardPage));
        });
        OnboardingOverlay.CreateSiteRequested += () => DispatcherQueue.TryEnqueue(() =>
        {
            HideOnboarding();
            GoToSites(addNew: true);
        });

        _tray = new TrayIcon($"OmniServ {Updater.CurrentVersion} — local web stack", icon);
        _tray.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowFromTray);
        _tray.QuitRequested += () => DispatcherQueue.TryEnqueue(QuitApp);
        _tray.StartAllRequested   += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Start("all"); } catch { } });
        _tray.StopAllRequested    += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Stop("all"); } catch { } });
        _tray.RestartAllRequested += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Restart("all"); } catch { } });

        // Close → hide to tray when "keep running" is on (Settings); otherwise really quit.
        AppWindow.Closing += (_, e) =>
        {
            if (_reallyQuit || !Config.Load().MinimizeToTray) { _tray.Dispose(); return; }
            e.Cancel = true;
            AppWindow.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloon("OmniServ is still running",
                    "Your sites stay up in the background. Click this icon to reopen — use the ^ to show hidden icons if you don't see it. Turn this off in Settings.");
            }
        };

        _ = FirstRunThenUpdateCheck();
        // Re-check once a day so an instance that stays open (tray/autostart) still notices updates
        // without needing a restart. Same gating + prompt as the launch check.
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateOnLaunch();
        _updateTimer.Start();
    }

    /// <summary>On launch: if it's a fresh install with no core stack, offer one-click setup; otherwise
    /// run the normal update check. (A fresh install is on the latest version, so the two never collide.)</summary>
    private async System.Threading.Tasks.Task FirstRunThenUpdateCheck()
    {
        // Wait for the XAML tree to be ready (XamlRoot set) so dialogs can show.
        for (int i = 0; i < 50 && (Content as FrameworkElement)?.XamlRoot is null; i++)
            await System.Threading.Tasks.Task.Delay(100);
        if (await OfferFirstRunSetup()) return;
        await CheckForUpdateOnLaunch();
    }

    /// <summary>First-run welcome: if onboarding has not been completed or the core stack
    /// (nginx + PHP + database + mkcert) isn't installed, activate the dedicated onboarding wizard.
    /// Returns true if onboarding was shown (so we skip the immediate update check).</summary>
    private async System.Threading.Tasks.Task<bool> OfferFirstRunSetup()
    {
        try
        {
            if (!AppWindow.IsVisible) return false;
            var cfg = Config.Load();
            var missing = EngineHost.Instance.Engine.MissingCore();
            if (!cfg.OnboardingCompleted || missing.Count > 0)
            {
                ShowOnboarding();
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>Open the dedicated full-window onboarding wizard.</summary>
    public void StartOnboarding() => DispatcherQueue.TryEnqueue(ShowOnboarding);

    public void ShowOnboarding()
    {
        OnboardingOverlay.Reset();
        OnboardingOverlay.Visibility = Visibility.Visible;
        Nav.Visibility = Visibility.Collapsed;
    }

    public void HideOnboarding()
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        Nav.Visibility = Visibility.Visible;
    }

    /// <summary>On launch (auto-update on), check GitHub for a newer build and PROACTIVELY tell the user —
    /// a popup if the window is visible, or a tray balloon if OmniServ started hidden (autostart). Also
    /// puts the attention dot on the Settings item. The user no longer has to open Settings to discover
    /// an update.</summary>
    private async System.Threading.Tasks.Task CheckForUpdateOnLaunch()
    {
        try
        {
            if (!Config.Load().AutoUpdate) return;
            if (!Updater.AutomaticCheckDue()) return;   // throttle: GitHub allows 60 API req/hr/IP
            Updater.StampAutomaticCheck();              // stamp up-front so a 403/failure also backs off
            var r = await Updater.Check();
            if (!r.UpdateAvailable || r.AssetUrl is null) return;

            if (Nav.SettingsItem is NavigationViewItem si) si.InfoBadge = new InfoBadge();   // attention dot
            _pendingUpdate = r;

            if (AppWindow.IsVisible)
                await ShowUpdatePromptIfPending();
            else
                _tray.ShowBalloon($"OmniServ {r.Latest} is available",
                    "A new version is ready — open OmniServ to update.");
        }
        catch { }
    }

    /// <summary>If an update is waiting, show the "update now / later" prompt. Cleared after one show so
    /// it doesn't re-nag within a session (it re-checks next launch). Safe to call when nothing's pending.</summary>
    private async System.Threading.Tasks.Task ShowUpdatePromptIfPending()
    {
        if (_pendingUpdate is not { AssetUrl: { } asset } r) return;
        if ((Content as FrameworkElement)?.XamlRoot is not { } xamlRoot) return;   // window not ready yet — retry on next show
        _pendingUpdate = null;

        var dlg = new ContentDialog
        {
            Title = $"Update available — OmniServ {r.Latest}",
            Content = $"A new version is ready.\n\nYou have {Updater.CurrentVersion} · latest is {r.Latest}.\n\n" +
                      "Update now? OmniServ will close, install the update, and reopen on its own.",
            PrimaryButtonText = "Update now", CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot,
        };
        try
        {
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                await Updater.DownloadAndRun(asset);
        }
        catch { /* another dialog already open / UAC declined / network — re-offered on the next check */ }
    }

    /// <summary>Autostart-at-login entry point: keep OmniServ running in the tray ONLY. The window is
    /// never Activate()'d (so it never appears on screen and never gets a taskbar button), and we also
    /// Hide() it as a belt-and-suspenders. The tray icon is the only entry point until the user opens it.</summary>
    public void StartHiddenInTray() => AppWindow.Hide();

    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.Restore();
        Activate();
        _ = ShowUpdatePromptIfPending();   // if an update was found while hidden, offer it now that we're visible
        // The user just opened the window → warm session → quietly ensure ionCube is loaded (no-op if it
        // already is). This is what makes ionCube "just work" after a reboot without the fragile boot heal.
        if (ContentFrame.Content is Views.DashboardPage dash) dash.AutoEnableIonCube();
    }

    private void QuitApp()
    {
        _reallyQuit = true;
        _tray.Dispose();
        Application.Current.Exit();
    }

    /// <summary>Self-updater path: mark a real quit (so the close handler doesn't hide to tray) and
    /// remove the tray icon. App.ForceQuit() then exits the process, unlocking the files for the installer.</summary>
    public void QuitForUpdate()
    {
        _reallyQuit = true;
        try { _tray.Dispose(); } catch { }
    }

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.MenuItems[0];   // Dashboard
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private bool _sitesAddPending;

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { ContentFrame.Navigate(typeof(SettingsPage)); return; }
        if (args.SelectedItemContainer is NavigationViewItem { Tag: string tag })
        {
            var page = tag switch
            {
                "sites"     => typeof(SitesPage),
                "databases" => typeof(DatabasesPage),
                "node"      => typeof(NodePage),
                "python"    => typeof(PythonPage),
                "services"  => typeof(ServicesPage),
                "logs"      => typeof(LogsPage),
                _           => typeof(DashboardPage),
            };
            object? param = null;
            if (tag == "sites" && _sitesAddPending) { param = "add"; _sitesAddPending = false; }
            ContentFrame.Navigate(page, param);
        }
    }

    /// <summary>Jump to the Sites tab from elsewhere (e.g. the Dashboard "Add site" button). When
    /// <paramref name="addNew"/> is true the Sites page focuses its name box so the user can type a
    /// site name immediately.</summary>
    public void GoToSites(bool addNew)
    {
        _sitesAddPending = addNew;
        NavigationViewItem? sites = null;
        foreach (var it in Nav.MenuItems)
            if (it is NavigationViewItem nvi && (nvi.Tag as string) == "sites") { sites = nvi; break; }
        if (sites is null) return;
        if (ReferenceEquals(Nav.SelectedItem, sites))   // already on Sites → SelectionChanged won't fire; navigate directly
        {
            object? param = _sitesAddPending ? "add" : null; _sitesAddPending = false;
            ContentFrame.Navigate(typeof(SitesPage), param);
        }
        else Nav.SelectedItem = sites;   // fires Nav_SelectionChanged, which honors _sitesAddPending
    }
}
