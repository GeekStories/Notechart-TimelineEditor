using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TimelineEditor.Models;
using TimelineEditor.ViewModels;

namespace TimelineEditor {
  public partial class MainWindow : Window {
    enum DragMode {
      None,
      Move,
      ResizeLeft,
      ResizeRight
    }

    private MainViewModel VM => (MainViewModel)DataContext;
    
    // Hover/Preview state
    private Note? hoveredNote = null;
    private double splitPreviewTime = 0;
    private Line? splitPreviewLine = null;
    
    // Layout
    private double LanePixelHeight => NotesCanvas.ActualHeight / VM.Timeline.Lanes;

    // Playback visuals
    private Line? playheadLine;
    private TranslateTransform playheadTransform = new();

    // Dragging state
    DragMode dragMode = DragMode.None;
    private Rectangle? draggedNoteRect;
    private Note? draggedNote;
    private Note? originalNote;
    double originalLeft;
    double originalWidth;
    private Point dragStartMouse;
    private double dragStartX;
    private double dragStartY;

    // Minimap
    private Rectangle? minimapViewport;
    private bool isDraggingMinimap = false;

    // Services
    private SettingsWindow SettingsWindow = new();

    public MainWindow() {
      InitializeComponent();

      var vm = new MainViewModel();
      DataContext = vm;

      vm.DrawTimelineRequested += DrawTimeline;
      vm.PropertyChanged += (s, e) => {
        if(e.PropertyName == nameof(VM.VisualTime))
          UpdatePlayhead();
      };

      TimelineGrid.SizeChanged += (_, __) => {
        DrawTimeGrid();
        DrawNotes();
        DrawPitchGraph();
        CreatePlayHead();
        DrawLyrics();
      };

      MinimapCanvas.SizeChanged += (s, e) => DrawMinimap();
      CompositionTarget.Rendering += OnRendering;

      DrawTimeGrid();
    }

    protected override void OnClosed(EventArgs e) {
      Application.Current.Shutdown();
      base.OnClosed(e);
    }

    private void OnRendering(object? sender, EventArgs e) {
      VM.UpdateVisualTime();
    }

    #region Input Events
    private void Window_KeyUp(object sender, KeyEventArgs e) {
      if(e.Key == Key.S)
        TrySplitHoveredNote();
      else if(e.Key == Key.Space) {
        if(VM.IsPlaying)
          VM.Stop();
        else
          VM.Play();
        e.Handled = true;
      } else if(e.Key == Key.R)
        VM.Reset();
    }

    private void Note_MouseDown(object sender, MouseButtonEventArgs e) {
      if(sender is not Rectangle rect) return;
      if(rect.Tag is not Note note) return;

      originalNote = new Note {
        Start = note.Start,
        Lane = note.Lane,
        Duration = note.Duration,
        Type = note.Type
      };

      originalLeft = Canvas.GetLeft(rect);
      originalWidth = rect.Width;

      draggedNoteRect = rect;
      draggedNote = note;

      dragStartMouse = e.GetPosition(NotesCanvas);
      dragStartX = Canvas.GetLeft(rect);
      dragStartY = Canvas.GetTop(rect);

      Point localPos = e.GetPosition(rect);

      if(localPos.X <= MainViewModel.ResizeHandleWidth) {
        dragMode = DragMode.ResizeLeft;
      } else if(localPos.X >= rect.Width - MainViewModel.ResizeHandleWidth) {
        dragMode = DragMode.ResizeRight;
      } else {
        dragMode = DragMode.Move;
      }

      rect.CaptureMouse();
      e.Handled = true;
    }

    private void Note_MouseMove(object sender, MouseEventArgs e) {
      if(draggedNoteRect == null || draggedNote == null) return;
      if(e.LeftButton != MouseButtonState.Pressed) return;

      Point pos = e.GetPosition(NotesCanvas);
      Vector delta = pos - dragStartMouse;

      if(dragMode == DragMode.Move) {
        MoveNote(delta);
      } else if(dragMode == DragMode.ResizeLeft) {
        ResizeLeft(delta);
      } else if(dragMode == DragMode.ResizeRight) {
        ResizeRight(delta);
      }

      UpdateMinimapViewport();
    }

