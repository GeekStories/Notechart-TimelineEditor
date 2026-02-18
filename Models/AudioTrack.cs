using NAudio.Wave;

namespace TimelineEditor.Models
{
  public enum TrackType {
    Vocal,
    Raw
  }

  public class AudioTrack {
    public AudioFileReader? _track;
    public WaveOutEvent? _output;

    public string FilePath { get; set; } = string.Empty;
    public TimeSpan TotalTime => _track?.TotalTime ?? TimeSpan.Zero;
    public string? FileName => _track?.FileName;
    public TrackType Type { get; set; }

    public void Load() {
      if (string.IsNullOrEmpty(FilePath))
        throw new InvalidOperationException("FilePath cannot be null or empty.");

      _track = new AudioFileReader(FilePath);
      _output = new WaveOutEvent();
      _output.Init(_track);
      _output.DesiredLatency = 50;
    }

    public void Play() { }
    public void Stop() { }
    public void Reset() { }

    public void Dispose() {
      _output?.Dispose();
      _track?.Dispose();
    }
  }
}
