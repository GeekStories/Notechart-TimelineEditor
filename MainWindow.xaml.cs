using Microsoft.Win32;
using NAudio.Wave;
using NWaves.Audio;
using NWaves.Features;
using NWaves.Signals;
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
    private Timeline? timeline;
    private List<PitchSample> pitchData = new();
    private CancellationTokenSource? pitchCts;

    // For minimap interaction
    private Rectangle minimapViewport;
    private bool isDraggingMinimap = false;

    private WaveOutEvent? output;
    private AudioFileReader? audio;
    private DispatcherTimer? playheadTimer;

    private Line? playheadLine;

    public MainWindow() {
      InitializeComponent();

      audioPath = string.Empty;
      jsonPath = string.Empty;
      timeline = null;

      output = null;
      audio = null;

      minimapViewport = new Rectangle {
        Height = 0,
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0))
      };

      playheadLine = null;
      TimelineCanvas.SizeChanged += (_, _) => UpdatePlayheadHeight();

      MinimapCanvas.Loaded += (_, _) => DrawMinimap();
      MinimapCanvas.SizeChanged += (_, _) => DrawMinimap();
    }

    private void Import_Click(object sender, RoutedEventArgs e) => ImportNotes();
    private void Browse_Click(object sender, RoutedEventArgs e) => BrowseAudio();
    private void ClearProject_Click(object sender, RoutedEventArgs e) => ClearProject();
    private void GenerateNotes_Click(object sender, RoutedEventArgs e) => GenerateNotes();
    private void Play_Click(object sender, RoutedEventArgs e) => Play();
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void Reset_Click(object sender, RoutedEventArgs e) => Reset();

    #region ButtonHandlers
    // Top Bar Buttons
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

        NotesFileLabel.Text = "Notes File: " + System.IO.Path.GetFileName(dlg.FileName);
        timeline = loaded;

        DrawTimeline();
      }
    }
    private void BrowseAudio() {
      OpenFileDialog dlg = new() {
        Filter = "Audio Files (*.wav;)|*.wav;"
      };

      if(dlg.ShowDialog() == true) {
        AudioFileLabel.Text = "Audio File: " + System.IO.Path.GetFileName(dlg.FileName);
        audioPath = dlg.FileName;

        LoadAudio(audioPath);
        StartPitchExtraction(audioPath);
      }
    }
    private void ClearProject() {
      pitchCts?.Cancel();
      pitchCts = null;

      timeline = null;

      TimelineCanvas.Children.Clear();
      MinimapCanvas.Children.Clear();

      NotesFileLabel.Text = "Notes File: None";
      AudioFileLabel.Text = "Audio File: None";

      output?.Stop();
      output?.Dispose();
      audio?.Dispose();

      audioPath = string.Empty;
      jsonPath = string.Empty;

      pitchData = new();

      UpdatePlayhead(0);
      ClearStatusBox();
    }
    #endregion

    #region Generate / Run Python
    private async void GenerateNotes() {
      if(string.IsNullOrEmpty(audioPath)) return;

      GenerateButton.IsEnabled = false;
      UpdateStatusBox("Generating chart...");

      bool success = await Task.Run(() => RunPythonGenerator(audioPath));

      if(success) {
        NotesFileLabel.Text = "Notes File: " + System.IO.Path.GetFileName(jsonPath);

        // Load JSON into chartData
        string jsonText = File.ReadAllText(jsonPath);

        JsonSerializerOptions options = new() {
          PropertyNameCaseInsensitive = true
        };

        Timeline? loaded = JsonSerializer.Deserialize<Timeline>(jsonText, options);
        if(loaded == null) {
          MessageBox.Show("Failed to load timeline from the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        timeline = loaded;
        UpdateStatusBox($"{timeline.Notes.Count} notes loaded onto Timeline.");


        // Refresh timeline + minimap
        DrawTimeline();
      } else {
        UpdateStatusBox("Chart generation failed. See console for details.");
      }

      GenerateButton.IsEnabled = true;
    }
    private bool RunPythonGenerator(string audioFile = "") {
      if(string.IsNullOrEmpty(audioFile)) return false;
      try {
        ProcessStartInfo psi = new() {
          FileName = "notechart",
          Arguments = $"\"{audioFile}\"", // pass the audio file as argument
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        UpdateStatusBox("Starting pitch extraction.");
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.WaitForExit();
        UpdateStatusBox("Pitch extraction finished.");

        jsonPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(audioFile), System.IO.Path.GetFileNameWithoutExtension(audioFile) + "_chart.json");
        return File.Exists(jsonPath);
      } catch(Exception ex) {
        UpdateStatusBox($"Error: {ex.Message}");
        return false;
      }
    }
    private void StartPitchExtraction(string audioPath) {
      // Cancel any previous extraction
      pitchCts?.Cancel();

      pitchCts = new CancellationTokenSource();
      CancellationToken token = pitchCts.Token;

      Task.Run(() =>
      {
        try {
          var pitch = ExtractPitchDataYin(audioPath, token); // pass token down
          pitch = SmoothPitch(pitch, window: 5);
          if(token.IsCancellationRequested) return;

          // Update UI on main thread
          Dispatcher.Invoke(() =>
          {
            pitchData = pitch;
            DrawPitchGraph();
          });
        } catch(OperationCanceledException) {
          UpdateStatusBox("Pitch Extraction Cancelled", true);
        }
      }, token);
    }
    private List<PitchSample> ExtractPitchDataYin(string audioFile, CancellationToken token, int hopSize = 1024, int minFreq = 80, int maxFreq = 1000) {
      var pitchData = new List<PitchSample>();
      using var fs = new FileStream(audioFile, FileMode.Open, FileAccess.Read);
      var wave = new WaveFile(fs);

      // Load and mix mono
      DiscreteSignal signal = wave.Signals[0]; // Mono-audio
      int sampleRate = signal.SamplingRate;

      // Determine safe frame size
      int frameSize = Math.Max(2048, (int)(2.0 * sampleRate / minFreq));

      for(int pos = 0; pos + frameSize <= signal.Length; pos += hopSize) {
        token.ThrowIfCancellationRequested();
        float[] frame = signal[pos, pos + frameSize].Samples;

        float percent = (float)pos / signal.Length * 100;
        UpdateStatusBox($"Processing pitch: {percent:F0}%", true);

        try {
          double freq = Pitch.FromYin(frame, sampleRate, 0, frame.Length, minFreq, maxFreq);
          pitchData.Add(new PitchSample { Time = (double)pos / sampleRate, Pitch = freq });
        } catch(OverflowException) {
          // Skip frames that cause overflow (silent/low-frequency)
          pitchData.Add(new PitchSample { Time = (double)pos / sampleRate, Pitch = 0 });
        }
      }

      UpdateStatusBox($"Extracted {pitchData.Count} pitch samples from audio.");
      return pitchData;
    }
    private List<PitchSample> SmoothPitch(List<PitchSample> raw, int window = 3) {
      var result = new List<PitchSample>(raw.Count);

      for(int i = 0; i < raw.Count; i++) {
        double sum = 0;
        int count = 0;

        for(int j = i - window; j <= i + window; j++) {
          if(j >= 0 && j < raw.Count && raw[j].Pitch > 0) {
            sum += raw[j].Pitch;
            count++;
          }
        }

        double avg = (count > 0) ? sum / count : 0;
        result.Add(new PitchSample { Time = raw[i].Time, Pitch = avg });
      }

      return result;
    }
    #endregion

    #region Playhead
    private void CreatePlayHead() {
      playheadLine = new Line {
        Stroke = Brushes.Red,
        StrokeThickness = 2,
        Y1 = 0,
        Y2 = TimelineCanvas.ActualHeight,
        IsHitTestVisible = false
      };
      TimelineCanvas.Children.Add(playheadLine);
    }
    private void UpdatePlayheadHeight() {
      if(playheadLine == null) return;

      playheadLine.Y1 = 0;
      playheadLine.Y2 = TimelineCanvas.ActualHeight;
    }
    private void LoadAudio(string path) {
      output?.Stop();
      output?.Dispose();
      audio?.Dispose();

      audio = new AudioFileReader(path);
      output = new WaveOutEvent();
      output.Init(audio);

      SetupPlayheadTimer();
      SetupTimelineForAudio();
      DrawTimelineGrid();
    }
    private void SetupPlayheadTimer() {
      if(audio == null) return;

      playheadTimer = new DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
      };
      playheadTimer.Tick += (s, e) => {
        UpdatePlayhead(audio.CurrentTime.TotalSeconds);
      };
    }
    private void UpdatePlayhead(double timeSeconds) {
      if(timeline == null || playheadLine == null) return;

      double pixelsPerSecond = 100; // same scale you use everywhere
      double x = timeSeconds * pixelsPerSecond;

      playheadLine.X1 = x;
      playheadLine.X2 = x;

      // AutoScrollTimeline(x);
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
      if(audio == null || output == null || playheadTimer == null) return;

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
    private void UpdateTimelineCanvasWidth() {
      double pixelsPerSecond = 100;

      // Base timeline length from timeline object
      double timelineLength = timeline?.Length ?? 0;

      // Include pitch data length if it’s longer
      if(pitchData.Count > 0) {
        double lastPitchTime = pitchData[^1].Time; // C# 8 index from end
        timelineLength = Math.Max(timelineLength, lastPitchTime);
      }

      // If still zero, set a minimal default to avoid zero-width canvas
      if(timelineLength <= 0) timelineLength = 10;

      TimelineCanvas.Width = timelineLength * pixelsPerSecond;
      TimelineCanvas.Height = TimelineCanvas.ActualHeight;
    }
    private void SetupTimelineForAudio() {
      // Determine the length of the timeline in seconds
      double timelineLengthSeconds = timeline?.Length ?? 0;

      if(pitchData.Count > 0) {
        double lastPitchTime = pitchData[^1].Time;
        timelineLengthSeconds = Math.Max(timelineLengthSeconds, lastPitchTime);
      }

      double pixelsPerSecond = 100;

      // Set the width of TimelineCanvas for scrolling
      TimelineCanvas.Width = timelineLengthSeconds * pixelsPerSecond;
      TimelineCanvas.Height = TimelineCanvas.ActualHeight;

      // Clear previous children
      TimelineCanvas.Children.Clear();
    }
    private void DrawTimelineGrid() {
      double pixelsPerSecond = 100;
      double interval = 1.0; // 1-second marks

      for(double t = 0; t < TimelineCanvas.Width / pixelsPerSecond; t += interval) {
        Line line = new Line {
          X1 = t * pixelsPerSecond,
          Y1 = 0,
          X2 = t * pixelsPerSecond,
          Y2 = TimelineCanvas.ActualHeight,
          Stroke = Brushes.Gray,
          StrokeThickness = 0.5
        };
        TimelineCanvas.Children.Add(line);
      }
    }
    private void DrawTimeline() {
      if(timeline == null && pitchData.Count == 0) return;

      UpdateTimelineCanvasWidth(); // ensure canvas width accounts for pitch

      double pixelsPerSecond = 100;
      double laneHeight = timeline != null ?  TimelineCanvas.ActualHeight / 9 : TimelineCanvas.ActualHeight;

      // Remove only existing note rectangles
      for(int i = TimelineCanvas.Children.Count - 1; i >= 0; i--) {
        if(TimelineCanvas.Children[i] is Rectangle rect && rect.Tag?.ToString() == "note")
          TimelineCanvas.Children.RemoveAt(i);
      }

      if(timeline != null) {
        foreach(Note note in timeline.Notes) {
          var rect = new Rectangle {
            Width = Math.Max(1, note.Duration * pixelsPerSecond),
            Height = laneHeight - 1,
            Fill = Brushes.Cyan,
            Tag = "note"
          };

          Canvas.SetLeft(rect, note.Start * pixelsPerSecond);
          Canvas.SetTop(rect, (timeline.Lanes / 2 - note.Lane) * laneHeight);

          TimelineCanvas.Children.Add(rect);
        }
      }

      // Ensure playhead exists
      if(playheadLine == null)
        CreatePlayHead();

      // Update minimap
      DrawMinimap();
      DrawMinimapViewport();
    }
    private void DrawPitchGraph() {
      if(pitchData == null || pitchData.Count == 0) return;

      UpdateTimelineCanvasWidth(); // ensures canvas width is correct

      // Remove old pitch line
      for(int i = TimelineCanvas.Children.Count - 1; i >= 0; i--) {
        if(TimelineCanvas.Children[i] is Polyline pl && pl.Tag?.ToString() == "pitch")
          TimelineCanvas.Children.RemoveAt(i);
      }

      Polyline line = new Polyline {
        Stroke = Brushes.Orange,
        StrokeThickness = 1.5,
        Opacity = 0.7,
        Tag = "pitch"
      };

      double pixelsPerSecond = 100;
      double minFreq = 80, maxFreq = 1000;
      double pitchHeightFraction = 0.7;
      double pitchHeight = TimelineCanvas.ActualHeight * pitchHeightFraction;
      double topOffset = TimelineCanvas.ActualHeight * (1 - pitchHeightFraction);

      foreach(var sample in pitchData) {
        double freq = Math.Clamp(sample.Pitch, minFreq, maxFreq);
        double normalized = (freq - minFreq) / (maxFreq - minFreq);
        double y = topOffset + (1 - normalized) * pitchHeight;
        double x = sample.Time * pixelsPerSecond;

        line.Points.Add(new Point(x, y));
      }

      TimelineCanvas.Children.Add(line);

      DrawTimeline(); // redraw notes (if any) on top
    }
    private void DrawMinimap() {
      MinimapCanvas.Children.Clear();

      if(MinimapCanvas.ActualWidth <= 0)
        return;

      // Determine total timeline length (notes or pitch)
      double totalTimelineLength = Math.Max(
          timeline?.Length ?? 0,
          pitchData.LastOrDefault()?.Time ?? 0
      );

      if(totalTimelineLength <= 0)
        return;

      double minimapWidth = MinimapCanvas.ActualWidth;
      double minimapHeight = MinimapCanvas.ActualHeight;
      int lanes = timeline?.Lanes ?? 1;
      double laneHeight = minimapHeight / lanes;
      double scaleX = minimapWidth / totalTimelineLength;

      // Draw notes
      if(timeline != null) {
        foreach(Note note in timeline.Notes) {
          Rectangle rect = new() {
            Width = Math.Max(1, note.Duration * scaleX),
            Height = laneHeight - 1,
            Fill = Brushes.Cyan
          };

          double x = note.Start * scaleX;
          double y = (lanes / 2 - note.Lane) * laneHeight;

          Canvas.SetLeft(rect, x);
          Canvas.SetTop(rect, y);

          MinimapCanvas.Children.Add(rect);
        }
      }

      // Draw viewport rectangle
      minimapViewport = new Rectangle {
        Height = minimapHeight,
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0))
      };

      MinimapCanvas.Children.Add(minimapViewport);
      DrawMinimapViewport();
    }
    private void DrawMinimapViewport() {
      if(TimelineCanvas.Width <= 0) return;

      double minimapWidth = MinimapCanvas.ActualWidth;

      double totalTimelineWidth = TimelineCanvas.Width;
      double visibleWidth = TimelineScrollViewer.ViewportWidth;

      if(totalTimelineWidth <= 0 || visibleWidth <= 0) return;

      double viewportRatio = visibleWidth / totalTimelineWidth;
      double offsetRatio = TimelineScrollViewer.HorizontalOffset / totalTimelineWidth;

      double viewportWidth = minimapWidth * viewportRatio;
      double viewportX = minimapWidth * offsetRatio;

      minimapViewport.Width = viewportWidth;
      Canvas.SetLeft(minimapViewport, viewportX);
      Canvas.SetTop(minimapViewport, 0);
    }
    #endregion

    #region Timeline / Minimap Controls
    private void JumpToMinimapPosition(double mouseX) {
      double minimapWidth = MinimapCanvas.ActualWidth;
      if(minimapWidth <= 0) return;

      double ratio = mouseX / minimapWidth;
      ratio = Math.Clamp(ratio, 0, 1);

      ///double totalWidth = timeline.Length * 100;
      double totalWidth = TimelineCanvas.Width;
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
      double timelineWidth = TimelineScrollViewer.ActualWidth;
      double minimapWidth = minimapViewport.ActualWidth;

      Canvas.SetLeft(minimapViewport, e.HorizontalOffset / timelineWidth * minimapWidth);
      DrawTimeline();
      DrawPitchGraph();
    }
    #endregion

    #region Status Box Helpers
    private void UpdateStatusBox(string message, bool overwrite = false) {
      Dispatcher.Invoke(() => {
        if(overwrite)
          StatusTextBlock.Text = $"[{DateTime.Now:t}]: {message}\n";
        else
          StatusTextBlock.Text += $"[{DateTime.Now:t}]: {message}\n";
      });
    }
    private void ClearStatusBox() {
      StatusTextBlock.Text = "";
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

      [JsonPropertyName("configs")]
      public Configs Configs { get; set; } = new();

      [JsonPropertyName("notes")]
      public List<Note> Notes { get; set; } = new();
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
      public double Time { get; set; }   // seconds
      public double Pitch { get; set; }  // Hz or MIDI
    }
    #endregion
  }
}