    private void Note_MouseUp(object sender, MouseButtonEventArgs e) {
      if(draggedNoteRect == null || draggedNote == null) return;

      draggedNoteRect.ReleaseMouseCapture();

      bool ok = true;
      if(dragMode == DragMode.Move) {
        ok = VM.TrySnapNoteToTimeline(draggedNote);
      }

      if(!ok) {
        draggedNote.Start = originalNote!.Start;
        draggedNote.Lane = originalNote.Lane;
        draggedNote.Duration = originalNote.Duration;
      }

      dragMode = DragMode.None;
      draggedNoteRect = null;
      draggedNote = null;
      originalNote = null;

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
      double time = pos.X / MainViewModel.PixelsPerSecond;

      splitPreviewTime = Math.Clamp(
        time,
        hoveredNote.Start + 0.001,
        hoveredNote.Start + hoveredNote.Duration - 0.001
      );

      DrawSplitPreviewLine(hoveredNote, splitPreviewTime);
    }

    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e) {
      if(e.ChangedButton == MouseButton.Left) {
        if(hoveredNote != null) return;

        double time = e.GetPosition(NotesCanvas).X / MainViewModel.PixelsPerSecond;
        VM.SeekTo(time);
        UpdatePlayhead();
      } else if(e.ChangedButton == MouseButton.Middle) {
        int lane = VM.Timeline.Lanes - 1 - (int)(e.GetPosition(NotesCanvas).Y / LanePixelHeight);
        double time = e.GetPosition(NotesCanvas).X / MainViewModel.PixelsPerSecond;
        VM.AddNoteCommand.Execute(Tuple.Create(lane, time));
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
      TimelineScrollViewer.ScrollToHorizontalOffset(
        TimelineScrollViewer.HorizontalOffset - e.Delta
      );
      e.Handled = true;
    }

    public void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) {
      DrawPitchGraph();
      UpdateMinimapViewport();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) {
      if(SettingsWindow.IsVisible) {
        SettingsWindow.Focus();
      } else {
        SettingsWindow = new SettingsWindow();
        SettingsWindow.Show();
      }
    }
    #endregion

    #region Note Manipulation (View Logic Only)
    private void TrySplitHoveredNote() {
      if(hoveredNote == null) return;
      VM.SplitNoteCommand.Execute(Tuple.Create(hoveredNote, splitPreviewTime));
      ClearSplitPreview();
    }

