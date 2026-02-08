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

    // State
    private string audioPath;
    private string lyricsPath = string.Empty;

    private Timeline timeline;
    private Dictionary<string, GeneratorSettings> configs;
    private Note? hoveredNote = null;
    private double splitPreviewTime = 0;
    private Line? splitPreviewLine = null;

    private double LanePixelHeight => NotesCanvas.ActualHeight / timeline.Lanes;
    private const double PixelsPerSecond = 100;
    double TimelineWidthSeconds => audio?.TotalTime.TotalSeconds ?? timeline?.Length ?? 10;

    private Line? playheadLine;

    // Dragging state
    private Rectangle? draggedNoteRect;
    private Note? draggedNote;
    private Note? original;
    private Point dragStartMouse;
    private double dragStartX;
    private double dragStartY;

    // Minimap
    private Rectangle? minimapViewport;
    private bool isDraggingMinimap = false;

    // Audio playback
    private WaveOutEvent? output;
    private AudioFileReader? audio;
    private TranslateTransform playheadTransform = new();

    // Config Info
    private GeneratorSettings? CurrentConfig => configs.TryGetValue(Properties.Settings.Default.LastConfigFile, out var cfg) ? cfg : null;
    private static string ConfigFolder => Properties.Settings.Default.ConfigFolder;

    // Services
    private readonly NotechartGenerator _generator = new();
    private SettingsWindow SettingsWindow = new();


    public MainWindow() {
      InitializeComponent();

      audioPath = string.Empty;
      timeline = new();
      configs = new();

      // Force update when canvas size changes
      TimelineGrid.SizeChanged += (_, __) => {
        DrawTimeGrid();
        DrawNotes();
        DrawPitchGraph();
        CreatePlayHead();
        DrawLyrics();
      };

      MinimapCanvas.SizeChanged += (s, e) => DrawMinimap();

      CompositionTarget.Rendering += (s, e) => {
        if(audio == null) return;
        if(audio.CurrentTime.TotalSeconds >= audio.TotalTime.TotalSeconds && LoopCheckbox.IsChecked == true) {
          Reset();
          return;
        }

        UpdatePlayhead(audio.CurrentTime.TotalSeconds);
      };

      LoadConfigs();
      ConfigWatcherService.ConfigsChanged += LoadConfigs;

      DrawTimeGrid();
    }
    protected override void OnClosed(EventArgs e) {
      ConfigWatcherService.ConfigsChanged -= LoadConfigs;
      Application.Current.Shutdown();
      base.OnClosed(e);
    }

    #region Input Handlers
    private void Import_Click(object sender, RoutedEventArgs e) => ImportNotes();
    private void Browse_Click(object sender, RoutedEventArgs e) => BrowseAudio();
    private void BrowseLyrics_Click(object sender, RoutedEventArgs e) => BrowseLyrics();
    private void ClearProject_Click(object sender, RoutedEventArgs e) => ClearTimeline();
    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveTimeline();
    private void GenerateNotes_Click(object sender, RoutedEventArgs e) => GenerateNotes();
    private void Play_Click(object sender, RoutedEventArgs e) => Play();
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void Reset_Click(object sender, RoutedEventArgs e) => Reset();
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    private void SaveGeneratorSettings_Click(object sender, RoutedEventArgs e) => SaveCurrentConfig();
    private void SaveNewGeneratorSettings_Click(object sender, RoutedEventArgs e) => SaveNewConfig();
    private void ConfigSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectConfig();
    private void DeleteCurrentConfig_Click(object sendNotesCanvas, RoutedEventArgs e) => DeleteCurrentConfig();
    #endregion

    #region Input Events
    private void Window_KeyUp(object sender, KeyEventArgs e) {
      if(e.Key == Key.S)
        TrySplitHoveredNote();
      else if(e.Key == Key.Space) {
        if(output?.PlaybackState == PlaybackState.Playing)
          Stop();
        else
          Play();

        e.Handled = true; // Stop WPF from sending it to buttons
      } else if(e.Key == Key.R)
        Reset();
    }
    private void Note_MouseDown(object sender, MouseButtonEventArgs e) {
      if(sender is not Rectangle rect) return;
      if(rect.Tag is not Note note) return;

      original = new Note {
        Start = note.Start,
        Lane = note.Lane,
        Duration = note.Duration
      };

      draggedNoteRect = rect;
      draggedNote = note;

      dragStartMouse = e.GetPosition(NotesCanvas);
      dragStartX = Canvas.GetLeft(rect);
      dragStartY = Canvas.GetTop(rect);

      rect.CaptureMouse();
      e.Handled = true;
    }
    private void Note_MouseMove(object sender, MouseEventArgs e) {
      if(draggedNoteRect == null || draggedNote == null) return;
      if(e.LeftButton != MouseButtonState.Pressed) return;

      Point pos = e.GetPosition(NotesCanvas);
      Vector delta = pos - dragStartMouse;

      // --- Time (X axis) ---
      double newX = Math.Max(0, dragStartX + delta.X);
      Canvas.SetLeft(draggedNoteRect, newX);
      draggedNote.Start = Math.Round(newX / PixelsPerSecond, 3);

      // --- Lane (Y axis) ---
      double newY = dragStartY + delta.Y;
      double clampedY = Math.Clamp(newY, 0, NotesCanvas.ActualHeight);

      double laneFloat = (clampedY + LanePixelHeight / 2) / LanePixelHeight;
      int laneIndex = (int)Math.Floor(laneFloat);

      int newLane = (timeline.Lanes - 1) - laneIndex;
      newLane = Math.Clamp(newLane, 0, timeline.Lanes - 1);

      draggedNote.Lane = newLane;
      Canvas.SetTop(draggedNoteRect, LaneToY(newLane, LanePixelHeight));

      UpdateMinimapViewport();
    }
    private void Note_MouseUp(object sender, MouseButtonEventArgs e) {
      if(draggedNoteRect == null || draggedNote == null) return;

      draggedNoteRect.ReleaseMouseCapture();

      // Try to snap the note into safe space
      bool ok = TrySnapNoteToTimeline(draggedNote);
      if(!ok) {
        draggedNote.Start = original.Start;
        draggedNote.Lane = original.Lane;
      }

      draggedNoteRect = null;
      draggedNote = null;
      original = null;

      DrawNotes();
      DrawMinimap();
    }
    private void NotesCanvas_MouseMove(object sender, MouseEventArgs e) {
      var pos = e.GetPosition(NotesCanvas);
      hoveredNote = GetNoteAtPosition(pos);

      if(hoveredNote == null) {
        ClearSplitPreview();
        NotesCanvas.Cursor = Cursors.Arrow;
        return;
      }

      NotesCanvas.Cursor = Cursors.SizeNS;
      double time = pos.X / PixelsPerSecond;

      splitPreviewTime = Math.Clamp(
          time,
          hoveredNote.Start + 0.001,
          hoveredNote.Start + hoveredNote.Duration - 0.001
      );

      DrawSplitPreviewLine(hoveredNote, splitPreviewTime);
    }
    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e) {
      if(e.ChangedButton == MouseButton.Left) {
        if(hoveredNote != null) return; // Don't seek if we were dragging a note

        double time = e.GetPosition(NotesCanvas).X / PixelsPerSecond;
        if(audio != null) {
          audio.CurrentTime = TimeSpan.FromSeconds(time);
          UpdatePlayhead(time);
        }
      } else if(e.ChangedButton == MouseButton.Middle) {
        if(timeline == null) return;

        int lane = timeline.Lanes - 1 - (int)(e.GetPosition(NotesCanvas).Y / LanePixelHeight);
        double time = e.GetPosition(NotesCanvas).X / PixelsPerSecond;

        if(lane < 0 || lane >= timeline.Lanes) return;
        if(time < 0 || time > TimelineWidthSeconds) return;

        Note newNote = new() {
          Lane = lane,
          Start = time,
          Duration = 0.5 // default, can adjust later
        };

        // Snap to safe space (push other notes if needed)
        bool placed = TrySnapNoteToTimeline(newNote);
        if(!placed) {
          UpdateStatusBox("Cannot place note: no free space.");
        } else {
          timeline.Notes.Add(newNote);
          DrawNotes();
          DrawMinimap();
        }
      }
    }
    public void Minimap_MouseMove(object sender, MouseEventArgs e) {
      if(!isDraggingMinimap) return;

      double x = e.GetPosition(MinimapCanvas).X;
      JumpToMinimapPosition(x);
    }
    public void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      isDraggingMinimap = true;
      MinimapCanvas.CaptureMouse();

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

    #region Note Manipulation
    private void TrySplitHoveredNote() {
      if(hoveredNote == null) return;
      SplitNote(hoveredNote, splitPreviewTime);
      ClearSplitPreview();
    }
    private void DrawSplitPreviewLine(Note note, double splitTime) {
      ClearSplitPreview();

      double x = splitTime * PixelsPerSecond;
      double yTop = (timeline.Lanes - 1 - note.Lane) * LanePixelHeight;
      double yBottom = yTop + LanePixelHeight;

      splitPreviewLine = new Line {
        X1 = x,
        X2 = x,
        Y1 = yTop,
        Y2 = yBottom,
        Stroke = Brushes.Red,
        StrokeThickness = 2,
        IsHitTestVisible = false
      };

      NotesCanvas.Children.Add(splitPreviewLine);
    }
    private void ClearSplitPreview() {
      if(splitPreviewLine != null) {
        NotesCanvas.Children.Remove(splitPreviewLine);
        splitPreviewLine = null;
      }
    }
    private void SplitNote(Note note, double splitTime) {
      double leftDuration = splitTime - note.Start;
      double rightDuration = note.Duration - leftDuration;

      var leftNote = new Note {
        Start = note.Start,
        Duration = leftDuration,
        Lane = note.Lane,
      };

      var rightNote = new Note {
        Start = splitTime,
        Duration = rightDuration,
        Lane = note.Lane,
      };

      timeline.Notes.Remove(note);
      timeline.Notes.Add(leftNote);
      timeline.Notes.Add(rightNote);

      timeline.Notes = timeline.Notes
          .OrderBy(n => n.Start)
          .ToList();

      DrawNotes();
      DrawMinimap();
    }
    private Note? GetNoteAtPosition(Point pos) {
      var (time, lane) = MouseToTimeline(pos);

      const double TimeTolerancePixels = 5;
      double timeTol = TimeTolerancePixels / PixelsPerSecond;

      return timeline.Notes
          .Where(n => n.Lane == lane)
          .FirstOrDefault(n =>
              time >= n.Start - timeTol &&
              time <= n.Start + n.Duration + timeTol
          );
    }
    #endregion

    #region Helpers
    private void UpdateStatusBox(string message, bool overwrite = false) {
      Dispatcher.Invoke(() => {
        if(overwrite)
          StatusTextBlock.Text = $"[{DateTime.Now:t}]: {message}\n";
        else
          StatusTextBlock.Text += $"[{DateTime.Now:t}]: {message}\n";

        OutputScrollView.ScrollToBottom();
      });
    }
    private (double time, int lane) MouseToTimeline(Point pos) {
      double time = pos.X / PixelsPerSecond;
      int lane = timeline.Lanes - 1 - (int)(pos.Y / LanePixelHeight);
      return (time, lane);
    }
    private static SolidColorBrush NoteColor(string type) {
      return (type) switch {
        "normal" => Brushes.Cyan,
        "hold" => Brushes.Magenta,
        "vibrato" => Brushes.Orange,
        _ => Brushes.Gray
      };

    }
    private static SolidColorBrush DarkenBrush(SolidColorBrush brush, double factor) {
      var c = brush.Color;
      return new SolidColorBrush(Color.FromRgb(
          (byte)(c.R * factor),
          (byte)(c.G * factor),
          (byte)(c.B * factor)
      ));
    }
    #endregion

    #region Config Handler
    public void LoadConfigs() {
      UpdateStatusBox($"Loading configurations: {ConfigFolder}");

      ConfigSelector.ItemsSource = null;

      if(string.IsNullOrWhiteSpace(ConfigFolder) || !Directory.Exists(ConfigFolder))
        return;

      List<ComboBoxItem> items = new();
      foreach(string path in Directory.GetFiles(ConfigFolder, "*.json")) {
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
        } catch(Exception ex) {
          UpdateStatusBox("Error loading config: " + ex.Message);
          Debug.WriteLine(ex);
        }
      }

      ConfigSelector.ItemsSource = items;

      string lastFile = Properties.Settings.Default.LastConfigFile;

      if(lastFile != "") {
        UpdateStatusBox($"Found last used config: {lastFile}.json");
        // LoadConfig(Properties.Settings.Default.LastConfigFile);
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
        if(string.IsNullOrWhiteSpace(ConfigFolder) || !Directory.Exists(ConfigFolder)) {
          MessageBox.Show("Config folder is not set or does not exist. Please set it in the settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        string filePath = System.IO.Path.Combine(ConfigFolder, $"{config.Profile}.json");
        File.WriteAllText(filePath, json);
        UpdateStatusBox($"Configuration updated: {config.Profile}.json");
        LoadConfigs(); // Refresh the list
      } catch(Exception ex) {
        MessageBox.Show($"Error updating configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
    private void SaveCurrentConfig() {
      if(CurrentConfig == null) {
        UpdateStatusBox("No configuration selected to save.");
        return;
      }

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
    private void DeleteCurrentConfig() {
      if(CurrentConfig == null) {
        UpdateStatusBox("No configuration selected to delete.");
        return;
      }

      string profile = CurrentConfig.Profile.ToLower();

      if(string.IsNullOrEmpty(profile)) {
        MessageBox.Show("No configuration selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      } else if(profile == "default") {
        MessageBox.Show("Cannot delete the default configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      MessageBoxResult result = MessageBox.Show(
        $"Are you sure you want to delete the configuration '{profile}'?",
        "Confirm Deletion",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning
      );

      if(result == MessageBoxResult.Yes) {
        try {
          string filePath = System.IO.Path.Combine(ConfigFolder, $"{profile}.json");
          if(File.Exists(filePath)) {
            File.Delete(filePath);
            UpdateStatusBox($"Deleted configuration: {profile}.json");
            LoadConfigs();
          } else {
            MessageBox.Show("Configuration file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          }
        } catch(Exception ex) {
          MessageBox.Show($"Error deleting configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
      }
    }
    #endregion

    #region ButtonHandlers
    private void ImportNotes() {
      OpenFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        string json = File.ReadAllText(dlg.FileName);

        try {
          Timeline? loaded = JsonSerializer.Deserialize<Timeline>(json);


          if(loaded == null) {
            MessageBox.Show("Failed to load timeline from the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
          }

          NoteFileLabel.Text = $"Note File: {System.IO.Path.GetFileName(dlg.FileName)}";
          timeline = loaded;
        } catch(Exception ex) {
          Debug.WriteLine(ex);
          UpdateStatusBox("Failed to load timeline from selected file");
        } finally {
          LaneCountTextBlock.Text = $"{timeline.Lanes}";

          UpdateStatusBox($"Imported Notes: {System.IO.Path.GetFileName(dlg.FileName)}");
          DrawTimeline();
        }
      }
    }
    private void BrowseLyrics() {
      if(audio == null) {
        MessageBox.Show("Please load an audio file before importing lyrics.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      OpenFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };

      if(dlg.ShowDialog() == true) {
        lyricsPath = dlg.FileName;
        LyricsFileLabel.Text = $"Lyrics File: {System.IO.Path.GetFileName(lyricsPath)}";

        timeline.Lyrics = LoadLyrics(lyricsPath);
        DrawLyrics();
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
        UpdateStatusBox($"Loaded {phrases.Count} lyric phrases.");
        return phrases;
      } catch(Exception ex) {
        Debug.WriteLine(ex);
        UpdateStatusBox("Failed to load lyrics from selected file");
        return new List<Lyric>();
      }
    }
    private void BrowseAudio() {
      OpenFileDialog dlg = new() {
        Filter = "Audio Files (*.wav;)|*.wav;"
      };

      if(dlg.ShowDialog() == true) {
        audioPath = dlg.FileName;

        audio = new AudioFileReader(audioPath);
        output = new WaveOutEvent();
        output.Init(audio);

        AudioFileLabel.Text = $"Audio File: {System.IO.Path.GetFileName(audioPath)} ({audio.TotalTime.Minutes}m {audio.TotalTime.Seconds}s)";

        DrawTimeline();
        GenerateButton.IsEnabled = true;
        UpdateStatusBox($"Loaded Audio: {System.IO.Path.GetFileName(audio.FileName)}");
      }
    }
    private void ClearTimeline() {
      Stop();

      MinimapCanvas.Children.Clear();
      NotesCanvas.Children.Clear();
      PitchCanvas.Children.Clear();
      LyricsCanvas.Children.Clear();

      timeline = new();

      NoteFileLabel.Text = "Note File: (none)";

      InitMinimapViewport();
      UpdateMinimapViewport();

      UpdatePlayhead(0);
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
    private void SaveTimeline() {
      if(timeline.Notes.Count == 0) {
        MessageBox.Show("No notes to save.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }
      SaveFileDialog dlg = new() {
        Filter = "JSON Files (*.json)|*.json"
      };
      if(dlg.ShowDialog() == true) {
        string json = JsonSerializer.Serialize(timeline, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, json);
        UpdateStatusBox($"Saved timeline to: {System.IO.Path.GetFileName(dlg.FileName)}");
      }
    }
    #endregion

    #region Generate / Run Python
    private async void GenerateNotes() {
      if(CurrentConfig == null) return;

      GenerateButton.IsEnabled = false;
      Stop();

      var progress = new Progress<string>(msg => UpdateStatusBox(msg));
      var result = await _generator.GenerateAsync(
        audioPath,
        CurrentConfig,
        progress
      );

      if(!result.Success) {
        UpdateStatusBox("Chart generation failed.");
        UpdateStatusBox(result.Error ?? "Unknown error");
        GenerateButton.IsEnabled = true;
        return;
      }

      LoadTimeline(result.OutputPath!);
      GenerateButton.IsEnabled = true;
    }
    private void LoadTimeline(string jsonPath) {
      string jsonText = File.ReadAllText(jsonPath);
      var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

      var loaded = JsonSerializer.Deserialize<Timeline>(jsonText, options);
      if(loaded == null) {
        MessageBox.Show("Failed to load timeline.", "Error");
        return;
      }

      List<Lyric> prevLoadLyrics = timeline.Lyrics ?? new List<Lyric>();
      if(timeline?.Lyrics?.Count > 0) {
        prevLoadLyrics = timeline.Lyrics;
      }

      timeline = loaded;
      timeline.Lyrics = prevLoadLyrics; // Preserve previously loaded lyrics if any

      LaneCountTextBlock.Text = $"{timeline.Lanes}";
      NoteFileLabel.Text = $"Note File: {System.IO.Path.GetFileName(jsonPath)}";

      UpdateStatusBox($"{timeline.Notes.Count} notes loaded onto Timeline.");
      DrawTimeline();
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

      playheadLine = new Line {
        Stroke = Brushes.Red,
        StrokeThickness = 2,
        Y1 = 0,
        Y2 = PlayheadCanvas.ActualHeight,
        X1 = 0,
        X2 = 0,
        Tag = "playhead",
        IsHitTestVisible = false,
        RenderTransform = playheadTransform // <-- GPU-friendly transform
      };

      PlayheadCanvas.SizeChanged += (_, _) => UpdatePlayheadHeight();
      PlayheadCanvas.Children.Add(playheadLine);
    }
    private void UpdatePlayhead(double timeSeconds) {
      playheadTransform.X = TimeToCanvasX(timeSeconds);
      TimeSpan time = TimeSpan.FromSeconds(timeSeconds);
      PlayheadLabel.Text = time.ToString(@"mm\:ss\.ff");
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
      if(audio == null || output == null) {
        UpdateStatusBox("No audio loaded..");
        return;
      }

      output.Play();
    }
    private void Stop() {
      if(output == null) return;
      output.Pause();
    }
    private void Reset() {
      if(audio == null) return;

      audio.Position = 0;
      audio.CurrentTime = TimeSpan.Zero;
      UpdatePlayhead(0);
    }
    #endregion

    #region Timeline/Minimap Rendering
    private void SetupTimelineForAudio() {
      PitchCanvas.Width = LyricsCanvas.Width = NotesCanvas.Width = PlayheadCanvas.Width = TimelineWidthSeconds * PixelsPerSecond;
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
      if(timeline.Notes.Count == 0) return;
      NotesCanvas.Children.Clear();

      foreach(var note in timeline.Notes) {
        SolidColorBrush fillBrush = NoteColor(note.Type);
        var strokeBrush = DarkenBrush(fillBrush, 0.6);

        Rectangle rect = new() {
          Width = Math.Max(1, note.Duration * PixelsPerSecond),
          Height = LanePixelHeight,
          Fill = fillBrush,
          Opacity = 0.55,
          Tag = note,
          IsHitTestVisible = true,
          Stroke = strokeBrush,
          StrokeThickness = 1,
        };

        Canvas.SetLeft(rect, note.Start * PixelsPerSecond);
        Canvas.SetTop(rect, LaneToY(note.Lane, LanePixelHeight));

        rect.MouseRightButtonUp += (_, __) => RemoveNoteFromTimeline(note);
        rect.MouseLeftButtonDown += Note_MouseDown;
        rect.MouseMove += Note_MouseMove;
        rect.MouseLeftButtonUp += Note_MouseUp;

        NotesCanvas.Children.Add(rect);
      }
    }
    private void DrawPitchGraph() {
      PitchCanvas.Children.Clear();

      if(timeline.PitchSamples.Count == 0) return;

      var geometry = new PathGeometry();
      PathFigure? figure = null;
      PitchSample? prev = null;

      double viewStart = TimelineScrollViewer.HorizontalOffset;
      double viewEnd = viewStart + TimelineScrollViewer.ViewportWidth;

      foreach(var p in timeline.PitchSamples) {
        double x = TimeToCanvasX(p.Time);

        // Only draw if within viewport
        if(x < viewStart || x > viewEnd || p.Midi <= 0)
          continue;

        if(prev == null || !IsContinuous(prev, p)) {
          figure = new PathFigure {
            StartPoint = new Point(x, MidiToY(p.Midi)),
            IsFilled = false,
            IsClosed = false
          };
          geometry.Figures.Add(figure);
        } else {
          figure!.Segments.Add(new LineSegment(new Point(x, MidiToY(p.Midi)), true));
        }

        prev = p;
      }

      PitchCanvas.Children.Add(new System.Windows.Shapes.Path {
        Data = geometry,
        Stroke = Brushes.Orange,
        StrokeThickness = 2,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
      });
    }

    private static bool IsContinuous(PitchSample prev, PitchSample curr) {
      if(curr.Midi <= 0 || prev.Midi <= 0)
        return false;

      double dt = curr.Time - prev.Time;
      if(dt <= 0)
        return false;

      double dm = Math.Abs(curr.Midi - prev.Midi);

      const double MaxTimeGap = 0.03;        // seconds
      const double SoftMidiJump = 0.35;      // semitones
      const double HardMidiJump = 0.75;      // semitones

      double pitchVelocity = Math.Abs(curr.Midi - prev.Midi) / dt;
      const double MaxPitchVelocity = 25; // semitones per secon

      if(dt > MaxTimeGap)
        return false;

      if(pitchVelocity > MaxPitchVelocity)
        return false;

      // Small jump → always continuous
      if(dm <= SoftMidiJump)
        return true;

      // Large jump → always break
      if(dm >= HardMidiJump)
        return false;

      // Middle zone: allow if time gap is very small
      return dt < MaxTimeGap * 0.5;
    }
    private double TimeToCanvasX(double timeSeconds)
      => timeSeconds * PixelsPerSecond;
    private double LaneToY(int lane, double laneHeight) {
      int laneIndex = (timeline.Lanes - 1) - lane;
      laneIndex = Math.Clamp(laneIndex, 0, timeline.Lanes - 1);
      return laneIndex * laneHeight;
    }
    private double MidiToY(double midi) {
      double minMidi = FrequencyToMidi(double.Parse(MinFrequencyBox.Text));
      double maxMidi = FrequencyToMidi(double.Parse(MaxFrequencyBox.Text));
      double pitchHeightFraction = 0.5;
      double pitchHeight = PitchCanvas.ActualHeight * pitchHeightFraction;
      double topOffset = PitchCanvas.ActualHeight * (1 - pitchHeightFraction);
      double normalized = (midi - minMidi) / (maxMidi - minMidi);
      normalized = Math.Clamp(normalized, 0, 1);
      return topOffset + (1 - normalized) * pitchHeight;
    }
    private static double FrequencyToMidi(double frequency) {
      return 69 + 12 * Math.Log2(frequency / 440.0);
    }
    private void DrawTimeline() {
      SetupTimelineForAudio();

      DrawTimeGrid();
      DrawPitchGraph();
      UpdateStatusBox($"Loaded {timeline.PitchSamples.Count} pitch samples.");

      DrawNotes();
      CreatePlayHead();

      DrawLyrics();
      DrawMinimap();
    }
    private void DrawTimeGrid() {
      GridCanvas.Children.Clear();

      double height = TimelineGrid.ActualHeight;
      double width = TimelineGrid.ActualWidth;

      for(double t = 0; t < width / PixelsPerSecond; t += 0.25) {
        double x = TimeToCanvasX(t);

        bool isSecond = Math.Abs(t % 1.0) < 0.001;

        var line = new Line {
          X1 = x,
          X2 = x,
          Y1 = 0,
          Y2 = height,
          Stroke = isSecond
                ? new SolidColorBrush(Color.FromRgb(60, 60, 60))
                : new SolidColorBrush(Color.FromRgb(40, 40, 40)),
          StrokeThickness = isSecond ? 2 : 1
        };

        GridCanvas.Children.Add(line);
      }
    }
    private void DrawMinimap() {
      if(MinimapCanvas.ActualHeight <= 0 || MinimapCanvas.ActualWidth <= 0)
        return;

      MinimapCanvas.Children.Clear();

      double minimapWidth = MinimapCanvas.ActualWidth;
      double minimapHeight = MinimapCanvas.ActualHeight;

      double scaleX = minimapWidth / TimelineWidthSeconds;
      double minimapLaneHeight = minimapHeight / timeline.Lanes;

      foreach(var note in timeline.Notes) {
        SolidColorBrush fillBrush = NoteColor(note.Type);

        Rectangle rect = new() {
          Width = Math.Max(1, note.Duration * scaleX),
          Height = minimapLaneHeight,
          Fill = fillBrush,
          IsHitTestVisible = false
        };

        Canvas.SetLeft(rect, note.Start * scaleX);
        Canvas.SetTop(rect, LaneToY(note.Lane, minimapLaneHeight));

        MinimapCanvas.Children.Add(rect);
      }

      InitMinimapViewport();
      Canvas.SetZIndex(minimapViewport, 100);
      UpdateMinimapViewport();
    }
    private void DrawLyrics() {
      if(audio == null || output == null) return;
      LyricsCanvas.Children.Clear();

      foreach(Lyric l in timeline.Lyrics) {
        if(string.IsNullOrWhiteSpace(l.Text)) continue;

        double width = Math.Max(1, (l.End - l.Start) * PixelsPerSecond);

        Border border = new() {
          Width = width,
          Height = 80,
          Background = new SolidColorBrush(Color.FromRgb(51, 0, 0)), 
          CornerRadius = new CornerRadius(12), 
          Padding = new Thickness(5),
          Margin = new Thickness(5, 0, 0, 5),
          HorizontalAlignment = HorizontalAlignment.Stretch 
        };

        TextBlock text = new() { 
          Text = l.Text, 
          FontSize = 20, 
          Foreground = Brushes.White, 
          TextAlignment = TextAlignment.Center, 
          TextWrapping = TextWrapping.Wrap, 
          VerticalAlignment = VerticalAlignment.Center, 
          HorizontalAlignment = HorizontalAlignment.Stretch, 
          LineStackingStrategy = LineStackingStrategy.BlockLineHeight, 
        };

        border.Child = text;

        Canvas.SetLeft(border, TimeToCanvasX(l.Start));
        Canvas.SetRight(border, TimeToCanvasX(l.End));
        Canvas.SetTop(border, 10); 
        
        LyricsCanvas.Children.Add(border);
      }
    }
    #endregion

    #region Timeline / Minimap Controls
    private void JumpToMinimapPosition(double mouseX) {
      double minimapWidth = MinimapCanvas.ActualWidth;
      if(minimapWidth <= 0) return;

      double ratio = mouseX / minimapWidth;
      ratio = Math.Clamp(ratio, 0, 1);

      double totalWidth = NotesCanvas.Width;
      double viewportWidth = TimelineScrollViewer.ViewportWidth;

      double maxOffset = Math.Max(0, totalWidth - viewportWidth);
      double targetOffset = (totalWidth * ratio) - (viewportWidth / 2);

      TimelineScrollViewer.ScrollToHorizontalOffset(
          Math.Clamp(targetOffset, 0, maxOffset)
      );
    }
    private void RemoveNoteFromTimeline(Note clickedNote) {
      timeline.Notes.Remove(clickedNote);
      DrawNotes();
      DrawMinimap();
      UpdateStatusBox($"Removed note at {clickedNote.Start:F2}s, lane {clickedNote.Lane}");
    }
    private bool IsOverlapping(Note note, double startTime) {
      double duration = note.Duration;
      int lane = note.Lane;

      return timeline.Notes.Any(n =>
          n != note &&
          n.Start < startTime + duration &&
          n.Start + n.Duration > startTime
      );
    }
    private bool TrySnapNoteToTimeline(Note anchorNote) {
      if(timeline == null) return false;

      double duration = anchorNote.Duration;

      // Get all notes except the anchor (all lanes)
      var otherNotes = timeline.Notes
          .Where(n => n != anchorNote)
          .OrderBy(n => n.Start)
          .ToList();

      foreach(var n in otherNotes) {
        // Check for collision
        if(n.Start < anchorNote.Start + duration && n.Start + n.Duration > anchorNote.Start) {
          // Decide which side to move the colliding note based on drop position
          double anchorCenter = anchorNote.Start + duration / 2;
          double colliderCenter = n.Start + n.Duration / 2;

          bool shifted = false;

          if(anchorCenter < colliderCenter) {
            // Dropped on left half → move collider right
            double newStart = anchorNote.Start + duration;
            if(newStart + n.Duration <= TimelineWidthSeconds && !IsOverlapping(n, newStart)) {
              n.Start = newStart;
              shifted = true;
            }
          } else {
            // Dropped on right half → move collider left
            double newStart = anchorNote.Start - n.Duration;
            if(newStart >= 0 && !IsOverlapping(n, newStart)) {
              n.Start = newStart;
              shifted = true;
            }
          }

          if(!shifted) {
            // Cannot move collider → fail placement
            return false;
          }
        }
      }

      // Sort notes and redraw
      timeline.Notes = [.. timeline.Notes.OrderBy(n => n.Start)];

      DrawNotes();
      DrawMinimap();
      return true;
    }
    #endregion

    #region Extra Classes
    public class Timeline {
      [JsonPropertyName("name")]
      public string Name { get; set; } = "";

      [JsonPropertyName("length")]
      public double Length { get; set; }

      [JsonPropertyName("lanes")]
      public int Lanes { get; set; }

      [JsonPropertyName("notes")]
      public List<Note> Notes { get; set; } = new();

      [JsonPropertyName("pitches")]
      public List<PitchSample> PitchSamples { get; set; } = new();
      [JsonPropertyName("lyrics")]
      public List<Lyric> Lyrics { get; set; } = new();
    }
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
    public class PitchSample {
      [JsonPropertyName("time")]
      public double Time { get; set; }   // seconds
      [JsonPropertyName("pitch")]
      public double Pitch { get; set; }  // Hz
      [JsonPropertyName("midi")]
      public double Midi { get; set; }   // Midi
    }
    public class Lyric {
      [JsonPropertyName("start")]
      public double Start { get; set; }
      [JsonPropertyName("end")]
      public double End { get; set; }
      [JsonPropertyName("text")]
      public string Text { get; set; } = "";
    }
    #endregion
  }
}