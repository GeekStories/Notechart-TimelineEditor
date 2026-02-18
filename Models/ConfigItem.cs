
namespace TimelineEditor.Models
{
  public class ConfigItem {
    public string Profile { get; set; } = "";
    public string Path { get; set; } = "";
    public GeneratorSettings Settings { get; set; } = null!;
  }
}
