using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using TimelineEditor.Models;
using TimelineEditor.Services;

namespace TimelineEditor.ViewModels {
  public class MainViewModel : INotifyPropertyChanged {
    private readonly AudioService _audioService = new();
    private readonly NotechartGenerator _generator = new();
    private readonly ProjectExportService _exportService = new();
    private readonly Stopwatch _visualClock = new();
    private bool _isGenerating = false;

    // Constants
    public const double PixelsPerSecond = 200;
    public const double ResizeHandleWidth = 6;
    public const double MinNoteDuration = 0.5;

    // Collections
    public ObservableCollection<ConfigItem> Configs { get; } = new();

    // Commands
    public ICommand ImportNotesCommand { get; }
    public ICommand BrowseAudioCommand { get; }
    public ICommand BrowseLyricsCommand { get; }
    public ICommand ClearProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand ExportProjectCommand { get; } // NEW
    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand LoadVocalsCommand { get; }
    public ICommand LoadRawCommand { get; }
    public ICommand GenerateCommand { get; }
    public ICommand RemoveNoteCommand { get; }
    public ICommand SplitNoteCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand SaveCurrentConfigCommand { get; }
    public ICommand SaveNewConfigCommand { get; }
    public ICommand DeleteCurrentConfigCommand { get; }

    // Properties
    private Timeline _timeline = new();
    public Timeline Timeline {
      get => _timeline;
      set {
        _timeline = value;
        OnPropertyChanged();
      }
    }

    private ConfigItem? _selectedConfig;
    public ConfigItem? CurrentConfig {
      get => _selectedConfig;
      set {
        if(_selectedConfig == value) return;
        _selectedConfig = value;
        OnPropertyChanged();
        if(value != null)
          LoadConfig(value);
      }
    }

    private string? _audioFileLabel;
    public string? AudioFileLabel {
      get => _audioFileLabel;
      set {
        _audioFileLabel = value;
        OnPropertyChanged();
      }
    }

    private string? _noteFileLabel = "Note File: (none)";
    public string? NoteFileLabel {
      get => _noteFileLabel;
      set {
        _noteFileLabel = value;
        OnPropertyChanged();
      }
    }

    private string? _lyricsFileLabel = "Lyrics File: (none)";
    public string? LyricsFileLabel {
      get => _lyricsFileLabel;
      set {
        _lyricsFileLabel = value;
        OnPropertyChanged();
      }
    }

    private string? _statusMessage;
    public string? StatusMessage {
      get => _statusMessage;
      set {
        _statusMessage = value;
        OnPropertyChanged();
        AppendStatus(value);
      }
    }

    private string? _statusLog = "";
    public string? StatusLog {
      get => _statusLog;
      set {
        _statusLog = value;
        OnPropertyChanged();
      }
    }

    private string _laneCount = "4";
    public string LaneCount {
      get => _laneCount;
      set {
        _laneCount = value;
        OnPropertyChanged();
      }
    }

    private double _visualTime;
    public double VisualTime {
      get => _visualTime;
      set {
        _visualTime = value;
        OnPropertyChanged();
      }
    }

    private double _anchorAudioTime;
    public double AnchorAudioTime {
      get => _anchorAudioTime;
      set {
        _anchorAudioTime = value;
        OnPropertyChanged();
      }
    }

    private string _minFrequency = "80";
    public string MinFrequency {
      get => _minFrequency;
      set {
        _minFrequency = value;
        OnPropertyChanged();
      }
    }

    private string _maxFrequency = "800";
    public string MaxFrequency {
      get => _maxFrequency;
      set {
        _maxFrequency = value;
        OnPropertyChanged();
      }
    }

    private string _lyricsPath = string.Empty;
    private string _audioPath = string.Empty;
    private string _rawAudioPath = string.Empty; // NEW: Track raw audio path

    // Computed Properties
    public double TimelineWidthSeconds =>
      _audioService.VocalTrack?.TotalTime.TotalSeconds ?? Timeline?.Length ?? 10;

    public double CurrentPlaybackTime =>
      _audioService.CurrentTimeSeconds;

    public bool IsPlaying =>
      _audioService.IsPlaying;

