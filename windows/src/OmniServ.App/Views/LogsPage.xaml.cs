using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OmniServ.App.Services;
using OmniServ.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace OmniServ.App.Views;

public sealed partial class LogsPage : Page
{
    private int _loadId;

    public LogsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (FilePicker.SelectedItem is null)
                PopulateFilesAndSelectDefault();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DispatcherQueue.TryEnqueue(PopulateFilesAndSelectDefault);
    }

    private void PopulateFilesAndSelectDefault()
    {
        var current = FilePicker.SelectedItem as string;
        var files = EngineHost.Instance.Engine.LogFiles();
        FilePicker.ItemsSource = files;

        if (files.Count == 0)
        {
            LogText.Text = $"(no log files found in {Paths.Logs})";
            return;
        }

        if (current is not null && files.Contains(current))
        {
            FilePicker.SelectedItem = current;
        }
        else
        {
            var idx = files.ToList().FindIndex(f => f.Contains("nginx-error"));
            FilePicker.SelectedIndex = idx >= 0 ? idx : 0;
        }

        Load();
    }

    private void File_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FilePicker.SelectedItem is not null) Load();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => PopulateFilesAndSelectDefault();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(Paths.Logs)) Directory.CreateDirectory(Paths.Logs);
            Process.Start(new ProcessStartInfo { FileName = Paths.Logs, UseShellExecute = true });
        }
        catch { }
    }

    private async void Load()
    {
        var id = ++_loadId;
        if (FilePicker.SelectedItem is not string name) return;

        try
        {
            var text = await System.Threading.Tasks.Task.Run(() => EngineHost.Instance.Engine.LogText(name, 500));
            if (id != _loadId) return;

            LogText.Text = string.IsNullOrWhiteSpace(text) ? "(empty)" : text;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try { Scroll.ChangeView(null, Scroll.ScrollableHeight, null); } catch { }
            });
        }
        catch (Exception ex)
        {
            if (id != _loadId) return;
            LogText.Text = $"(error reading log: {ex.Message})";
        }
    }
}
