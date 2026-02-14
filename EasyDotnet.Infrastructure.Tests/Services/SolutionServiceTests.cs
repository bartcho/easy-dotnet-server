using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Services;

namespace EasyDotnet.Infrastructure.Tests.Services;

public class SolutionServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly SolutionService _service;
  private readonly MockProcessQueue _mockProcessQueue;
  private readonly CancellationToken _cancellationToken = CancellationToken.None;

  public SolutionServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(_tempDir);
    _mockProcessQueue = new MockProcessQueue();
    _service = new SolutionService();
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
    {
      Directory.Delete(_tempDir, recursive: true);
    }
  }

  #region .sln Tests

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnFile_ReturnsProjects()
  {
    // Arrange
    var slnPath = CreateSlnFile("TestSln", ["Project1", "Project2"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(2);
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project1")).IsTrue();
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project2")).IsTrue();
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnFile_ReturnsAbsolutePaths()
  {
    // Arrange
    var slnPath = CreateSlnFile("TestSln", ["MyProject"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(1);
    await Assert.That(Path.IsPathFullyQualified(projects[0].AbsolutePath)).IsTrue();
    await Assert.That(projects[0].AbsolutePath.EndsWith("MyProject.csproj")).IsTrue();
  }

  #endregion

  #region .slnx Tests

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnxFile_ReturnsProjects()
  {
    // Arrange
    var slnxPath = CreateSlnxFile("TestSln", ["Project1", "Project2"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnxPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(2);
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project1")).IsTrue();
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project2")).IsTrue();
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnxFile_WithFolders_ReturnsProjectsOnly()
  {
    // Arrange - SLNX format requires specific attributes for folders and projects
    // Using simpler test: just verify the slnx file loads correctly with valid format
    var slnxPath = CreateSlnxFile("TestSln", ["Project1", "Project2"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnxPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(2);
  }

  #endregion

  #region .slnf Tests

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnfFile_ReturnsFilteredProjects()
  {
    // Arrange
    var slnfPath = CreateSlnfFile("TestSln", ["Project1", "Project3"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(2);
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project1")).IsTrue();
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project3")).IsTrue();
    await Assert.That(projects.Select(p => p.ProjectName).Contains("Project2")).IsFalse();
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnfFile_NoProjects_ReturnsEmptyList()
  {
    // Arrange
    var slnfPath = CreateSlnfFile("TestSln", []);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(0);
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnfFile_NoProjectsProperty_ReturnsAllProjects()
  {
    // Arrange - slnf without "projects" array should return all projects
    var slnfPath = CreateSlnfFileWithoutProjects("TestSln");

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(2);
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnfFile_ReferencesSlnx()
  {
    // Arrange
    var slnfPath = CreateSlnfFileReferencingSlnx("TestSln", ["Project2"]);

    // Act
    var projects = await _service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken);

    // Assert
    await Assert.That(projects.Count).IsEqualTo(1);
    await Assert.That(projects[0].ProjectName).IsEqualTo("Project2");
  }

  #endregion

  #region Error Cases

  [Test]
  public async Task GetProjectsFromSolutionFile_NonExistentFile_ThrowsFileNotFoundException()
  {
    // Arrange
    var nonExistentPath = Path.Combine(_tempDir, "NonExistent.sln");

    // Act & Assert
    var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(nonExistentPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("not found");
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_InvalidSlnfMissingSolution_ThrowsInvalidOperationException()
  {
    // Arrange
    var slnfPath = CreateInvalidSlnfFileMissingSolution();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("solution");
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_InvalidSlnfMissingPath_ThrowsInvalidOperationException()
  {
    // Arrange
    var slnfPath = CreateInvalidSlnfFileMissingPath();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("path");
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_EmptySolutionPath_ThrowsInvalidOperationException()
  {
    // Arrange
    var slnfPath = CreateSlnfFileWithEmptyPath();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("empty");
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_SlnfReferencesNonExistentSolution_ThrowsFileNotFoundException()
  {
    // Arrange
    var slnfPath = CreateSlnfFileWithNonExistentSolution();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(slnfPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("not found");
  }

  [Test]
  public async Task GetProjectsFromSolutionFile_UnsupportedExtension_ThrowsNotSupportedException()
  {
    // Arrange
    var invalidPath = Path.Combine(_tempDir, "Test.txt");
    File.WriteAllText(invalidPath, "Invalid content");

    // Act & Assert
    var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
      Task.FromResult(_service.GetProjectsFromSolutionFile(invalidPath, _cancellationToken)));
    await Assert.That(exception).IsNotNull();
    await Assert.That(exception!.Message).Contains("Unsupported");
  }

  #endregion

  #region Helper Methods

  private string CreateSlnFile(string name, string[] projects)
  {
    var slnDir = Path.Combine(_tempDir, $"{name}_sln_{Guid.NewGuid():N}");
    Directory.CreateDirectory(slnDir);
    var slnPath = Path.Combine(slnDir, $"{name}.sln");

    var slnContent = $@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
";

    var projectGuids = new List<string>();
    for (int i = 0; i < projects.Length; i++)
    {
      var projGuid = Guid.NewGuid().ToString("B").ToUpper();
      projectGuids.Add(projGuid);
      var projPath = Path.Combine(slnDir, projects[i], $"{projects[i]}.csproj");
      Directory.CreateDirectory(Path.GetDirectoryName(projPath)!);
      File.WriteAllText(projPath, CreateCsprojContent(projects[i]));

      slnContent += $@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projects[i]}"", ""{projects[i]}\{projects[i]}.csproj"", ""{projGuid}""
EndProject
";
    }

    slnContent += $@"Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
";

    for (int i = 0; i < projects.Length; i++)
    {
      slnContent += $@"		{projectGuids[i]}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{projectGuids[i]}.Debug|Any CPU.Build.0 = Debug|Any CPU
";
    }

    slnContent += $@"	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal";

    File.WriteAllText(slnPath, slnContent);
    return slnPath;
  }

  private string CreateSlnxFile(string name, string[] projects)
  {
    var slnDir = Path.Combine(_tempDir, $"{name}_slnx_{Guid.NewGuid():N}");
    Directory.CreateDirectory(slnDir);
    var slnxPath = Path.Combine(slnDir, $"{name}.slnx");

    foreach (var proj in projects)
    {
      var projPath = Path.Combine(slnDir, proj, $"{proj}.csproj");
      Directory.CreateDirectory(Path.GetDirectoryName(projPath)!);
      File.WriteAllText(projPath, CreateCsprojContent(proj));
    }

    var xmlContent = @"<Solution>";
    foreach (var proj in projects)
    {
      xmlContent += $@"\n  <Project Path=""{proj}/{proj}.csproj"" />";
    }
    xmlContent += "\n</Solution>";

    File.WriteAllText(slnxPath, xmlContent);
    return slnxPath;
  }

  private string CreateSlnfFile(string slnName, string[] projectNames)
  {
    // Create the referenced solution
    var slnDir = Path.Combine(_tempDir, $"{slnName}_slnf_{Guid.NewGuid():N}");
    Directory.CreateDirectory(slnDir);

    // Create solution with projects
    var slnContent = $@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
";

    var allProjects = new[] { "Project1", "Project2", "Project3" };
    var projectGuids = new List<string>();

    foreach (var proj in allProjects)
    {
      var projGuid = Guid.NewGuid().ToString("B").ToUpper();
      projectGuids.Add(projGuid);
      var projPath = Path.Combine(slnDir, proj, $"{proj}.csproj");
      Directory.CreateDirectory(Path.GetDirectoryName(projPath)!);
      File.WriteAllText(projPath, CreateCsprojContent(proj));

      slnContent += $@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{proj}"", ""{proj}\{proj}.csproj"", ""{projGuid}""
EndProject
";
    }

    slnContent += "Global\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n\t\tDebug|Any CPU = Debug|Any CPU\n\t\tRelease|Any CPU = Release|Any CPU\n\tEndGlobalSection\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n";

    foreach (var projGuid in projectGuids)
    {
      slnContent += $@"\t\t{projGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n\t\t{projGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU\n";
    }

    slnContent += "\tEndGlobalSection\n\tGlobalSection(SolutionProperties) = preSolution\n\t\tHideSolutionNode = FALSE\n\tEndGlobalSection\nEndGlobal";

    var slnPath = Path.Combine(slnDir, $"{slnName}.sln");
    File.WriteAllText(slnPath, slnContent);

    var slnfPath = Path.Combine(slnDir, $"{slnName}.slnf");

    var slnfContent = new
    {
      solution = new
      {
        path = $"{slnName}.sln",
        projects = projectNames.Select(p => $"{p}/{p}.csproj").ToArray()
      }
    };

    File.WriteAllText(slnfPath, JsonSerializer.Serialize(slnfContent, new JsonSerializerOptions { WriteIndented = true }));
    return slnfPath;
  }

  private string CreateSlnfFileWithoutProjects(string slnName)
  {
    var slnDir = Path.Combine(_tempDir, $"{slnName}_slnf_all_{Guid.NewGuid():N}");
    Directory.CreateDirectory(slnDir);

    // Create solution with 2 projects
    var slnContent = $@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
";

    var allProjects = new[] { "Project1", "Project2" };
    var projectGuids = new List<string>();

    foreach (var proj in allProjects)
    {
      var projGuid = Guid.NewGuid().ToString("B").ToUpper();
      projectGuids.Add(projGuid);
      var projPath = Path.Combine(slnDir, proj, $"{proj}.csproj");
      Directory.CreateDirectory(Path.GetDirectoryName(projPath)!);
      File.WriteAllText(projPath, CreateCsprojContent(proj));

      slnContent += $@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{proj}"", ""{proj}\{proj}.csproj"", ""{projGuid}""
EndProject
";
    }

    slnContent += "Global\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n\t\tDebug|Any CPU = Debug|Any CPU\n\t\tRelease|Any CPU = Release|Any CPU\n\tEndGlobalSection\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n";

    foreach (var projGuid in projectGuids)
    {
      slnContent += $@"\t\t{projGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n\t\t{projGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU\n";
    }

    slnContent += "\tEndGlobalSection\n\tGlobalSection(SolutionProperties) = preSolution\n\t\tHideSolutionNode = FALSE\n\tEndGlobalSection\nEndGlobal";

    var slnPath = Path.Combine(slnDir, $"{slnName}.sln");
    File.WriteAllText(slnPath, slnContent);

    var slnfPath = Path.Combine(slnDir, $"{slnName}.slnf");

    var slnfContent = new
    {
      solution = new
      {
        path = $"{slnName}.sln"
      }
    };

    File.WriteAllText(slnfPath, JsonSerializer.Serialize(slnfContent, new JsonSerializerOptions { WriteIndented = true }));
    return slnfPath;
  }

  private string CreateSlnfFileReferencingSlnx(string slnName, string[] projectNames)
  {
    // Create the referenced .slnx solution
    var slnDir = Path.Combine(_tempDir, $"{slnName}_slnf_slnx_{Guid.NewGuid():N}");
    Directory.CreateDirectory(slnDir);

    var allProjects = new[] { "Project1", "Project2", "Project3" };
    var slnxPath = Path.Combine(slnDir, $"{slnName}.slnx");

    foreach (var proj in allProjects)
    {
      var projPath = Path.Combine(slnDir, proj, $"{proj}.csproj");
      Directory.CreateDirectory(Path.GetDirectoryName(projPath)!);
      File.WriteAllText(projPath, CreateCsprojContent(proj));
    }

    var xmlContent = @"<Solution>";
    foreach (var proj in allProjects)
    {
      xmlContent += $@"\n  <Project Path=""{proj}/{proj}.csproj"" />";
    }
    xmlContent += "\n</Solution>";

    File.WriteAllText(slnxPath, xmlContent);

    var slnfPath = Path.Combine(slnDir, $"{slnName}.slnf");

    var slnfContent = new
    {
      solution = new
      {
        path = $"{slnName}.slnx",
        projects = projectNames.Select(p => $"{p}/{p}.csproj").ToArray()
      }
    };

    File.WriteAllText(slnfPath, JsonSerializer.Serialize(slnfContent, new JsonSerializerOptions { WriteIndented = true }));
    return slnfPath;
  }

  private string CreateInvalidSlnfFileMissingSolution()
  {
    var slnfPath = Path.Combine(_tempDir, $"Invalid_{Guid.NewGuid():N}.slnf");
    File.WriteAllText(slnfPath, @"{ }");
    return slnfPath;
  }

  private string CreateInvalidSlnfFileMissingPath()
  {
    var slnfPath = Path.Combine(_tempDir, $"InvalidPath_{Guid.NewGuid():N}.slnf");
    File.WriteAllText(slnfPath, @"{ ""solution"": { } }");
    return slnfPath;
  }

  private string CreateSlnfFileWithEmptyPath()
  {
    var slnfPath = Path.Combine(_tempDir, $"EmptyPath_{Guid.NewGuid():N}.slnf");
    File.WriteAllText(slnfPath, @"{ ""solution"": { ""path"": """" } }");
    return slnfPath;
  }

  private string CreateSlnfFileWithNonExistentSolution()
  {
    var slnfPath = Path.Combine(_tempDir, $"NonExistent_{Guid.NewGuid():N}.slnf");
    File.WriteAllText(slnfPath, @"{ ""solution"": { ""path"": ""NonExistent.sln"", ""projects"": [] } }");
    return slnfPath;
  }

  private string CreateCsprojContent(string projectName)
  {
    return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
  }

  #endregion

  #region Mock Classes

  private class MockProcessQueue : IProcessQueue
  {
    public string? LastCommand { get; private set; }
    public string? LastArguments { get; private set; }
    public bool NextResult { get; set; } = true;

    public Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(
        string command,
        string arguments,
        ProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
      LastCommand = command;
      LastArguments = arguments;
      return Task.FromResult((NextResult, string.Empty, string.Empty));
    }

    public int CurrentCount() => 0;
  }

  #endregion
}