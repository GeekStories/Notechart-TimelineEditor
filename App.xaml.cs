using System.Diagnostics;
using System.Windows;

namespace TimelineEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
    protected override void OnExit(ExitEventArgs e) {
      Debug.WriteLine("Threads still alive:");
      foreach(ProcessThread t in Process.GetCurrentProcess().Threads) {
        Debug.WriteLine($"Thread {t.Id} State={t.ThreadState}");
      }
      ConfigWatcherService.Stop();
      base.OnExit(e);
    }
  }
}
