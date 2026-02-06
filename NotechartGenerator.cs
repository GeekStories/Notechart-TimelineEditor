using System.Diagnostics;
using System.IO;

namespace TimelineEditor
{
  public sealed class NotechartGenerator {

    public async Task<GeneratorResult> GenerateAsync(
      string audioFile,
      GeneratorSettings settings,
      IProgress<string>? progress = null
    ) {
      if(string.IsNullOrWhiteSpace(audioFile))
        return GeneratorResult.Fail("No audio file provided.");

      return await Task.Run(() => Run(audioFile, settings, progress));
    }

    private GeneratorResult Run(
      string audioFile,
      GeneratorSettings settings,
      IProgress<string>? progress
    ) {
      try {
        progress?.Report("Starting pitch extraction.");

        var psi = new ProcessStartInfo {
          FileName = "notechart",
          Arguments = BuildArguments(audioFile, settings),
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;

        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if(process.ExitCode != 0) {
          return GeneratorResult.Fail(stderr);
        }

        progress?.Report("Pitch extraction finished.");

        string jsonPath = Path.Combine(
          Path.GetDirectoryName(audioFile)!,
          Path.GetFileNameWithoutExtension(audioFile) + "_chart.json"
        );

        return File.Exists(jsonPath)
          ? GeneratorResult.SuccessResult(jsonPath)
          : GeneratorResult.Fail("Output file not found.");
      } catch(Exception ex) {
        return GeneratorResult.Fail(ex.Message);
      }
    }

    private static string BuildArguments(string audioFile, GeneratorSettings cfg) {
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
  }
}
