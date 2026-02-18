using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TimelineEditor {
  public static class ConfigWatcherService {
    private static FileSystemWatcher? _watcher;
    private static DispatcherTimer? _debounceTimer;

    public static event Action? ConfigsChanged;

    public static void Start(string folder, bool changed = false) {
      Stop(changed);

      if(!Directory.Exists(folder)) {
        Debug.WriteLine("Selected Config Directory doesn't exist");
        return;
      }

      _watcher = new FileSystemWatcher(folder, "*.json") {
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        EnableRaisingEvents = true
      };

      _watcher.Created += OnChanged;
      _watcher.Deleted += OnChanged;
      _watcher.Renamed += OnChanged;
    }

    public static void RaiseConfigsChanged() { 
      Start(Properties.Settings.Default.ConfigFolder, true);
      ConfigsChanged?.Invoke();
    }

    private static void OnChanged(object sender, FileSystemEventArgs e) {
      Application.Current.Dispatcher.Invoke(() => {
        _debounceTimer ??= new DispatcherTimer {
          Interval = TimeSpan.FromMilliseconds(200)
        };

        _debounceTimer.Stop();
        _debounceTimer.Tick -= DebounceTick;
        _debounceTimer.Tick += DebounceTick;
        _debounceTimer.Start();
      });
    }

    private static void DebounceTick(object? sender, EventArgs e) {
      _debounceTimer?.Stop();
      ConfigsChanged?.Invoke();
    }

    public static void Stop(bool changed = false) {
      if(_watcher == null)
        return;

      _watcher.EnableRaisingEvents = false;

      _watcher.Created -= OnChanged;
      _watcher.Deleted -= OnChanged;
      _watcher.Renamed -= OnChanged;

      _watcher.Dispose();
      _watcher = null;

      if(!changed) ConfigsChanged = null;
    }
  }
}