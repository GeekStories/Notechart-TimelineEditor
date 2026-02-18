using NAudio.Wave;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TimelineEditor.Models;

namespace TimelineEditor.Services {
  public class AudioService {
    private AudioTrack? _vocalTrack;
    public AudioTrack? VocalTrack {
      get => _vocalTrack;
      set {
        _vocalTrack = value;
        OnPropertyChanged();
      }
    }

    private AudioTrack? _rawTrack;
    public AudioTrack? RawTrack {
      get => _rawTrack;
      set {
        _rawTrack = value;
        OnPropertyChanged();
      }
    }

    public double CurrentTimeSeconds =>
    _vocalTrack != null
        ? (double)_vocalTrack._track.Position / _vocalTrack._track.WaveFormat.AverageBytesPerSecond
        : 0;

    public bool IsPlaying =>
        _vocalTrack?._output?.PlaybackState == PlaybackState.Playing;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public AudioTrack LoadAudio(string path, TrackType type) {
      AudioTrack track = new() {
        FilePath = path,
        Type = type
      };

      track.Load();

      if(type == TrackType.Vocal) {
        VocalTrack = track;
      } else {
        RawTrack = track;
      }

      return track;
    }
  }
}