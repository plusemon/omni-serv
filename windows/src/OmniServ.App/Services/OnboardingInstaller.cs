using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OmniServ.Core;

namespace OmniServ.App.Services;

public enum OnboardingItemStatus
{
    Queued,
    Downloading,
    Extracting,
    Completed,
    Failed,
    Skipped
}

public sealed class OnboardingItem : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }

    private OnboardingItemStatus _status = OnboardingItemStatus.Queued;
    public OnboardingItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(StatusColorBrushKey));
            }
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.1)
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressLabel));
            }
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusLabel => Status switch
    {
        OnboardingItemStatus.Queued      => "Waiting in queue",
        OnboardingItemStatus.Downloading => $"Downloading {Progress:0}%",
        OnboardingItemStatus.Extracting  => "Extracting & configuring…",
        OnboardingItemStatus.Completed   => "Installed & ready",
        OnboardingItemStatus.Failed      => "Installation failed",
        OnboardingItemStatus.Skipped     => "Skipped",
        _                                => "",
    };

    public string ProgressLabel => Status == OnboardingItemStatus.Downloading
        ? $"{Progress:0}%"
        : Status == OnboardingItemStatus.Extracting
            ? "Extracting…"
            : "";

    public bool IsActive => Status is OnboardingItemStatus.Downloading or OnboardingItemStatus.Extracting;
    public bool IsDone   => Status == OnboardingItemStatus.Completed;
    public bool IsFailed => Status == OnboardingItemStatus.Failed;

    public string StatusColorBrushKey => Status switch
    {
        OnboardingItemStatus.Completed   => "SystemFillColorSuccessBrush",
        OnboardingItemStatus.Failed      => "SystemFillColorCriticalBrush",
        OnboardingItemStatus.Downloading => "AccentFillColorDefaultBrush",
        OnboardingItemStatus.Extracting  => "AccentFillColorDefaultBrush",
        _                                => "CardStrokeColorDefaultBrush",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Coordinates granular download and configuration for the onboarding setup wizard.
/// Tracks per-item progress, isolates errors, supports single-item retries,
/// and starts installed services upon completion.
/// </summary>
public sealed class OnboardingInstaller : INotifyPropertyChanged
{
    public ObservableCollection<OnboardingItem> Items { get; } = new();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (_isRunning != value) { _isRunning = value; OnPropertyChanged(); } }
    }

    private double _overallProgress;
    public double OverallProgress
    {
        get => _overallProgress;
        private set { if (Math.Abs(_overallProgress - value) > 0.5) { _overallProgress = value; OnPropertyChanged(); } }
    }

    private string _statusSummary = "";
    public string StatusSummary
    {
        get => _statusSummary;
        private set { if (_statusSummary != value) { _statusSummary = value; OnPropertyChanged(); } }
    }

    private int _completedCount;
    public int CompletedCount
    {
        get => _completedCount;
        private set { if (_completedCount != value) { _completedCount = value; OnPropertyChanged(); } }
    }

    private int _failedCount;
    public int FailedCount
    {
        get => _failedCount;
        private set { if (_failedCount != value) { _failedCount = value; OnPropertyChanged(); } }
    }

    public bool HasFailures => FailedCount > 0;
    public bool AllSucceeded => Items.Count > 0 && CompletedCount == Items.Count;

    public event Action? InstallCompleted;

    public void Populate(IEnumerable<string> toolKeys, Config cfg)
    {
        Items.Clear();
        foreach (var key in toolKeys.Distinct())
        {
            var already = OmniServ.Core.Services.Installed(key, cfg);
            var (name, desc, glyph) = GetItemMeta(key, cfg);
            Items.Add(new OnboardingItem
            {
                Key = key,
                Name = name,
                Description = desc,
                Glyph = glyph,
                Status = already ? OnboardingItemStatus.Completed : OnboardingItemStatus.Queued,
                Progress = already ? 100 : 0,
            });
        }
        UpdateCounts();
    }

    private static (string name, string desc, string glyph) GetItemMeta(string key, Config cfg) => key switch
    {
        "nginx"       => ("Nginx Web Server", "High-performance HTTP & reverse proxy server", "\uE774"),
        "apache"      => ("Apache Web Server", "HTTP server for native .htaccess support", "\uE774"),
        "mariadb"     => ("MariaDB Database", "Fast, open-source MySQL-compatible database", "\uE8F1"),
        "mysql"       => ("MySQL Database", "Official Oracle MySQL Community Server", "\uE8F1"),
        "postgresql"  => ("PostgreSQL", "Powerful open-source object-relational database", "\uE8F1"),
        "mkcert"      => ("mkcert (Local HTTPS)", "Issues trusted SSL certificates for local domains", "\uE72E"),
        "mailpit"     => ("Mailpit Email Server", "Local SMTP testing tool and web inbox", "\uE715"),
        "redis"       => ("Redis Server", "In-memory cache and key-value datastore", "\uE776"),
        "memcached"   => ("Memcached Server", "High-performance distributed memory cache", "\uE776"),
        "fnm"         => ("Node.js (fnm)", "Fast Node Manager for modern JavaScript tooling", "\uE7BE"),
        "python"      => ("Python 3", "Standalone portable Python runtime", "\uE943"),
        _ when key.StartsWith("php") =>
            ($"PHP {OmniServ.Core.Services.PhpVersion(key, cfg)}", "FastCGI runtime with essential extensions enabled", "\uE943"),
        _             => (key, "System component", "\uE71D"),
    };

    public async Task StartInstallAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        IsRunning = true;
        StatusSummary = "Starting downloads…";

        try
        {
            var pending = Items.Where(i => i.Status is OnboardingItemStatus.Queued or OnboardingItemStatus.Failed).ToList();
            for (int i = 0; i < pending.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var item = pending[i];
                await InstallSingleItemAsync(item, ct);
                UpdateCounts();
            }

            // Once complete, automatically start core services if any were installed
            var cfg = Config.Load();
            var coreInstalled = Items.Any(it => it.Status == OnboardingItemStatus.Completed && (it.Key is "nginx" or "mariadb" or "mysql" || it.Key.StartsWith("php")));
            if (coreInstalled)
            {
                StatusSummary = "Starting local services…";
                await Task.Run(() =>
                {
                    try { EngineHost.Instance.Engine.Start("all"); } catch { }
                });
            }

            if (FailedCount == 0)
            {
                StatusSummary = "All components installed and ready!";
                cfg.OnboardingCompleted = true;
                cfg.Save();
            }
            else
            {
                StatusSummary = $"{FailedCount} component(s) encountered issues. You can retry or continue.";
            }

            InstallCompleted?.Invoke();
        }
        finally
        {
            IsRunning = false;
            UpdateCounts();
        }
    }

    public async Task RetrySingleAsync(OnboardingItem item, CancellationToken ct = default)
    {
        if (IsRunning) return;
        IsRunning = true;
        try
        {
            item.Status = OnboardingItemStatus.Queued;
            item.ErrorMessage = null;
            item.Progress = 0;
            UpdateCounts();

            await InstallSingleItemAsync(item, ct);
            UpdateCounts();

            if (FailedCount == 0)
            {
                var cfg = Config.Load();
                cfg.OnboardingCompleted = true;
                cfg.Save();
                StatusSummary = "All components ready!";
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task InstallSingleItemAsync(OnboardingItem item, CancellationToken ct)
    {
        item.Status = OnboardingItemStatus.Downloading;
        item.Progress = 0;
        item.ErrorMessage = null;
        StatusSummary = $"Installing {item.Name}…";

        string? capturedError = null;
        void OnLog(string line)
        {
            if (line.Contains("✗", StringComparison.Ordinal) || line.Contains("failed:", StringComparison.OrdinalIgnoreCase))
            {
                capturedError = line.Trim().TrimStart('✗', ' ', '\t');
            }
        }

        EngineHost.Instance.LogAppended += OnLog;
        Downloader.OnProgress = p =>
        {
            item.Progress = p;
            if (p >= 99.5)
            {
                item.Status = OnboardingItemStatus.Extracting;
            }
            UpdateOverallProgress();
        };

        try
        {
            await Task.Run(() =>
            {
                EngineHost.Instance.Engine.Install(item.Key);
            }, ct);

            var cfg = Config.Load();
            if (OmniServ.Core.Services.Installed(item.Key, cfg))
            {
                item.Status = OnboardingItemStatus.Completed;
                item.Progress = 100;
                item.ErrorMessage = null;
            }
            else
            {
                item.Status = OnboardingItemStatus.Failed;
                item.ErrorMessage = capturedError ?? "File extraction incomplete. Check if an antivirus quarantined the file.";
            }
        }
        catch (Exception ex)
        {
            item.Status = OnboardingItemStatus.Failed;
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            Downloader.OnProgress = null;
            EngineHost.Instance.LogAppended -= OnLog;
            UpdateOverallProgress();
        }
    }

    private void UpdateCounts()
    {
        CompletedCount = Items.Count(i => i.Status == OnboardingItemStatus.Completed);
        FailedCount    = Items.Count(i => i.Status == OnboardingItemStatus.Failed);
        UpdateOverallProgress();
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(AllSucceeded));
    }

    private void UpdateOverallProgress()
    {
        if (Items.Count == 0)
        {
            OverallProgress = 0;
            return;
        }

        double total = 0;
        foreach (var item in Items)
        {
            if (item.Status == OnboardingItemStatus.Completed) total += 100;
            else if (item.Status == OnboardingItemStatus.Downloading) total += item.Progress;
            else if (item.Status == OnboardingItemStatus.Extracting) total += 95;
        }
        OverallProgress = Math.Clamp(total / Items.Count, 0, 100);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
