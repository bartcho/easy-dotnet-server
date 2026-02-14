using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Solution;
using EasyDotnet.MsBuild;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace EasyDotnet.Infrastructure.Services;

public class SolutionService : ISolutionService
{
  public async Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken)
  {
    var fullSolutionPath = Path.GetFullPath(solutionFilePath);
    var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? throw new Exception("Solution dir cannot be null");
    var extension = Path.GetExtension(fullSolutionPath);

    // Handle solution filter files (.slnf) specially
    if (extension.Equals(FileTypes.SolutionFilterExtension, StringComparison.OrdinalIgnoreCase))
    {
      return GetProjectsFromSolutionFilter(fullSolutionPath);
    }

    var serializer = SolutionSerializers.GetSerializerByMoniker(fullSolutionPath) ?? throw new InvalidOperationException($"No serializer found for solution file: {fullSolutionPath}");
    var solutionModel = await serializer.OpenAsync(fullSolutionPath, cancellationToken);
    var solutionFolderType = Guid.Parse("2150E333-8FDC-42A3-9474-1A3956D46DE8");

    return [.. solutionModel.SolutionProjects
        .Where(p => p.TypeId != solutionFolderType)
        .Select(p =>
        {
          var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, p.FilePath));

          return new SolutionFileProject(
                ProjectName: p.ActualDisplayName,
                AbsolutePath: absolutePath
            );
        })];
  }

  public async Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken)
  {
    var serializer = SolutionSerializers.GetSerializerByMoniker(solutionFilePath);
    if (serializer == null) return false;

    var solutionModel = await serializer.OpenAsync(solutionFilePath, cancellationToken);

    var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? throw new Exception("Solution dir cannot be null");
    var relativePath = Path.GetRelativePath(solutionDirectory, projectPath);

    solutionModel.AddProject(relativePath);

    await serializer.SaveAsync(solutionFilePath, solutionModel, cancellationToken);

    return true;
  }

  private static List<SolutionFileProject> GetProjectsFromSolutionFilter(string slnfPath)
  {
    var slnfContent = File.ReadAllText(slnfPath);
    using var doc = JsonDocument.Parse(slnfContent);
    var root = doc.RootElement;

    if (!root.TryGetProperty("solution", out var solution))
    {
      throw new InvalidOperationException($"Invalid solution filter file (missing 'solution' property): {slnfPath}");
    }

    if (!solution.TryGetProperty("path", out var slnPath))
    {
      throw new InvalidOperationException($"Invalid solution filter file (missing 'solution.path' property): {slnfPath}");
    }

    var solutionDir = Path.GetDirectoryName(slnfPath) ?? Directory.GetCurrentDirectory();
    var solutionPathValue = slnPath.GetString();
    if (string.IsNullOrWhiteSpace(solutionPathValue))
    {
      throw new InvalidOperationException($"Invalid solution filter file (empty solution path): {slnfPath}");
    }

    var fullSlnPath = Path.GetFullPath(Path.Combine(solutionDir, solutionPathValue));

    if (!File.Exists(fullSlnPath))
    {
      throw new FileNotFoundException($"Referenced solution file not found: {fullSlnPath}", fullSlnPath);
    }

    // Load the referenced solution using the Microsoft library
    var serializer = SolutionSerializers.GetSerializerByMoniker(fullSlnPath)
      ?? throw new NotSupportedException($"Unsupported solution file format: {fullSlnPath}");
    var solutionModel = serializer.OpenAsync(fullSlnPath, CancellationToken.None)
      .GetAwaiter().GetResult()
      ?? throw new InvalidOperationException($"Failed to load solution: {fullSlnPath}");
    var allProjects = ExtractProjectsFromModel(solutionModel, fullSlnPath);

    // If no projects specified in filter, return all projects
    if (!solution.TryGetProperty("projects", out var projectGlobs))
    {
      return allProjects;
    }

    var slnfProjects = projectGlobs.EnumerateArray()
      .Select(p => p.GetString())
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .Select(p => Path.GetFullPath(Path.Combine(solutionDir, p!)))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    return [.. allProjects.Where(p => slnfProjects.Contains(p.AbsolutePath))];
  }

  private static List<SolutionFileProject> ExtractProjectsFromModel(SolutionModel solutionModel, string solutionPath)
  {
    var solutionDir = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();

    return [.. solutionModel.SolutionProjects
       .Select(p =>
       {
         var projectName = Path.GetFileNameWithoutExtension(p.FilePath) ?? p.DisplayName ?? "";
         var absolutePath = Path.GetFullPath(Path.Combine(solutionDir, p.FilePath));
         return new SolutionFileProject(projectName, absolutePath);
       })];
  }
}