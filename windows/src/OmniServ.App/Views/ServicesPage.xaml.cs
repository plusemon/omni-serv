using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmniServ.App.Services;
using OmniServ.Core;
using CoreServices = OmniServ.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace OmniServ.App.Views;

/// <summary>Row shown in the services list.</summary>
public sealed class SvcRow
{
    private static readonly SolidColorBrush RunningBadgeBg = new(Color.FromArgb(35, 16, 185, 129));
    private static readonly SolidColorBrush RunningBadgeBorder = new(Color.FromArgb(70, 16, 185, 129));
    private static readonly SolidColorBrush RunningBadgeFg = new(Color.FromArgb(255, 74, 222, 128));
    private static readonly SolidColorBrush RunningDot = new(Color.FromArgb(255, 74, 222, 128));

    private static readonly SolidColorBrush StoppedBadgeBg = new(Color.FromArgb(30, 255, 255, 255));
    private static readonly SolidColorBrush StoppedBadgeBorder = new(Color.FromArgb(50, 255, 255, 255));
    private static readonly SolidColorBrush StoppedBadgeFg = new(Color.FromArgb(255, 161, 161, 170));
    private static readonly SolidColorBrush StoppedDot = new(Color.FromArgb(255, 113, 113, 122));

    private static readonly SolidColorBrush NotInstalledBadgeBg = new(Color.FromArgb(15, 255, 255, 255));
    private static readonly SolidColorBrush NotInstalledBadgeBorder = new(Color.FromArgb(30, 255, 255, 255));
    private static readonly SolidColorBrush NotInstalledBadgeFg = new(Color.FromArgb(255, 120, 136, 155));
    private static readonly SolidColorBrush NotInstalledDot = new(Color.FromArgb(255, 90, 102, 120));

    private static readonly SolidColorBrush StarActiveBrush = new(Color.FromArgb(255, 251, 191, 36));
    private static readonly SolidColorBrush StarInactiveBrush = new(Color.FromArgb(255, 140, 140, 140));

    public required string Key { get; init; }
    public required string Version { get; init; }
    public required bool Installed { get; init; }
    public required bool Running { get; init; }
    public required bool AutoStart { get; init; }
    public required bool Manageable { get; init; }
    public required ServiceRole Role { get; init; }
    public required string DefaultPhp { get; init; }
    public required string RamText { get; init; }

    public bool IsPhp => Key.StartsWith("php@");
    public string PhpVer => IsPhp ? Key["php@".Length..] : "";
    public bool IsDefaultPhp => IsPhp && PhpVer == DefaultPhp;
    public bool IsOlderPhp => PhpVer is "8.1" or "7.4";

    public string DisplayName => Key switch
    {
        "nginx" => "Nginx",
        "apache" => "Apache HTTPD",
        "mariadb" => "MariaDB",
        "mysql" => "MySQL",
        "postgresql" => "PostgreSQL",
        "redis" => "Redis",
        "memcached" => "Memcached",
        "mailpit" => "Mailpit",
        "composer" => "Composer",
        "fnm" => "fnm (Node.js)",
        "mkcert" => "mkcert (Local CA)",
        "python" => "Python",
        _ when Key.StartsWith("php@") => $"PHP {Key["php@".Length..]}",
        _ when Key == "php" => "PHP",
        _ => Key,
    };

    public string Monogram => Key switch
    {
        "nginx" => "NG",
        "apache" => "AP",
        "mariadb" => "MD",
        "mysql" => "MY",
        "postgresql" => "PG",
        "redis" => "RD",
        "memcached" => "MC",
        "mailpit" => "MP",
        "composer" => "CP",
        "fnm" => "FN",
        "mkcert" => "MK",
        "python" => "PY",
        _ when IsPhp => "PHP",
        _ => Key.Length >= 2 ? Key[..2].ToUpperInvariant() : Key.ToUpperInvariant(),
    };

    public SolidColorBrush MonogramBgBrush => Key switch
    {
        "nginx" => new(Color.FromArgb(35, 16, 185, 129)),
        "apache" => new(Color.FromArgb(40, 239, 68, 68)),
        "mariadb" => new(Color.FromArgb(35, 14, 165, 233)),
        "mysql" => new(Color.FromArgb(35, 59, 130, 246)),
        "postgresql" => new(Color.FromArgb(35, 99, 102, 241)),
        "redis" => new(Color.FromArgb(40, 249, 115, 22)),
        "memcached" => new(Color.FromArgb(40, 245, 158, 11)),
        "mailpit" => new(Color.FromArgb(40, 168, 85, 247)),
        "composer" => new(Color.FromArgb(35, 217, 119, 6)),
        "fnm" => new(Color.FromArgb(35, 16, 185, 129)),
        "mkcert" => new(Color.FromArgb(35, 100, 116, 139)),
        "python" => new(Color.FromArgb(35, 14, 165, 233)),
        _ when Key.StartsWith("php") => new(Color.FromArgb(40, 99, 102, 241)),
        _ => new(Color.FromArgb(25, 255, 255, 255)),
    };

