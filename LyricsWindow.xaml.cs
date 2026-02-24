using System.Windows;
using System.Windows.Input;
using TimelineEditor.Models;
using TimelineEditor.ViewModels;

namespace TimelineEditor {
  /// <summary>
  /// Interaction logic for LyricsWindow.xaml
  /// </summary>
  public partial class LyricsWindow : Window {
    public MainViewModel? ViewModel { get; set; }

    private List<string> lyricQueue = new();
    private int currentLyricIndex = 0;
    private const double DefaultLyricDuration = 5.0; // seconds

    public LyricsWindow() {
      InitializeComponent();
    }

    private void LoadLyricsButton_Click(object sender, RoutedEventArgs e) {
      string text = LyricsTextBox.Text;
      if(string.IsNullOrWhiteSpace(text)) {
        MessageBox.Show("Please enter lyrics to load.", "No Lyrics", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }

      var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      if(lines.Length == 0) {
        MessageBox.Show("No valid lyrics found.", "No Lyrics", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }

      // Load lyrics into queue
      lyricQueue = lines.Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
      currentLyricIndex = 0;

      UpdateUI();
      MessageBox.Show($"Loaded {lyricQueue.Count} lyric line(s).\nUse 'Place Next' or press Space/Enter to place lyrics at the playhead position.", 
                      "Lyrics Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PlaceNextButton_Click(object sender, RoutedEventArgs e) {
      PlaceNextLyric();
    }

    private void PlaceNextLyric() {
      if(ViewModel == null) return;
      if(currentLyricIndex >= lyricQueue.Count) return;

      // Check if vocal audio is loaded
      if(ViewModel.TimelineWidthSeconds <= 10) {
        MessageBox.Show("Please load vocal audio before placing lyrics.", "No Audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      // Place lyric at current playhead position
      double startTime = ViewModel.VisualTime;
      string lyricText = lyricQueue[currentLyricIndex];

      var lyric = new Lyric {
        Start = startTime,
        End = startTime + DefaultLyricDuration,
        Text = lyricText
      };

      ViewModel.Timeline.Lyrics.Add(lyric);
      currentLyricIndex++;

      // Update status
      ViewModel.LyricsFileLabel = $"Lyrics: {ViewModel.Timeline.Lyrics.Count} of {lyricQueue.Count} placed";
      ViewModel.StatusMessage = $"Placed lyric {currentLyricIndex}/{lyricQueue.Count}: \"{lyricText}\"";

      // Trigger timeline redraw
      ViewModel.RequestDrawTimeline();

      UpdateUI();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) {
      if(ViewModel == null) return;
      if(ViewModel.Timeline.Lyrics.Count == 0) return;
      if(currentLyricIndex <= 0) return;

      // Remove the last placed lyric
      var lastLyric = ViewModel.Timeline.Lyrics[ViewModel.Timeline.Lyrics.Count - 1];
      ViewModel.Timeline.Lyrics.RemoveAt(ViewModel.Timeline.Lyrics.Count - 1);
      currentLyricIndex--;

      // Update status
      ViewModel.LyricsFileLabel = $"Lyrics: {ViewModel.Timeline.Lyrics.Count} of {lyricQueue.Count} placed";
      ViewModel.StatusMessage = $"Removed lyric: \"{lastLyric.Text}\"";

      // Trigger timeline redraw
      ViewModel.RequestDrawTimeline();

      UpdateUI();
    }

    private void UpdateUI() {
      if(lyricQueue.Count == 0) {
        QueueStatusText.Text = "No lyrics loaded";
        CurrentLyricText.Text = "";
        PlaceNextButton.IsEnabled = false;
        UndoButton.IsEnabled = false;
      } else {
        if(currentLyricIndex >= lyricQueue.Count) {
          QueueStatusText.Text = $"All lyrics placed! ({lyricQueue.Count}/{lyricQueue.Count})";
          CurrentLyricText.Text = "✓ All lyrics have been placed on the timeline";
          PlaceNextButton.IsEnabled = false;
        } else {
          QueueStatusText.Text = $"Lyric {currentLyricIndex + 1} of {lyricQueue.Count}";
          CurrentLyricText.Text = $"Next: \"{lyricQueue[currentLyricIndex]}\"";
          PlaceNextButton.IsEnabled = true;
        }

        UndoButton.IsEnabled = ViewModel?.Timeline.Lyrics.Count > 0 && currentLyricIndex > 0;
      }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) {
      LyricsTextBox.Clear();
      lyricQueue.Clear();
      currentLyricIndex = 0;
      UpdateUI();
    }

    private void ClearAllLyricsButton_Click(object sender, RoutedEventArgs e) {
      if(ViewModel == null) return;

      if(ViewModel.Timeline.Lyrics.Count == 0 && lyricQueue.Count == 0) {
        MessageBox.Show("No lyrics to clear.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }

      var result = MessageBox.Show(
        $"Clear all lyrics from timeline ({ViewModel.Timeline.Lyrics.Count} placed) and reset queue?",
        "Confirm Clear",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

      if(result == MessageBoxResult.Yes) {
        ViewModel.Timeline.Lyrics.Clear();
        currentLyricIndex = 0;
        ViewModel.LyricsFileLabel = "Lyrics File: (none)";
        ViewModel.StatusMessage = "Cleared all lyrics from timeline.";

        // Trigger timeline redraw
        ViewModel.RequestDrawTimeline();

        UpdateUI();
      }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
      // Allow Space or Enter to place the next lyric
      if(e.Key == Key.Space || e.Key == Key.Return) {
        if(PlaceNextButton.IsEnabled) {
          PlaceNextLyric();
          e.Handled = true;
        }
      }
      // Allow Backspace to undo last lyric
      else if(e.Key == Key.Back) {
        if(UndoButton.IsEnabled) {
          UndoButton_Click(sender, e);
          e.Handled = true;
        }
      }
    }
  }
}
