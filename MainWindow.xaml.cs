using Microsoft.Win32;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TimelineEditor {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    private string audioPath;
    private string jsonPath;
    private Timeline timeline;
    private CancellationTokenSource? pitchCts;

    private SettingsWindow SettingsWindow = new();

    // For minimap interaction
    private Rectangle? minimapViewport;
    private bool isDraggingMinimap = false;

    private WaveOutEvent? output;
    private AudioFileReader? audio;
    private DispatcherTimer? playheadTimer;

    private Line? playheadLine;

    private Dictionary<string, GeneratorSettings> configs;
    private GeneratorSettings CurrentConfig => configs.TryGetValue(Properties.Settings.Default.LastConfigFile, out var cfg) ? cfg : new GeneratorSettings();

    string configFolder => Properties.Settings.Default.ConfigFolder;

    public MainWindow() {
      InitializeComponent();

      audioPath = string.Empty;
      jsonPath = string.Empty;
      timeline = new();

      output = null;
      audio = null;

      playheadLine = null;

      configs = new();

      // Force update when canvas size changes
      MinimapCanvas.SizeChanged += (_, _) => DrawMinimap();

      LoadConfigs();
      ConfigWatcherService.ConfigsChanged += LoadConfigs;
    }

    protected override void OnClosed(EventArgs e) {
      ConfigWatcherService.ConfigsChanged -= LoadConfigs;
      Application.Current.Shutdown();
      base.OnClosed(e);
    }

    private void Import_Click(object sender, RoutedEventArgs e) => ImportNotes();
    private void Browse_Click(object sender, RoutedEventArgs e) => BrowseAudio();
    private void ClearProject_Click(object sender, RoutedEventArgs e) => ClearTimeline();
    private void GenerateNotes_Click(object sender, RoutedEventArgs e) => GenerateNotes();
    private void Play_Click(object sender, RoutedEventArgs e) => Play();
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void Reset_Click(object sender, RoutedEventArgs e) => Reset();
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    private void SaveGeneratorSettings_Click(object sender, RoutedEventArgs e) => SaveCurrentConfig();
    private void SaveNewGeneratorSettings_Click(object sender, RoutedEventArgs e) => SaveNewConfig();
    private void ConfigSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectConfig();
    
    private void LoadAudio(string path) {
      SetupTimelineForAudio();

      audio = new AudioFileReader(path);
      output = new WaveOutEvent();
      output.Init(audio);

      GenerateButton.IsEnabled = true;
      UpdateStatusBox($"Loaded Audio: {System.IO.Path.GetFileName(audio.FileName)}");
    }

    #region Config Handler
    public void LoadConfigs() {
      UpdateStatusBox($"Loading configurations: {configFolder}");

      ConfigSelector.ItemsSource = null;

      if(string.IsNullOrWhiteSpace(configFolder) || !Directory.Exists(configFolder))
        return;

      List<ComboBoxItem> items = new();
      foreach(string path in Directory.GetFiles(configFolder, "*.json")) {
        try {
          string? json = File.ReadAllText(path);
          if(json == null) {
            Debug.WriteLine("Failed to read JSON");
            continue;
          }

          GeneratorSettings? config = JsonSerializer.Deserialize<GeneratorSettings>(json);
          if(config == null) {
            Debug.WriteLine("Failed to deserialize JSON");
            continue;
          }

          Debug.WriteLine(config.Profile);

          ComboBoxItem item = new() {
            Content = config.Profile,
            Tag = path,
          };

          items.Add(item);
          configs[config.Profile] = config;
          UpdateStatusBox("Loaded Profile: " + config.Profile);

        } catch(Exception ex) {
          UpdateStatusBox("Error loading config: " + ex.Message);
          Debug.WriteLine(ex);
        }
      }

      ConfigSelector.ItemsSource = items;

      string lastFile = Properties.Settings.Default.LastConfigFile;

      if(lastFile != "") {
        UpdateStatusBox($"Found last used config: {lastFile}.json");
        LoadConfig(Properties.Settings.Default.LastConfigFile);
        ConfigSelector.SelectedIndex = items.FindIndex(item => (string)item.Content == lastFile);
      } else if(items?.Count == 1) {
        ConfigSelector.SelectedIndex = 0;
      }
    }
    private void LoadConfig(string profile) {
      if(configs.TryGetValue(profile, out var config)) {
        MinFrequencyBox.Text = config.MinFreq.ToString();
        MaxFrequencyBox.Text = config.MaxFreq.ToString();
        WindowSizeBox.Text = config.WindowSize.ToString();
        HopSizeBox.Text = config.HopSize.ToString();
        SmoothFramesBox.Text = config.SmoothFrames.ToString();
        StabilityFramesBox.Text = config.StabilityFrames.ToString();
        HoldToleranceBox.Text = config.HoldTolerance.ToString();
        MergeGapBox.Text = config.MergeGap.ToString();
        MinNoteDurationBox.Text = config.MinNoteDuration.ToString();
        NoteMergeToleranceBox.Text = config.NoteMergeTolerance.ToString();
        PhraseGapBox.Text = config.PhraseGap.ToString();
        PhrasePitchToleranceBox.Text = config.PhrasePitchTolerance.ToString();
        StretchFactorBox.Text = config.StretchFactor.ToString();
        FinalMergeGapBox.Text = config.FinalMergeGap.ToString();

        Properties.Settings.Default.LastConfigFile = config.Profile;
        Properties.Settings.Default.Save();

        UpdateStatusBox($"Loaded configuration: {config.Profile}.json");
      } else {
        MessageBox.Show("Failed to load configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
    private void SaveConfig(string profile) {
      GeneratorSettings config = new() {
        Profile = profile,
        MinFreq = double.Parse(MinFrequencyBox.Text),
        MaxFreq = double.Parse(MaxFrequencyBox.Text),
        WindowSize = int.Parse(WindowSizeBox.Text),
        HopSize = int.Parse(HopSizeBox.Text),
        SmoothFrames = int.Parse(SmoothFramesBox.Text),
        StabilityFrames = int.Parse(StabilityFramesBox.Text),
        HoldTolerance = double.Parse(HoldToleranceBox.Text),
        MergeGap = double.Parse(MergeGapBox.Text),
        MinNoteDuration = double.Parse(MinNoteDurationBox.Text),
        NoteMergeTolerance = double.Parse(NoteMergeToleranceBox.Text),
        PhraseGap = double.Parse(PhraseGapBox.Text),
        PhrasePitchTolerance = double.Parse(PhrasePitchToleranceBox.Text),
        StretchFactor = double.Parse(StretchFactorBox.Text),
        FinalMergeGap = double.Parse(FinalMergeGapBox.Text)
      };

      try {
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        if(string.IsNullOrWhiteSpace(configFolder) || !Directory.Exists(configFolder)) {
          MessageBox.Show("Config folder is not set or does not exist. Please set it in the settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        string filePath = System.IO.Path.Combine(configFolder, $"{config.Profile}.json");
        File.WriteAllText(filePath, json);
        UpdateStatusBox($"Configuration updated: {config.Profile}.json");
        LoadConfigs(); // Refresh the list
      } catch(Exception ex) {
        MessageBox.Show($"Error updating configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
    private void SaveCurrentConfig() {
      SaveConfig(CurrentConfig.Profile);
    }
    private void SaveNewConfig() {
      // Prompt for name
      string name = Microsoft.VisualBasic.Interaction.InputBox(
        "Enter a name for the current configuration:",
        "Save Configuration",
        $"new_config"
      );

      if(string.IsNullOrWhiteSpace(name)) {
        MessageBox.Show("Configuration name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      // Check if name already exists
      if(configs.ContainsKey(name)) {
        var result = MessageBox.Show(
          "A configuration with this name already exists. Do you want to overwrite it?",
          "Confirm Overwrite",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning
        );
        if(result != MessageBoxResult.Yes) {
          return;
        }
      }

      SaveConfig(name);
    }
    #endregion

    #region ButtonHandlers
    private void ImportNotes() {
      OpenFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        string json = File.ReadAllText(dlg.FileName);
        Timeline? loaded = JsonSerializer.Deserialize<Timeline>(json);
        if(loaded == null) {
          MessageBox.Show("Failed to load timeline from the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        timeline = loaded;
        UpdateStatusBox($"Imported Notes: {System.IO.Path.GetFileName(dlg.FileName)}");
        DrawTimelineAndMinimap();

        GenerateButton.IsEnabled = true;
      }
    }
    private void BrowseAudio() {
      OpenFileDialog dlg = new() {
        Filter = "Audio Files (*.wav;)|*.wav;"
      };

      if(dlg.ShowDialog() == true) {
        audioPath = dlg.FileName;
        LoadAudio(audioPath);
      }
    }
    private void ClearTimeline() {
      Stop();

      pitchCts?.Cancel();
      pitchCts = null;

      timeline = new() {
        Length = 0,
      };

      PlayheadCanvas.Children.Clear();
      MinimapCanvas.Children.Clear();
      NotesCanvas.Children.Clear();
      PitchCanvas.Children.Clear();

      minimapViewport = null;

      GenerateButton.IsEnabled = true;

      UpdatePlayhead(0);
      ClearStatusBox();
    }
    private void OpenSettingsWindow() {
      if(SettingsWindow.IsVisible) {
        SettingsWindow.Focus();
      } else {
        SettingsWindow = new SettingsWindow();
        SettingsWindow.Show();
      }
    }
    private void SelectConfig() {
      if(ConfigSelector.SelectedItem is ComboBoxItem item && item.Content is string v) {
        LoadConfig(v);
      }
    }
    #endregion

    #region Generate / Run Python
    private async void GenerateNotes() {
      if(string.IsNullOrEmpty(audioPath)) return;

      Stop(); // Kill the audio if playing


      GenerateButton.IsEnabled = false;
      UpdateStatusBox("Generating chart...");

      var settings = new GeneratorSettings {
        MinFreq = double.Parse(MinFrequencyBox.Text),
        MaxFreq = double.Parse(MaxFrequencyBox.Text),
        WindowSize = int.Parse(WindowSizeBox.Text),
        HopSize = int.Parse(HopSizeBox.Text),

        SmoothFrames = int.Parse(SmoothFramesBox.Text),
        StabilityFrames = int.Parse(StabilityFramesBox.Text),
        HoldTolerance = double.Parse(HoldToleranceBox.Text),

        MergeGap = double.Parse(MergeGapBox.Text),
        MinNoteDuration = double.Parse(MinNoteDurationBox.Text),
        NoteMergeTolerance = double.Parse(NoteMergeToleranceBox.Text),

        PhraseGap = double.Parse(PhraseGapBox.Text),
        PhrasePitchTolerance = double.Parse(PhrasePitchToleranceBox.Text),
        StretchFactor = double.Parse(StretchFactorBox.Text),

        FinalMergeGap = double.Parse(FinalMergeGapBox.Text),
      };

      bool success = await Task.Run(
        () => RunPythonGenerator(audioPath, settings)
      );

      if(success) {
        UpdateStatusBox($"Created Notes File: {System.IO.Path.GetFileName(jsonPath)}");

        string jsonText = File.ReadAllText(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        Timeline? loaded = JsonSerializer.Deserialize<Timeline>(jsonText, options);
        if(loaded == null) {
          MessageBox.Show("Failed to load timeline.", "Error");
          return;
        }

        timeline = loaded;
        UpdateStatusBox($"{timeline.Notes.Count} notes loaded onto Timeline.");

        DrawTimelineAndMinimap();
      } else {
        UpdateStatusBox("Chart generation failed.");
      }
    }
    private bool RunPythonGenerator(string audioFile, GeneratorSettings settings) {
      if(string.IsNullOrEmpty(audioFile)) return false;

      try {
        string arguments = BuildGeneratorArguments(audioFile, settings);

        ProcessStartInfo psi = new() {
          FileName = "notechart",
          Arguments = arguments,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        UpdateStatusBox("Starting pitch extraction.");

        using var process = new Process { StartInfo = psi };
        process.Start();

        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if(process.ExitCode != 0) {
          UpdateStatusBox("Generator error:");
          UpdateStatusBox(stderr);
          return false;
        }

        UpdateStatusBox("Pitch extraction finished.");

        jsonPath = System.IO.Path.Combine(
          System.IO.Path.GetDirectoryName(audioFile)!,
          System.IO.Path.GetFileNameWithoutExtension(audioFile) + "_chart.json"
        );

        return File.Exists(jsonPath);
      } catch(Exception ex) {
        UpdateStatusBox($"Error: {ex.Message}");
        return false;
      }
    }
    #endregion

    #region Playhead
    private void UpdatePlayheadHeight() {
      if(playheadLine == null) return;

      playheadLine.Y1 = 0;
      playheadLine.Y2 = PlayheadCanvas.ActualHeight;
    }
    private void CreatePlayHead() {
      PlayheadCanvas.Children.Clear();

      var playheadLine = new Line {
        Stroke = Brushes.Red,
        StrokeThickness = 2,
        Y1 = 0,
        Y2 = PlayheadCanvas.ActualHeight,
        X1 = 0,
        X2 = 0,
        Tag = "playhead",
        IsHitTestVisible = false
      };

      PlayheadCanvas.SizeChanged += (_, _) => UpdatePlayheadHeight();
      PlayheadCanvas.Children.Add(playheadLine);
    }
    private void UpdatePlayhead(double timeSeconds) {
      double pixelsPerSecond = 100;
      double x = timeSeconds * pixelsPerSecond;

      if(PlayheadCanvas.Children.Count == 0) return;
      if(PlayheadCanvas.Children[0] is Line line) {
        line.X1 = line.X2 = x;
      }

      //AutoScrollTimeline(x);
    }
    private void AutoScrollTimeline(double playheadX) {
      double left = TimelineScrollViewer.HorizontalOffset;
      double right = left + TimelineScrollViewer.ViewportWidth;

      if(playheadX < left || playheadX > right - 50) {
        TimelineScrollViewer.ScrollToHorizontalOffset(
          playheadX - TimelineScrollViewer.ViewportWidth / 3
        );
      }
    }
    private void Play() {
      if(audio == null || output == null || playheadTimer == null) {
        UpdateStatusBox("No audio loaded..");
        return;
      }

      output.Play();
      playheadTimer.Start();
    }
    private void Stop() {
      if(output == null || playheadTimer == null) return;

      output.Pause();
      playheadTimer.Stop();
    }
    private void Reset() {
      if(audio == null) return;

      audio.Position = 0;
      UpdatePlayhead(0);
    }
    #endregion

    #region Timeline/Minimap Rendering
    private void SetupTimelineForAudio() {
      double timelineLengthSeconds = Math.Max(timeline.Length, timeline.PitchSamples.LastOrDefault()?.Time ?? 0);
      if(timelineLengthSeconds <= 0) timelineLengthSeconds = 10;

      double pixelsPerSecond = 100;
      double width = timelineLengthSeconds * pixelsPerSecond;
      double height = TimelineScrollViewer.ActualHeight;

      PitchCanvas.Width = NotesCanvas.Width = PlayheadCanvas.Width = width;
      PitchCanvas.Height = NotesCanvas.Height = PlayheadCanvas.Height = height;

      playheadTimer = new DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
      };
      playheadTimer.Tick += (s, e) => {
        if(audio == null) return;
        UpdatePlayhead(audio.CurrentTime.TotalSeconds);
      };

      // Clear previous drawings
      PitchCanvas.Children.Clear();
      NotesCanvas.Children.Clear();
    }
    private void InitMinimapViewport() {
      minimapViewport = new Rectangle {
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
        IsHitTestVisible = false,
        Height = MinimapCanvas.ActualHeight // may be 0 if called too early
      };

      MinimapCanvas.Children.Add(minimapViewport);
    }
    private void UpdateMinimapViewport() {
      if(NotesCanvas.Width <= 0 || MinimapCanvas.ActualWidth <= 0 || minimapViewport == null) return;

      double viewportRatio = TimelineScrollViewer.ViewportWidth / NotesCanvas.Width;
      double offsetRatio = TimelineScrollViewer.HorizontalOffset / NotesCanvas.Width;

      double viewportWidth = MinimapCanvas.ActualWidth * viewportRatio;
      double viewportX = MinimapCanvas.ActualWidth * offsetRatio;

      minimapViewport.Width = viewportWidth;
      Canvas.SetLeft(minimapViewport, viewportX);
      Canvas.SetTop(minimapViewport, 0);
    }
    private void DrawNotes() {
      double pixelsPerSecond = 100;
      if(timeline.Notes.Count == 0) return;

      double laneHeight = NotesCanvas.Height / Math.Max(1, timeline.Lanes);

      foreach(var note in timeline.Notes) {
        Rectangle rect = new Rectangle {
          Width = Math.Max(1, note.Duration * pixelsPerSecond),
          Height = laneHeight - 1,
          Fill = Brushes.Cyan,
          Tag = "note",
          IsHitTestVisible = false
        };

        Canvas.SetLeft(rect, note.Start * pixelsPerSecond);
        Canvas.SetTop(rect, (timeline.Lanes / 2 - note.Lane) * laneHeight);

        NotesCanvas.Children.Add(rect);
      }
    }
    private void DrawPitchGraph() {
      if(timeline.PitchSamples.Count == 0) return;
      PitchCanvas.Children.Clear();


      const double MaxTimeGap = 0.03;   // 30 ms
      const double MaxMidiJump = 0.75;  // semitones

      double pixelsPerSecond = 100;
      float viewStart = (float)(TimelineScrollViewer.HorizontalOffset / pixelsPerSecond);
      float viewEnd = (float)((TimelineScrollViewer.HorizontalOffset + TimelineScrollViewer.ViewportWidth) / pixelsPerSecond);

      PitchSample? prev = null;

      foreach(var p in timeline.PitchSamples) {
        if(p.Time < viewStart || p.Time > viewEnd || p.Midi <= 0)
          continue;

        if(prev != null) {
          double dt = p.Time - prev.Time;
          double dm = Math.Abs(p.Midi - prev.Midi);

          if(dt <= MaxTimeGap && dm <= MaxMidiJump) {
            DrawLine(
                TimeToX(prev.Time),
                MidiToY(prev.Midi),
                TimeToX(p.Time),
                MidiToY(p.Midi)
            );
          }
        }

        prev = p;
      }
    }
    private void DrawLine(double x1, double y1, double x2, double y2) {
      Line line = new() {
        X1 = x1,
        Y1 = PitchCanvas.Height,
        X2 = x2,
        Y2 = y2,
        Stroke = Brushes.Orange,
        StrokeThickness = 2,
        Opacity = 0.7,
        Tag = "pitch",
        IsHitTestVisible = false
      };
      PitchCanvas.Children.Add(line);
    }
    private static double TimeToX(double timeSeconds) {
      double pixelsPerSecond = 100;
      return timeSeconds * pixelsPerSecond;
    }
    private double MidiToY(double midi) {
      double minMidi = FrequencyToMidi(double.Parse(MinFrequencyBox.Text));
      double maxMidi = FrequencyToMidi(double.Parse(MaxFrequencyBox.Text));
      double pitchHeightFraction = 0.5;
      double pitchHeight = PitchCanvas.Height * pitchHeightFraction;
      double topOffset = PitchCanvas.Height * (1 - pitchHeightFraction);
      double normalized = (midi - minMidi) / (maxMidi - minMidi);
      normalized = Math.Clamp(normalized, 0, 1);
      return topOffset + (1 - normalized) * pitchHeight;
    }
    private static double FrequencyToMidi(double frequency) {
      return 69 + 12 * Math.Log2(frequency / 440.0);
    }

    private void DrawTimelineAndMinimap() {
      SetupTimelineForAudio();  // sets sizes, clears pitch/notes once

      DrawPitchGraph();
      UpdateStatusBox($"Loaded {timeline.PitchSamples.Count} pitch samples.");

      DrawNotes();
      CreatePlayHead();

      DrawMinimap();
    }
    private void DrawMinimap() {
      MinimapCanvas.Children.Clear();

      double minimapWidth = MinimapCanvas.ActualWidth;
      double minimapHeight = MinimapCanvas.ActualHeight;
      if(minimapWidth > 0 && minimapHeight > 0) {
        int lanes = Math.Max(1, timeline.Lanes);
        double laneHeight = minimapHeight / lanes;
        double scaleX = minimapWidth / timeline.Length;

        // Draw notes in minimap
        foreach(var note in timeline.Notes) {
          Rectangle rect = new() {
            Width = Math.Max(1, note.Duration * scaleX),
            Height = laneHeight - 1,
            Fill = Brushes.Cyan,
            IsHitTestVisible = false
          };

          Canvas.SetLeft(rect, note.Start * scaleX);
          Canvas.SetTop(rect, (lanes / 2 - note.Lane) * laneHeight);
          MinimapCanvas.Children.Add(rect);
        }
      }

      InitMinimapViewport();

      Canvas.SetZIndex(minimapViewport, 100);
      UpdateMinimapViewport();
    }
    #endregion

    #region Timeline / Minimap Controls
    private void JumpToMinimapPosition(double mouseX) {
      double minimapWidth = MinimapCanvas.ActualWidth;
      if(minimapWidth <= 0) return;

      double ratio = mouseX / minimapWidth;
      ratio = Math.Clamp(ratio, 0, 1);

      ///double totalWidth = timeline.Length * 100;
      double totalWidth = NotesCanvas.Width;
      double viewportWidth = TimelineScrollViewer.ViewportWidth;

      double maxOffset = Math.Max(0, totalWidth - viewportWidth);
      double targetOffset = (totalWidth * ratio) - (viewportWidth / 2);

      TimelineScrollViewer.ScrollToHorizontalOffset(
          Math.Clamp(targetOffset, 0, maxOffset)
      );
    }
    public void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      isDraggingMinimap = true;
      MinimapCanvas.CaptureMouse();

      double x = e.GetPosition(MinimapCanvas).X;
      JumpToMinimapPosition(x);
    }
    public void Minimap_MouseMove(object sender, MouseEventArgs e) {
      if(!isDraggingMinimap) return;

      double x = e.GetPosition(MinimapCanvas).X;
      JumpToMinimapPosition(x);
    }
    public void Minimap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
      isDraggingMinimap = false;
      MinimapCanvas.ReleaseMouseCapture();
    }
    public void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
      // Wheel ONLY = horizontal scroll
      TimelineScrollViewer.ScrollToHorizontalOffset(
          TimelineScrollViewer.HorizontalOffset - e.Delta
      );

      e.Handled = true;
    }
    public void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) {
      DrawPitchGraph();
      UpdateMinimapViewport(); // only move the viewport rectangle
    }
    #endregion

    #region Status Box Helpers
    private void UpdateStatusBox(string message, bool overwrite = false) {
      Dispatcher.Invoke(() => {
        if(overwrite)
          StatusTextBlock.Text = $"[{DateTime.Now:t}]: {message}\n";
        else
          StatusTextBlock.Text += $"[{DateTime.Now:t}]: {message}\n";

        OutputScrollView.ScrollToBottom();
      });
    }
    private void ClearStatusBox() {
      StatusTextBlock.Text = "";
    }
    #endregion

    #region CLI Helpers
    private static string BuildGeneratorArguments(string audioFile, GeneratorSettings cfg) {
      var args = new List<string> {
    $"\"{audioFile}\"",

    $"--window-size {cfg.WindowSize}",
    $"--hop-size {cfg.HopSize}",
    $"--min-freq {cfg.MinFreq}",
    $"--max-freq {cfg.MaxFreq}",

    $"--smooth-frames {cfg.SmoothFrames}",
    $"--stability-frames {cfg.StabilityFrames}",
    $"--hold-tolerance {cfg.HoldTolerance}",

    $"--min-note-duration {cfg.MinNoteDuration}",
    $"--merge-gap {cfg.MergeGap}",
    $"--note-pitch-tolerance {cfg.NoteMergeTolerance}",

    $"--phrase-gap {cfg.PhraseGap}",
    $"--phrase-pitch-tolerance {cfg.PhrasePitchTolerance}",
    $"--stretch-factor {cfg.StretchFactor}",

    $"--final-merge-gap {cfg.FinalMergeGap}",

    $"--lane-range {cfg.LaneRange}"
  };

      return string.Join(" ", args);
    }
    #endregion

    #region Extra Classes
    public class GeneratorSettings {
      [JsonPropertyName("profile")]
      public string Profile { get; set; } = "";
      // Analysis
      [JsonPropertyName("window_size")]
      public int WindowSize { get; set; } = 2048;
      [JsonPropertyName("hop_size")]
      public int HopSize { get; set; } = 512;
      [JsonPropertyName("min_freq")]
      public double MinFreq { get; set; } = 70.0;
      [JsonPropertyName("max_freq")]
      public double MaxFreq { get; set; } = 1100.0;

      // Stability
      [JsonPropertyName("smooth_frames")]
      public int SmoothFrames { get; set; } = 5;
      [JsonPropertyName("stability_frames")]
      public int StabilityFrames { get; set; } = 6;
      [JsonPropertyName("hold_tolerance")]
      public double HoldTolerance { get; set; } = 0.75;

      // Notes
      [JsonPropertyName("min_note_duration")]
      public double MinNoteDuration { get; set; } = 0.12;
      [JsonPropertyName("merge_gap")]
      public double MergeGap { get; set; } = 0.05;
      [JsonPropertyName("note_merge_tolerance")]
      public double NoteMergeTolerance { get; set; } = 0.5;

      // Phrases
      [JsonPropertyName("phrase_gap")]
      public double PhraseGap { get; set; } = 0.45;
      [JsonPropertyName("phrase_pitch_tolerance")]
      public double PhrasePitchTolerance { get; set; } = 1.75;
      [JsonPropertyName("stretch_factor")]
      public double StretchFactor { get; set; } = 1.25;

      // Final
      [JsonPropertyName("final_merge_gap")]
      public double FinalMergeGap { get; set; } = 0.15;

      // Lanes
      [JsonPropertyName("lane_range")]
      public int LaneRange { get; set; } = 4;
    }
    public class Timeline {
      [JsonPropertyName("name")]
      public string Name { get; set; } = "";

      [JsonPropertyName("length")]
      public double Length { get; set; }

      [JsonPropertyName("lanes")]
      public int Lanes { get; set; }

      [JsonPropertyName("configs")]
      public Configs Configs { get; set; } = new();

      [JsonPropertyName("notes")]
      public List<Note> Notes { get; set; } = new();

      [JsonPropertyName("pitches")]
      public List<PitchSample> PitchSamples { get; set; } = new();
    }
    public class Configs {
      public string? profile { get; set; } = "";
      public string? song { get; set; } = "";
    }
    public class Note {
      [JsonPropertyName("start")]
      public double Start { get; set; }

      [JsonPropertyName("duration")]
      public double Duration { get; set; }

      [JsonPropertyName("lane")]
      public int Lane { get; set; }
    }
    public class PitchSample {
      [JsonPropertyName("time")]
      public double Time { get; set; }   // seconds
      [JsonPropertyName("pitch")]
      public double Pitch { get; set; }  // Hz
      [JsonPropertyName("midi")]
      public double Midi { get; set; }   // Midi
    }
    #endregion
  }
}