    public SolidColorBrush MonogramFgBrush => Key switch
    {
        "nginx" => new(Color.FromArgb(255, 52, 211, 153)),
        "apache" => new(Color.FromArgb(255, 248, 113, 113)),
        "mariadb" => new(Color.FromArgb(255, 56, 189, 248)),
        "mysql" => new(Color.FromArgb(255, 96, 165, 250)),
        "postgresql" => new(Color.FromArgb(255, 129, 140, 248)),
        "redis" => new(Color.FromArgb(255, 251, 146, 60)),
        "memcached" => new(Color.FromArgb(255, 251, 191, 36)),
        "mailpit" => new(Color.FromArgb(255, 192, 132, 252)),
        "composer" => new(Color.FromArgb(255, 245, 158, 11)),
        "fnm" => new(Color.FromArgb(255, 52, 211, 153)),
        "mkcert" => new(Color.FromArgb(255, 148, 163, 184)),
        "python" => new(Color.FromArgb(255, 56, 189, 248)),
        _ when Key.StartsWith("php") => new(Color.FromArgb(255, 129, 140, 248)),
        _ => new(Color.FromArgb(255, 161, 161, 170)),
    };

    public string MetaText
    {
        get
        {
            var port = Key switch
            {
                "nginx" => "Port: 80, 443",
                "apache" => "Port: 8080",
                "mariadb" or "mysql" => "Port: 3306",
                "postgresql" => "Port: 5432",
                "redis" => "Port: 6379",
                "memcached" => "Port: 11211",
                "mailpit" => "Port: 8025 (UI), 1025 (SMTP)",
                _ when IsPhp => $"Port: {PhpCgi.PortFor(PhpVer)} · FastCGI",
                _ => "CLI Tool",
            };

            if (!Installed)
                return Manageable ? $"{port} · Not installed" : "Not installed";

            var cleanVer = CleanVersion(Version);
            if (!string.IsNullOrEmpty(cleanVer))
            {
                var v = cleanVer.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? cleanVer : $"v{cleanVer}";
                return Manageable ? $"{v} • {port}" : $"{v} • CLI Tool";
            }

            return port;
        }
    }

