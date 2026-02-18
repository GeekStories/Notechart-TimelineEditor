using System.Windows;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace TimelineEditor {
  /// <summary>
  /// Interaction logic for SettingsWindow.xaml
  /// </summary>
  public partial class SettingsWindow : Window {
    public SettingsWindow() {
      InitializeComponent();
      ConfigPathTextBox.Text = Properties.Settings.Default.ConfigFolder;
      ffmpegPathTextBox.Text = Properties.Settings.Default.ffmpeg;
    }

    private void BrowseConfigFolder_Click(object sender, RoutedEventArgs e) {
      using var dialog = new FolderBrowserDialog {
        Description = "Select configuration folder",
        UseDescriptionForTitle = true,
        SelectedPath = ConfigPathTextBox.Text
      };

      if(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
        ConfigPathTextBox.Text = dialog.SelectedPath;
      }
    }
    private void BrowserFFMPEG_Click(object sender, RoutedEventArgs e) {
      using var dialog = new OpenFileDialog {
        Title = "Select ffmpeg executable",
        Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
        CheckFileExists = true
      };
      if(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
        ffmpegPathTextBox.Text = dialog.FileName;
        Properties.Settings.Default.Save();
      }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) {
      Properties.Settings.Default.ConfigFolder = ConfigPathTextBox.Text;
      Properties.Settings.Default.ffmpeg = ffmpegPathTextBox.Text;
      Properties.Settings.Default.Save();

      // Reload configs immediately after changing the folder
      ConfigWatcherService.RaiseConfigsChanged();

      Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
      Close();
    }
  }
}
