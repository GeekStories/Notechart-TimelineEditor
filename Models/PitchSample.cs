using System.Text.Json.Serialization;

namespace TimelineEditor.Models
{
  public class PitchSample {
    [JsonPropertyName("time")]
    public double Time { get; set; }   // seconds
    [JsonPropertyName("pitch")]
    public double Pitch { get; set; }  // Hz
    [JsonPropertyName("midi")]
    public double Midi { get; set; }   // Midi
  }
}
