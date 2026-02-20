using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimelineEditor.Models
{
  public class ConfigItem : INotifyPropertyChanged {
    public string Profile {
      get => Settings.Profile;
      set {
        Settings.Profile = value;
        OnPropertyChanged();
      }
    }

    public string Path { get; set; } = "";
    public GeneratorSettings Settings { get; set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
