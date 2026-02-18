using System.Text.Json.Serialization;

namespace TimelineEditor.Models
{
  public class Note {
    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "normal";
  }
}