    public string PlayheadLabel {
      get {
        TimeSpan time = TimeSpan.FromSeconds(VisualTime);
        return time.ToString(@"mm\:ss\.ff");
      }
    }

    public Stopwatch VisualClock => _visualClock;

    // Events
    public event Action? DrawTimelineRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel() {
      // Initialize Commands
      ImportNotesCommand = new RelayCommand(_ => ImportNotes());
      BrowseAudioCommand = new RelayCommand(_ => BrowseAudio());
      BrowseLyricsCommand = new RelayCommand(_ => BrowseLyrics());
      ClearProjectCommand = new RelayCommand(_ => ClearProject());
      SaveProjectCommand = new RelayCommand(_ => SaveProject());
      ExportProjectCommand = new RelayCommand(_ => ExportProject()); // NEW
      PlayCommand = new RelayCommand(_ => Play());
      StopCommand = new RelayCommand(_ => Stop());
      ResetCommand = new RelayCommand(_ => Reset());
      LoadVocalsCommand = new RelayCommand(_ => LoadVocals());
      LoadRawCommand = new RelayCommand(_ => LoadRaw());
      GenerateCommand = new RelayCommand(_ => GenerateNotes(), _ => _audioService.VocalTrack != null && !_isGenerating);
      RemoveNoteCommand = new RelayCommand(param => RemoveNote((Note)param!));
      SplitNoteCommand = new RelayCommand(param => {
        var args = (Tuple<Note, double>)param!;
        SplitNote(args.Item1, args.Item2);
      });
      AddNoteCommand = new RelayCommand(param => {
        var args = (Tuple<int, double>)param!;
        AddNote(args.Item1, args.Item2);
      });
      SaveCurrentConfigCommand = new RelayCommand(_ => SaveCurrentConfig());
      SaveNewConfigCommand = new RelayCommand(_ => SaveNewConfig());
      DeleteCurrentConfigCommand = new RelayCommand(_ => DeleteCurrentConfig(), _ => CurrentConfig != null);

      LoadConfigs();
      ConfigWatcherService.ConfigsChanged += LoadConfigs;
    }

    #region Audio Methods
    private void LoadVocals() {
      var dlg = new OpenFileDialog() {
        Filter = "Audio Files (*.wav;)|*.wav;"
      };

      if(dlg.ShowDialog() != true)
        return;

      _audioPath = dlg.FileName;
      _audioService.LoadAudio(dlg.FileName, TrackType.Vocal);

      AudioFileLabel = $"Audio File: {Path.GetFileName(_audioService.VocalTrack.FilePath)} ({_audioService.VocalTrack.TotalTime.Minutes}m {_audioService.VocalTrack.TotalTime.Seconds}s)";
      StatusMessage = $"Loaded Audio: {Path.GetFileName(_audioService.VocalTrack.FilePath)}";

      DrawTimelineRequested?.Invoke();
    }

    private void LoadRaw() {
      var dlg = new OpenFileDialog() {
        Filter = "Audio Files (*.wav;)|*.wav;"
      };

      if(dlg.ShowDialog() != true)
        return;

      _rawAudioPath = dlg.FileName; // Store raw audio path
      _audioService.LoadAudio(dlg.FileName, TrackType.Raw);
      StatusMessage = $"Loaded Raw Audio: {Path.GetFileName(_audioService.RawTrack.FilePath)}";
    }

    public void Play() {
      if(_audioService.VocalTrack == null) {
        StatusMessage = "No audio loaded.";
        return;
      }

      _audioService.VocalTrack._output?.Play();
      AnchorAudioTime = CurrentPlaybackTime;
      _visualClock.Restart();
    }

    public void Stop() {
      _audioService.VocalTrack?._output?.Pause();
    }

    public void Reset() {
      if(_audioService.VocalTrack?._track == null) return;

      _audioService.VocalTrack._track.Position = 0;
      _audioService.VocalTrack._track.CurrentTime = TimeSpan.Zero;
      AnchorAudioTime = 0;
      VisualTime = 0;
      _visualClock.Stop();
    }

