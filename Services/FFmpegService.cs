using System.Diagnostics;
using System.IO;

namespace TimelineEditor.Services {
  public class FFmpegService {
    public bool IsFFmpegAvailable() {
      try {
        var process = new Process {
          StartInfo = new ProcessStartInfo {
            FileName = Properties.Settings.Default.ffmpeg,
            Arguments = "-version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
          }
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
      } catch {
        return false;
      }
    }
    public async Task<ConversionResult> ConvertToOggAsync(
      string inputPath, 
      string outputPath, 
      IProgress<string>? progress = null) {
      
      if(!File.Exists(inputPath)) {
        return new ConversionResult {
          Success = false,
          Error = $"Input file not found: {inputPath}"
        };
      }

      if(!IsFFmpegAvailable()) {
        return new ConversionResult {
          Success = false,
          Error = "FFmpeg is not installed or not found in PATH. Please install FFmpeg."
        };
      }

      try {
        // Create output directory if it doesn't exist
        var outputDir = Path.GetDirectoryName(outputPath);
        if(!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) {
          Directory.CreateDirectory(outputDir);
        }

        progress?.Report("Converting audio to OGG format...");

        var processInfo = new ProcessStartInfo {
          FileName = Properties.Settings.Default.ffmpeg,
          Arguments = $"-i \"{inputPath}\" -c:a libvorbis -q:a 6 -y \"{outputPath}\"",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        
        var errorOutput = string.Empty;
        process.ErrorDataReceived += (sender, e) => {
          if(!string.IsNullOrEmpty(e.Data)) {
            errorOutput += e.Data + "\n";
            progress?.Report($"FFmpeg: {e.Data}");
          }
        };

        process.Start();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();

        if(process.ExitCode == 0 && File.Exists(outputPath)) {
          progress?.Report("Audio conversion completed successfully.");
          return new ConversionResult {
            Success = true,
            OutputPath = outputPath
          };
        } else {
          return new ConversionResult {
            Success = false,
            Error = $"FFmpeg conversion failed with exit code {process.ExitCode}.\n{errorOutput}"
          };
        }
      } catch(Exception ex) {
        return new ConversionResult {
          Success = false,
          Error = $"Error during conversion: {ex.Message}"
        };
      }
    }
  }

  public class ConversionResult {
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
  }
}