using System.Text.Json.Serialization;

namespace TimelineEditor
{
  public class GeneratorSettings {
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "";

    // Analysis
    public int WindowSize { get; set; } = 2048;
    public int HopSize { get; set; } = 512;
    public double MinFreq { get; set; } = 70.0;
    public double MaxFreq { get; set; } = 1100.0;

    // Stability
    public int SmoothFrames { get; set; } = 5;
    public int StabilityFrames { get; set; } = 6;
    public double HoldTolerance { get; set; } = 0.75;

    // Notes
    public double MinNoteDuration { get; set; } = 0.12;
    public double MergeGap { get; set; } = 0.05;
    public double NoteMergeTolerance { get; set; } = 0.5;

    // Phrases
    public double PhraseGap { get; set; } = 0.45;
    public double PhrasePitchTolerance { get; set; } = 1.75;
    public double StretchFactor { get; set; } = 1.25;

    // Final
    public double FinalMergeGap { get; set; } = 0.15;

    // Lanes
    public int LaneRange { get; set; } = 4;
  }

}