    public void SeekTo(double timeSeconds) {
      if(_audioService.VocalTrack?._track == null) return;

      _audioService.VocalTrack._track.Position =
        (long)(timeSeconds * _audioService.VocalTrack._track.WaveFormat.AverageBytesPerSecond);

      AnchorAudioTime = timeSeconds;
      VisualTime = timeSeconds;
      _visualClock.Restart();
    }

    public void UpdateVisualTime() {
      double realTime = CurrentPlaybackTime;

      if(IsPlaying) {
        double interpolated = AnchorAudioTime + _visualClock.Elapsed.TotalSeconds;

        if(realTime > AnchorAudioTime) {
          AnchorAudioTime = realTime;
          _visualClock.Restart();
          interpolated = realTime;
        }

        VisualTime = interpolated;
      } else {
        VisualTime = realTime;
      }
    }
    #endregion

    #region Timeline Methods
    private void ImportNotes() {
      OpenFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        LoadTimelineFromFile(dlg.FileName);
      }
    }

    private void LoadTimelineFromFile(string path) {
      try {
        string json = File.ReadAllText(path);
        Timeline? loaded = JsonSerializer.Deserialize<Timeline>(json);

        if(loaded == null) {
          MessageBox.Show("Failed to load timeline from the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        NoteFileLabel = $"Note File: {Path.GetFileName(path)}";
        Timeline = loaded;
        LaneCount = $"{Timeline.Lanes}";

        StatusMessage = $"Imported Notes: {Path.GetFileName(path)}";
        DrawTimelineRequested?.Invoke();
      } catch(Exception ex) {
        Debug.WriteLine(ex);
        StatusMessage = "Failed to load timeline from selected file";
      }
    }

    private void BrowseLyrics() {
      if(_audioService.VocalTrack == null) {
        MessageBox.Show("Please load an audio file before importing lyrics.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      OpenFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        _lyricsPath = dlg.FileName;
        LyricsFileLabel = $"Lyrics File: {Path.GetFileName(_lyricsPath)}";

        Timeline.Lyrics = LoadLyrics(_lyricsPath);
        DrawTimelineRequested?.Invoke();
      }
    }

    private List<Lyric> LoadLyrics(string path) {
      try {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<Lyric>? phrases = JsonSerializer.Deserialize<List<Lyric>>(json, options);
        
        if(phrases == null) {
          MessageBox.Show("Failed to load lyrics from the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return new List<Lyric>();
        }
        
        StatusMessage = $"Loaded {phrases.Count} lyric phrases.";
        return phrases;
      } catch(Exception ex) {
        Debug.WriteLine(ex);
        StatusMessage = "Failed to load lyrics from selected file";
        return new List<Lyric>();
      }
    }

    private void BrowseAudio() {
      LoadVocals();
    }

    private void ClearProject() {
      Stop();
      Timeline = new();
      NoteFileLabel = "Note File: (none)";
      DrawTimelineRequested?.Invoke();
    }

    private void SaveProject() {
      if(Timeline.Notes.Count == 0) {
        MessageBox.Show("No notes to save.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      SaveFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        string json = JsonSerializer.Serialize(Timeline, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, json);
        StatusMessage = $"Saved timeline to: {Path.GetFileName(dlg.FileName)}";
      }
    }

    private async void ExportProject() {
      // Validate export prerequisites
      if(!_exportService.IsFFmpegAvailable()) {
        MessageBox.Show(
          "FFmpeg is not installed or not found.\n\n" +
          "Please install FFmpeg and make sure it's in your system PATH, or place ffmpeg.exe in the application directory.\n\n" +
          "Download from: https://ffmpeg.org/download.html",
          "FFmpeg Required",
          MessageBoxButton.OK,
          MessageBoxImage.Warning
        );
        return;
      }

      if(Timeline.Notes.Count == 0) {
        MessageBox.Show("No notes to export. Please generate or import a notechart first.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      if(string.IsNullOrEmpty(_rawAudioPath) || !File.Exists(_rawAudioPath)) {
        MessageBox.Show("Raw audio file not found. Please load a raw audio file first.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      // Prompt for save location
      SaveFileDialog dlg = new() {
        Filter = "Rhythm Game Project (*.rgp)|*.rgp",
        FileName = string.IsNullOrEmpty(Timeline.Name) ? "project.rgp" : $"{Timeline.Name}.rgp",
        DefaultExt = ".rgp"
      };

      if(dlg.ShowDialog() != true)
        return;

      try {
        Stop(); // Stop playback during export

        var progress = new Progress<string>(msg => StatusMessage = msg);
        
        StatusMessage = "Starting project export...";
        
        var result = await _exportService.ExportProjectAsync(
          Timeline,
          _rawAudioPath,
          dlg.FileName,
          progress
        );

        if(result.Success) {
          MessageBox.Show(
            $"Project exported successfully!\n\nLocation: {result.OutputPath}",
            "Export Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information
          );
          StatusMessage = $"Export completed: {Path.GetFileName(result.OutputPath)}";
        } else {
          MessageBox.Show(
            $"Export failed:\n\n{result.Error}",
            "Export Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error
          );
          StatusMessage = $"Export failed: {result.Error}";
        }
      } catch(Exception ex) {
        MessageBox.Show(
          $"Unexpected error during export:\n\n{ex.Message}",
          "Export Error",
          MessageBoxButton.OK,
          MessageBoxImage.Error
        );
        StatusMessage = $"Export error: {ex.Message}";
      }
    }
    #endregion

    #region Note Manipulation
    public void AddNote(int lane, double time) {
      if(lane < 0 || lane >= Timeline.Lanes) return;
      if(time < 0 || time > TimelineWidthSeconds) return;

      Note newNote = new() {
        Lane = lane,
        Start = time,
        Duration = 0.5
      };

      bool placed = TrySnapNoteToTimeline(newNote);
      if(!placed) {
        StatusMessage = "Cannot place note: no free space.";
      } else {
        Timeline.Notes.Add(newNote);
        DrawTimelineRequested?.Invoke();
      }
    }

    public void RemoveNote(Note note) {
      Timeline.Notes.Remove(note);
      StatusMessage = $"Removed note at {note.Start:F2}s, lane {note.Lane}";
      DrawTimelineRequested?.Invoke();
    }

    public void SplitNote(Note note, double splitTime) {
      double leftDuration = splitTime - note.Start;
      double rightDuration = note.Duration - leftDuration;

      var leftNote = new Note {
        Start = note.Start,
        Duration = leftDuration,
        Lane = note.Lane,
        Type = note.Type
      };

      var rightNote = new Note {
        Start = splitTime,
        Duration = rightDuration,
        Lane = note.Lane,
        Type = note.Type
      };

      Timeline.Notes.Remove(note);
      Timeline.Notes.Add(leftNote);
      Timeline.Notes.Add(rightNote);

      Timeline.Notes = Timeline.Notes.OrderBy(n => n.Start).ToList();
      DrawTimelineRequested?.Invoke();
    }

    public bool TrySnapNoteToTimeline(Note anchor) {
      var overlaps = GetOverlappingNotes(anchor);
      if(overlaps.Count == 0)
        return true;

      foreach(var n in overlaps) {
        double pushRight = anchor.Start + anchor.Duration;
        if(pushRight + n.Duration <= TimelineWidthSeconds &&
           !IsOverlapping(n, pushRight, n.Duration)) {
          n.Start = pushRight;
          continue;
        }

        double pushLeft = anchor.Start - n.Duration;
        if(pushLeft >= 0 &&
           !IsOverlapping(n, pushLeft, n.Duration)) {
          n.Start = pushLeft;
          continue;
        }

        return false;
      }

      Timeline.Notes = Timeline.Notes.OrderBy(n => n.Start).ToList();
      return true;
    }

    private bool IsOverlapping(Note note, double start, double duration) {
      return Timeline.Notes.Any(n =>
        n != note &&
        n.Lane == note.Lane &&
        n.Start < start + duration &&
        n.Start + n.Duration > start
      );
    }

    private List<Note> GetOverlappingNotes(Note anchor) {
      return Timeline.Notes
        .Where(n =>
          n != anchor &&
          n.Lane == anchor.Lane &&
          n.Start < anchor.Start + anchor.Duration &&
          n.Start + n.Duration > anchor.Start
        )
        .OrderBy(n => n.Start)
        .ToList();
    }
    #endregion

    #region Generate / Run Python
    private async void GenerateNotes() {
      if(CurrentConfig == null) return;

      _isGenerating = true;
      CommandManager.InvalidateRequerySuggested();

      Stop();

      var progress = new Progress<string>(msg => StatusMessage = msg);
      var result = await _generator.GenerateAsync(_audioPath, CurrentConfig.Settings, progress);

      if(!result.Success) {
        StatusMessage = "Chart generation failed.";
        StatusMessage = result.Error ?? "Unknown error";
        _isGenerating = false;
        CommandManager.InvalidateRequerySuggested();
        return;
      }

      LoadTimelineFromFile(result.OutputPath!);
      _isGenerating = false;
      CommandManager.InvalidateRequerySuggested();
    }
    #endregion

    #region Config Handler
    public void LoadConfigs() {
      StatusMessage = $"Loading configurations: {ConfigFolder}";
      Configs.Clear();

      if(string.IsNullOrWhiteSpace(ConfigFolder) || !Directory.Exists(ConfigFolder))
        return;

      foreach(var path in Directory.GetFiles(ConfigFolder, "*.json")) {
        try {
          string json = File.ReadAllText(path);
          var settings = JsonSerializer.Deserialize<GeneratorSettings>(json);
          if(settings == null) continue;

          Configs.Add(new ConfigItem {
            Profile = settings.Profile,
            Path = path,
            Settings = settings
          });
        } catch(Exception ex) {
          StatusMessage = "Error loading config: " + ex.Message;
        }
      }

      string lastFile = TimelineEditor.Properties.Settings.Default.LastConfigFile;
      CurrentConfig = Configs.FirstOrDefault(c => c.Profile == lastFile)
                     ?? Configs.FirstOrDefault();
    }

    private void LoadConfig(ConfigItem profile) {
      TimelineEditor.Properties.Settings.Default.LastConfigFile = profile.Profile;
      TimelineEditor.Properties.Settings.Default.Save();
      StatusMessage = $"Loaded configuration: {profile.Profile}.json";
    }

    private void SaveCurrentConfig() {
      if(CurrentConfig == null) return;
      
      try {
        string json = JsonSerializer.Serialize(CurrentConfig.Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CurrentConfig.Path, json);
        StatusMessage = $"Saved configuration: {CurrentConfig.Profile}";
      } catch(Exception ex) {
        StatusMessage = $"Error saving config: {ex.Message}";
      }
    }

    private void SaveNewConfig() {
      if(CurrentConfig == null) return;
      
      var dlg = new SaveFileDialog {
        Filter = "JSON Files (*.json)|*.json",
        InitialDirectory = ConfigFolder,
        FileName = "new_config.json"
      };
      
      if(dlg.ShowDialog() == true) {
        try {
          string json = JsonSerializer.Serialize(CurrentConfig.Settings, new JsonSerializerOptions { WriteIndented = true });
          File.WriteAllText(dlg.FileName, json);
          StatusMessage = $"Saved new configuration: {Path.GetFileName(dlg.FileName)}";
          LoadConfigs();
        } catch(Exception ex) {
          StatusMessage = $"Error saving config: {ex.Message}";
        }
      }
    }

    private void DeleteCurrentConfig() {
      if(CurrentConfig == null) return;
      
      var result = MessageBox.Show(
        $"Are you sure you want to delete '{CurrentConfig.Profile}'?",
        "Confirm Delete",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning
      );
      
      if(result == MessageBoxResult.Yes) {
        try {
          File.Delete(CurrentConfig.Path);
          StatusMessage = $"Deleted configuration: {CurrentConfig.Profile}";
          LoadConfigs();
        } catch(Exception ex) {
          StatusMessage = $"Error deleting config: {ex.Message}";
        }
      }
    }

    private static string ConfigFolder => TimelineEditor.Properties.Settings.Default.ConfigFolder;
    #endregion

    #region Helpers
    private void AppendStatus(string? message) {
      if(string.IsNullOrEmpty(message)) return;
      StatusLog += $"[{DateTime.Now:t}]: {message}\n";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
  }
}