    private void DrawSplitPreviewLine(Note note, double splitTime) {
      ClearSplitPreview();

      double x = splitTime * MainViewModel.PixelsPerSecond;
      double yTop = (VM.Timeline.Lanes - 1 - note.Lane) * LanePixelHeight;
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

    private Note? GetNoteAtPosition(Point pos) {
      var (time, lane) = MouseToTimeline(pos);

      const double TimeTolerancePixels = 5;
      double timeTol = TimeTolerancePixels / MainViewModel.PixelsPerSecond;

      return VM.Timeline.Notes
        .Where(n => n.Lane == lane)
        .FirstOrDefault(n =>
          time >= n.Start - timeTol &&
          time <= n.Start + n.Duration + timeTol
        );
    }

    private void MoveNote(Vector delta) {
      if(draggedNote == null || draggedNoteRect == null) return;

      double newX = Math.Max(0, dragStartX + delta.X);
      Canvas.SetLeft(draggedNoteRect, newX);
      draggedNote.Start = Math.Round(newX / MainViewModel.PixelsPerSecond, 3);

      double newY = dragStartY + delta.Y;
      double clampedY = Math.Clamp(newY, 0, NotesCanvas.ActualHeight);
      double laneFloat = (clampedY + LanePixelHeight / 2) / LanePixelHeight;
      int laneIndex = (int)Math.Floor(laneFloat);
      int newLane = (VM.Timeline.Lanes - 1) - laneIndex;
      newLane = Math.Clamp(newLane, 0, VM.Timeline.Lanes - 1);
      draggedNote.Lane = newLane;
      Canvas.SetTop(draggedNoteRect, LaneToY(newLane, LanePixelHeight));
    }

    private void ResizeRight(Vector delta) {
      double newWidth = originalWidth + delta.X;

      double minWidth = MainViewModel.MinNoteDuration * MainViewModel.PixelsPerSecond;
      double maxRight = GetGlobalRightBound(draggedNote!) * MainViewModel.PixelsPerSecond;
      double leftPx = originalLeft;

      double maxWidth = maxRight - leftPx;
      newWidth = Math.Clamp(newWidth, minWidth, maxWidth);

      draggedNoteRect!.Width = newWidth;
      draggedNote!.Duration = Math.Round(newWidth / MainViewModel.PixelsPerSecond, 3);
    }

    private void ResizeLeft(Vector delta) {
      double newLeft = originalLeft + delta.X;

      double minWidth = MainViewModel.MinNoteDuration * MainViewModel.PixelsPerSecond;
      double maxLeft = GetGlobalLeftBound(draggedNote!) * MainViewModel.PixelsPerSecond;

      newLeft = Math.Max(newLeft, maxLeft);

      double rightEdge = originalLeft + originalWidth;
      double newWidth = rightEdge - newLeft;

      if(newWidth < minWidth) {
        newWidth = minWidth;
        newLeft = rightEdge - newWidth;
      }

      Canvas.SetLeft(draggedNoteRect!, newLeft);
      draggedNoteRect!.Width = newWidth;

      draggedNote!.Start = Math.Round(newLeft / MainViewModel.PixelsPerSecond, 3);
      draggedNote.Duration = Math.Round(newWidth / MainViewModel.PixelsPerSecond, 3);
    }

    private double GetGlobalLeftBound(Note note) {
      var prev = VM.Timeline.Notes
        .Where(n => n != note && n.Lane == note.Lane && n.Start + n.Duration <= note.Start)
        .OrderByDescending(n => n.Start + n.Duration)
        .FirstOrDefault();

      return prev != null ? prev.Start + prev.Duration : 0;
    }

    private double GetGlobalRightBound(Note note) {
      var next = VM.Timeline.Notes
        .Where(n => n != note && n.Lane == note.Lane && n.Start >= note.Start + note.Duration)
        .OrderBy(n => n.Start)
        .FirstOrDefault();

      return next != null ? next.Start : VM.TimelineWidthSeconds;
    }
    #endregion

    #region Helpers
    private (double time, int lane) MouseToTimeline(Point pos) {
      double time = pos.X / MainViewModel.PixelsPerSecond;
      int lane = VM.Timeline.Lanes - 1 - (int)(pos.Y / LanePixelHeight);
      return (time, lane);
    }

    private static SolidColorBrush NoteColor(string type) {
      return type switch {
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
        RenderTransform = playheadTransform
      };

      PlayheadCanvas.SizeChanged += (_, _) => UpdatePlayheadHeight();
      PlayheadCanvas.Children.Add(playheadLine);
    }

    private void UpdatePlayhead() {
      playheadTransform.X = TimeToCanvasX(VM.VisualTime);
    }
    #endregion

    #region Timeline/Minimap Rendering
    private void SetupTimelineForAudio() {
      PitchCanvas.Width = LyricsCanvas.Width = NotesCanvas.Width = PlayheadCanvas.Width =
        VM.TimelineWidthSeconds * MainViewModel.PixelsPerSecond;
    }

    private void InitMinimapViewport() {
      minimapViewport = new Rectangle {
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
        IsHitTestVisible = false,
        Height = MinimapCanvas.ActualHeight
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
      if(VM.Timeline.Notes.Count == 0) return;
      NotesCanvas.Children.Clear();

      foreach(var note in VM.Timeline.Notes) {
        SolidColorBrush fillBrush = NoteColor(note.Type);
        var strokeBrush = DarkenBrush(fillBrush, 0.6);

        Rectangle rect = new() {
          Width = Math.Max(1, note.Duration * MainViewModel.PixelsPerSecond),
          Height = LanePixelHeight,
          Fill = fillBrush,
          Opacity = 0.55,
          Tag = note,
          IsHitTestVisible = true,
          Stroke = strokeBrush,
          StrokeThickness = 1,
        };

        Canvas.SetLeft(rect, note.Start * MainViewModel.PixelsPerSecond);
        Canvas.SetTop(rect, LaneToY(note.Lane, LanePixelHeight));

        rect.MouseRightButtonUp += (_, __) => VM.RemoveNoteCommand.Execute(note);
        rect.MouseLeftButtonDown += Note_MouseDown;
        rect.MouseMove += Note_MouseMove;
        rect.MouseLeftButtonUp += Note_MouseUp;
        rect.MouseMove += (_, e) => {
          var p = e.GetPosition(rect);
          if(p.X < MainViewModel.ResizeHandleWidth || p.X > rect.Width - MainViewModel.ResizeHandleWidth)
            rect.Cursor = Cursors.SizeWE;
          else
            rect.Cursor = Cursors.Hand;
        };

        NotesCanvas.Children.Add(rect);
      }
    }

    private void DrawPitchGraph() {
      PitchCanvas.Children.Clear();

      if(VM.Timeline.PitchSamples.Count == 0) return;

      var geometry = new PathGeometry();
      PathFigure? figure = null;
      PitchSample? prev = null;

      double viewStart = TimelineScrollViewer.HorizontalOffset;
      double viewEnd = viewStart + TimelineScrollViewer.ViewportWidth;

      foreach(var p in VM.Timeline.PitchSamples) {
        double x = TimeToCanvasX(p.Time);

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

      const double MaxTimeGap = 0.03;
      const double SoftMidiJump = 0.35;
      const double HardMidiJump = 0.75;

      double pitchVelocity = Math.Abs(curr.Midi - prev.Midi) / dt;
      const double MaxPitchVelocity = 25;

      if(dt > MaxTimeGap)
        return false;

      if(pitchVelocity > MaxPitchVelocity)
        return false;

      if(dm <= SoftMidiJump)
        return true;

      if(dm >= HardMidiJump)
        return false;

      return dt < MaxTimeGap * 0.5;
    }

    private static double TimeToCanvasX(double timeSeconds) {
      return timeSeconds * MainViewModel.PixelsPerSecond;
    }

    private double LaneToY(int lane, double laneHeight) {
      int laneIndex = (VM.Timeline.Lanes - 1) - lane;
      laneIndex = Math.Clamp(laneIndex, 0, VM.Timeline.Lanes - 1);
      return laneIndex * laneHeight;
    }

    private double MidiToY(double midi) {
      double minMidi = FrequencyToMidi(double.Parse(VM.MinFrequency));
      double maxMidi = FrequencyToMidi(double.Parse(VM.MaxFrequency));
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
      DrawNotes();
      CreatePlayHead();
      DrawLyrics();
      DrawMinimap();
    }

    private void DrawTimeGrid() {
      GridCanvas.Children.Clear();

      double height = TimelineGrid.ActualHeight;
      double width = TimelineGrid.ActualWidth;

      for(double t = 0; t < width / MainViewModel.PixelsPerSecond; t += 0.25) {
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

      double scaleX = minimapWidth / VM.TimelineWidthSeconds;
      double minimapLaneHeight = minimapHeight / VM.Timeline.Lanes;

      foreach(var note in VM.Timeline.Notes) {
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
      if(VM.Timeline.Lyrics.Count == 0) return;
      LyricsCanvas.Children.Clear();

      foreach(Lyric l in VM.Timeline.Lyrics) {
        if(string.IsNullOrWhiteSpace(l.Text)) continue;

        double width = Math.Max(1, (l.End - l.Start) * MainViewModel.PixelsPerSecond);

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
    #endregion
  }
}