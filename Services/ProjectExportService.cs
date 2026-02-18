using System.IO;
using System.IO.Compression;
using System.Text.Json;
using TimelineEditor.Models;

namespace TimelineEditor.Services {
  public class ProjectExportService {
    private readonly FFmpegService _ffmpegService;

    public ProjectExportService() {
      _ffmpegService = new FFmpegService();
    }

    public async Task<ExportResult> ExportProjectAsync(
      Timeline timeline,
      string rawAudioPath,
      string outputRgpPath,
      IProgress<string>? progress = null) {
      
      if(timeline == null || timeline.Notes.Count == 0) {
        return new ExportResult {
          Success = false,
          Error = "No notes to export."
        };
      }

      if(string.IsNullOrEmpty(rawAudioPath) || !File.Exists(rawAudioPath)) {
        return new ExportResult {
          Success = false,
          Error = "Raw audio file not found. Please load a raw audio file first."
        };
      }

      try {
        progress?.Report("Starting project export...");

        // Create temporary directory for export
        string tempDir = Path.Combine(Path.GetTempPath(), $"RGP_Export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try {
          // Convert audio to OGG
          progress?.Report("Converting audio to OGG format...");
          string tempOggPath = Path.Combine(tempDir, "song.ogg");
          
          var conversionResult = await _ffmpegService.ConvertToOggAsync(
            rawAudioPath, 
            tempOggPath, 
            progress
          );

          if(!conversionResult.Success) {
            return new ExportResult {
              Success = false,
              Error = conversionResult.Error
            };
          }

          // Serialize timeline to JSON
          progress?.Report("Preparing notechart data...");
          string notechartJson = JsonSerializer.Serialize(timeline, new JsonSerializerOptions { 
            WriteIndented = true 
          });
          string tempNotechartPath = Path.Combine(tempDir, "notechart.json");
          await File.WriteAllTextAsync(tempNotechartPath, notechartJson);

          // Create metadata file
          progress?.Report("Creating metadata...");
          var metadata = new ProjectMetadata {
            Version = "1.0",
            ExportDate = DateTime.UtcNow,
            SongName = timeline.Name,
            NoteCount = timeline.Notes.Count,
            Lanes = timeline.Lanes,
            Duration = timeline.Length
          };
          string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { 
            WriteIndented = true 
          });
          string tempMetadataPath = Path.Combine(tempDir, "metadata.json");
          await File.WriteAllTextAsync(tempMetadataPath, metadataJson);

          // Package into .rgp file (ZIP archive)
          progress?.Report("Packaging files into .rgp archive...");
          
          // Delete existing file if it exists
          if(File.Exists(outputRgpPath)) {
            File.Delete(outputRgpPath);
          }

          // Create ZIP archive
          ZipFile.CreateFromDirectory(tempDir, outputRgpPath, CompressionLevel.Optimal, false);

          progress?.Report($"Export completed successfully: {Path.GetFileName(outputRgpPath)}");

          return new ExportResult {
            Success = true,
            OutputPath = outputRgpPath
          };

        } finally {
          // Cleanup temporary directory
          try {
            if(Directory.Exists(tempDir)) {
              Directory.Delete(tempDir, true);
            }
          } catch {
            // Ignore cleanup errors
          }
        }
      } catch(Exception ex) {
        return new ExportResult {
          Success = false,
          Error = $"Export failed: {ex.Message}"
        };
      }
    }

    public bool IsFFmpegAvailable() => _ffmpegService.IsFFmpegAvailable();
  }

  public class ExportResult {
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
  }

  public class ProjectMetadata {
    public string Version { get; set; } = "1.0";
    public DateTime ExportDate { get; set; }
    public string SongName { get; set; } = "";
    public int NoteCount { get; set; }
    public int Lanes { get; set; }
    public double Duration { get; set; }
  }
}