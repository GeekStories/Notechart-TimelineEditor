using System.Windows;
using System.Windows.Forms;

namespace TimelineEditor {
  /// <summary>
  /// Interaction logic for SettingsWindow.xaml
  /// </summary>
  public partial class SettingsWindow : Window {
    public SettingsWindow() {
      InitializeComponent();
      ConfigPathTextBox.Text = Properties.Settings.Default.ConfigFolder;
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

    private void Ok_Click(object sender, RoutedEventArgs e) {
      Properties.Settings.Default.ConfigFolder = ConfigPathTextBox.Text;
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