    private static string CleanVersion(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var parts = v.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && (char.IsDigit(parts[1][0]) || parts[1].StartsWith("v", StringComparison.OrdinalIgnoreCase)))
            return parts[1];
        return v;
    }

    public string StatusBadgeText => !Installed ? "Not Installed" : !Manageable ? "Ready" : Running ? "Running" : "Stopped";
    public SolidColorBrush StatusBadgeBgBrush => Running || (Installed && !Manageable) ? RunningBadgeBg : Installed ? StoppedBadgeBg : NotInstalledBadgeBg;
    public SolidColorBrush StatusBadgeBorderBrush => Running || (Installed && !Manageable) ? RunningBadgeBorder : Installed ? StoppedBadgeBorder : NotInstalledBadgeBorder;
    public SolidColorBrush StatusBadgeFgBrush => Running || (Installed && !Manageable) ? RunningBadgeFg : Installed ? StoppedBadgeFg : NotInstalledBadgeFg;
    public SolidColorBrush StatusDotBrush => Running || (Installed && !Manageable) ? RunningDot : Installed ? StoppedDot : NotInstalledDot;

    public Visibility DefaultBadgeVis => IsDefaultPhp ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManageVis => Manageable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstallVis => !Installed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StartVis => Installed && !Running && Manageable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVis => Installed && Running && Manageable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoreVis => Installed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RamVis => !string.IsNullOrEmpty(RamText) ? Visibility.Visible : Visibility.Collapsed;

    public string StarGlyph => AutoStart ? "\uE735" : "\uE734";
    public SolidColorBrush StarFgBrush => AutoStart ? StarActiveBrush : StarInactiveBrush;

    public bool HasConfig => IsPhp || Key is "nginx" or "apache" or "mariadb" or "mysql" or "postgresql";
    public Visibility EditConfigVis => HasConfig && Installed ? Visibility.Visible : Visibility.Collapsed;
    public string ConfigMenuText => Key switch
    {
        _ when IsPhp => "Edit php.ini",
        "nginx" => "Edit nginx.conf",
        "apache" => "Edit httpd.conf",
        "mariadb" or "mysql" => "Edit my.ini",
        "postgresql" => "Edit postgresql.conf",
        _ => "Edit Config",
    };

    public bool HasShell => IsPhp || Key is "mariadb" or "mysql" or "postgresql" or "python" or "fnm" or "mailpit";
    public Visibility OpenShellVis => HasShell && Installed ? Visibility.Visible : Visibility.Collapsed;
    public string ShellMenuText => Key == "mailpit" ? "Open Web UI" : "Open Terminal (CLI)";
    public string ShellMenuGlyph => Key == "mailpit" ? "\uE774" : "\uE756";

    public bool HasLog => IsPhp || Key is "nginx" or "apache" or "mariadb" or "mysql" or "postgresql";
    public Visibility ViewLogVis => HasLog ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SetDefaultPhpVis => IsPhp && Installed && !IsDefaultPhp ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstalledVis => Installed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SeparatorVis => (SetDefaultPhpVis == Visibility.Visible || OpenShellVis == Visibility.Visible || EditConfigVis == Visibility.Visible || ViewLogVis == Visibility.Visible) && InstalledVis == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    public bool Matches(string q) =>
        DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
        || Key.Contains(q, StringComparison.OrdinalIgnoreCase)
        || MetaText.Contains(q, StringComparison.OrdinalIgnoreCase)
        || Version.Contains(q, StringComparison.OrdinalIgnoreCase)
        || StatusBadgeText.Contains(q, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A titled group of services (PHP Runtimes, Web servers, …).</summary>
public sealed class SvcGroup
{
    public required string Title { get; init; }
    public required string CountText { get; init; }
    public required List<SvcRow> Rows { get; init; }
    public bool HasOlderToggle { get; init; }
    public string OlderToggleText { get; init; } = "";
    public Visibility OlderToggleVis => HasOlderToggle ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class ServicesPage : Page
{
    private static readonly (string cat, string title)[] Categories =
    {
        ("web", "WEB SERVERS & GATEWAYS"),
        ("php", "PHP RUNTIMES"),
        ("db", "DATABASES"),
        ("cache", "CACHE & MEMORY"),
        ("mail", "MAIL & NETWORK"),
        ("tools", "DEVELOPER TOOLS & RUNTIMES"),
    };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private List<SvcRow> _allRows = new();
    private string _searchQuery = "";
    private bool _showOlderPhp;

    public ServicesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        EngineHost.Instance.OpChanged += OnOpChanged;
        _timer.Start();
        RenderOp();   // re-attach to an install that's still running
        if (EngineHost.Instance.LastSnapshot is { } last) RenderSnapshot(last);
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        EngineHost.Instance.OpChanged -= OnOpChanged;
    }

    private void OnOpChanged() => DispatcherQueue.TryEnqueue(() => { RenderOp(); if (EngineHost.Instance.CurrentOp is { Running: false }) Refresh(); });

    private void RenderOp()
    {
        var op = EngineHost.Instance.CurrentOp;
        if (op is null) { OpBanner.Visibility = Visibility.Collapsed; return; }
        OpBanner.Visibility = Visibility.Visible;
        OpName.Text = op.Name;
        OpMsg.Text = op.Message;
        OpBar.IsIndeterminate = op.Running && op.Progress < 0;
        OpBar.Value = op.Progress < 0 ? 0 : op.Progress;
        OpPct.Text = op.Running && op.Progress >= 0 ? $"{op.Progress:0}%" : op.Running ? "working…" : op.Success ? "done" : "failed";
        OpDismiss.Visibility = op.Running ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpDismiss_Click(object sender, RoutedEventArgs e) { EngineHost.Instance.DismissOp(); RenderOp(); }

    private void RenderSnapshot(Snapshot snap)
    {
        var cfg = Config.Load();

        // Exclude the redundant bare "php" key since versioned keys (php@8.5, etc.) are shown
        var services = snap.Services.Where(s => s.Key != "php").ToList();

        _allRows = services.Select(s =>
        {
            var isManageable = s.Role is ServiceRole.Php
                || s.Key is "nginx" or "apache" or "mysql" or "mariadb" or "postgresql" or "redis" or "memcached" or "mailpit";

            var ram = s.Running ? GetServiceRam(s.Key, s.Key.StartsWith("php@") ? s.Key["php@".Length..] : "") : "";

            return new SvcRow
            {
                Key = s.Key,
                Version = OmniServ.Core.Services.ShortVersion(s.Key, cfg),
                Installed = s.Installed,
                Running = s.Running,
                AutoStart = s.AutoStart,
                Manageable = isManageable,
                Role = s.Role,
                DefaultPhp = cfg.DefaultPhp,
                RamText = ram,
            };
        }).ToList();

        ApplyFilterAndRender();
    }

    private static string CategoryOf(SvcRow r) => r.Role switch
    {
        ServiceRole.Web => "web",
        ServiceRole.Php => "php",
        ServiceRole.Db => "db",
        ServiceRole.Cache => "cache",
        ServiceRole.Mail => "mail",
        _ => "tools",
    };

    private void ApplyFilterAndRender()
    {
        var q = _searchQuery.Trim();
        var isSearching = !string.IsNullOrEmpty(q);

        var filtered = isSearching
            ? _allRows.Where(r => r.Matches(q)).ToList()
            : _allRows;

        var groups = new List<SvcGroup>();

        foreach (var (cat, title) in Categories)
        {
            var inCat = filtered.Where(r => CategoryOf(r) == cat).ToList();
            if (inCat.Count == 0) continue;

            bool hasOlderToggle = false;
            string olderToggleText = "";

            if (cat == "php" && !isSearching)
            {
                var olderRows = inCat.Where(r => r.IsOlderPhp && !r.Installed && !r.Running).ToList();
                if (olderRows.Count > 0)
                {
                    hasOlderToggle = true;
                    if (!_showOlderPhp)
                    {
                        inCat = inCat.Except(olderRows).ToList();
                        var vers = string.Join(", ", olderRows.Select(r => r.PhpVer));
                        olderToggleText = $"+ Show {olderRows.Count} older versions ({vers})...";
                    }
                    else
                    {
                        olderToggleText = "- Hide older versions";
                    }
                }
            }

            var countLabel = cat == "php"
                ? $"{inCat.Count} version{(inCat.Count == 1 ? "" : "s")}"
                : $"{inCat.Count} service{(inCat.Count == 1 ? "" : "s")}";

            groups.Add(new SvcGroup
            {
                Title = title,
                CountText = countLabel,
                Rows = inCat,
                HasOlderToggle = hasOlderToggle,
                OlderToggleText = olderToggleText,
            });
        }

        Groups.ItemsSource = groups;
        EmptyNotice.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetServiceRam(string key, string version)
    {
        try
        {
            if (key.StartsWith("php"))
            {
                var info = PhpCgi.Info(version);
                if (info is not null)
                {
                    using var p = Process.GetProcessById(info.Pid);
                    var mb = p.WorkingSet64 / (1024.0 * 1024.0);
                    return $"RAM: {mb:0.1} MB";
                }
            }
            else
            {
                var procName = key switch
                {
                    "nginx" => "nginx",
                    "apache" => "httpd",
                    "mariadb" or "mysql" => "mysqld",
                    "postgresql" => "postgres",
                    "redis" => "redis-server",
                    "memcached" => "memcached",
                    "mailpit" => "mailpit",
                    _ => null,
                };
                if (procName is not null)
                {
                    var procs = Process.GetProcessesByName(procName);
                    if (procs.Length > 0)
                    {
                        long total = 0;
                        foreach (var p in procs) { total += p.WorkingSet64; p.Dispose(); }
                        var mb = total / (1024.0 * 1024.0);
                        return $"RAM: {mb:0.1} MB";
                    }
                }
            }
        }
        catch { }
        return "";
    }

    private async void Refresh()
    {
        if (Groups.ItemsSource is null) Busy.IsActive = true;
        Snapshot snap;
        try { snap = await EngineHost.Instance.Snapshot(); }
        catch { return; }
        finally { Busy.IsActive = false; }

        RenderSnapshot(snap);
    }

    private static string InstallToken(string key) => key;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text?.Trim() ?? "";
        ApplyFilterAndRender();
    }

    private void OlderToggle_Click(object sender, RoutedEventArgs e)
    {
        _showOlderPhp = !_showOlderPhp;
        ApplyFilterAndRender();
    }

    private void AutoStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.Tag is not string key) return;
        var cfg = Config.Load();
        if (tb.IsChecked == true) CoreServices.Enable(key, cfg);
        else CoreServices.Disable(key, cfg);
        Refresh();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    { if ((sender as Button)?.Tag is string key) await Track($"Installing {key}", () => EngineHost.Instance.Engine.Install(InstallToken(key))); }

    private async void Start_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is string key) await Track($"Starting {key}", () => EngineHost.Instance.Engine.Start(key)); }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is string key) await Track($"Stopping {key}", () => EngineHost.Instance.Engine.Stop(key)); }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is string key) await Track($"Restarting {key}", () => EngineHost.Instance.Engine.Restart(key)); }

    private async void Update_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.Tag is string key) await Track($"Updating {key}", () => EngineHost.Instance.Engine.Update(InstallToken(key))); }

    private async void StartAll_Click(object sender, RoutedEventArgs e)
        => await Track("Starting all services", () => EngineHost.Instance.Engine.Start("all"));

    private async void StopAll_Click(object sender, RoutedEventArgs e)
        => await Track("Stopping all services", () => EngineHost.Instance.Engine.Stop("all"));

    private async void RestartAll_Click(object sender, RoutedEventArgs e)
        => await Track("Restarting services", () => EngineHost.Instance.Engine.Restart("all"));

    private async void SetDefaultPhp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        var ver = CoreServices.PhpVersion(key, Config.Load());
        var cfg = Config.Load();
        cfg.DefaultPhp = ver;
        cfg.Save();
        Php.SyncDefaultPhpToUserPath(ver);
        Refresh();
    }

    private void OpenShell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        try
        {
            if (key.StartsWith("php"))
            {
                var ver = CoreServices.PhpVersion(key, Config.Load());
                var exe = Tools.PhpCgiExe(ver);
                if (exe is not null)
                {
                    var dir = Path.GetDirectoryName(exe)!;
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoExit -Command \"$env:Path = '{dir};' + $env:Path; Write-Host 'OmniServ PHP {ver} CLI Shell' -ForegroundColor Green; php -v\"",
                        UseShellExecute = true,
                    });
                }
            }
            else if (key is "mariadb" or "mysql")
            {
                var client = Tools.MysqlClientFor(key);
                if (client is not null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k \"\"{client}\" -u root\"",
                        UseShellExecute = true,
                    });
                }
            }
            else if (key == "postgresql")
            {
                var psql = Tools.PsqlExe();
                if (psql is not null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k \"\"{psql}\" -U postgres\"",
                        UseShellExecute = true,
                    });
                }
            }
            else if (key == "mailpit")
            {
                Process.Start(new ProcessStartInfo { FileName = "http://localhost:8025", UseShellExecute = true });
            }
            else if (key == "python")
            {
                var py = Tools.PythonExe();
                if (py is not null)
                {
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k \"\"{py}\"\"", UseShellExecute = true });
                }
            }
        }
        catch { }
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        try
        {
            var cfg = Config.Load();
            var path = key switch
            {
                _ when key.StartsWith("php") => EngineHost.Instance.Engine.PhpIniPath(CoreServices.PhpVersion(key, cfg)),
                "nginx" => Path.Combine(Paths.Home, "nginx", "nginx.conf"),
                "apache" => Path.Combine(Paths.Home, "apache", "httpd.conf"),
                "mariadb" => Path.Combine(Paths.Home, "data-mariadb", "my.ini"),
                "mysql" => Path.Combine(Paths.Home, "data", "my.ini"),
                "postgresql" => Path.Combine(Paths.Home, "pgdata", "postgresql.conf"),
                _ => null,
            };

            if (path is not null && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }
        catch { }
    }

    private void ViewLog_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        try
        {
            var log = key switch
            {
                "nginx" => Path.Combine(Paths.Logs, "nginx-error.log"),
                "apache" => Path.Combine(Paths.Logs, "apache-error.log"),
                "mariadb" => Path.Combine(Paths.Logs, "mariadb.err"),
                "mysql" => Path.Combine(Paths.Logs, "mysql.err"),
                "postgresql" => Path.Combine(Paths.Logs, "postgresql.log"),
                _ when key.StartsWith("php") => Path.Combine(Paths.Logs, "php-heal.log"),
                _ => null,
            };

            var target = log is not null && File.Exists(log) ? log : Paths.Logs;
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch { }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        var dlg = new ContentDialog
        {
            Title = "Uninstall",
            Content = $"Remove {key} binaries from OmniServ's bin folder?",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await Track($"Uninstalling {key}", () => EngineHost.Instance.Engine.Uninstall(key));
    }

    private async Task Track(string name, Action action)
    {
        await EngineHost.Instance.RunTracked(name, action);
        Refresh();
    }
}

