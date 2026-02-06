namespace TimelineEditor
{
  public record GeneratorResult(
    bool Success,
    string? OutputPath,
    string? Error
  ) {
    public static GeneratorResult SuccessResult(string path)
      => new(true, path, null);

    public static GeneratorResult Fail(string error)
      => new(false, null, error);
  }
}
