using System.Text.Json.Serialization;

namespace TimelineEditor.Models
{
  public class Timeline {
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("length")]
    public double Length { get; set; }

    [JsonPropertyName("reference_pitch")]
    public float ReferencePitch { get; set; }

    [JsonPropertyName("mid_lane")]
    public float midLane { get; set; }

    [JsonPropertyName("lanes")]
    public int Lanes { get; set; }

    [JsonPropertyName("notes")]
    public List<Note> Notes { get; set; } = new();

    [JsonPropertyName("pitches")]
    public List<PitchSample> PitchSamples { get; set; } = new();
    [JsonPropertyName("lyrics")]
    public List<Lyric> Lyrics { get; set; } = new();
  }
}
