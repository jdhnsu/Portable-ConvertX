using System.IO;

namespace ConvertXPortable.Services;

public sealed class PathResolver
{
    public PathResolver()
    {
        WorkspaceRoot = FindWorkspaceRoot();
        TestToolsRoot = Path.Combine(WorkspaceRoot, "TestTools");
        ToolsJsonPath = Path.Combine(TestToolsRoot, "tools.json");
        var workspaceConversions = Path.Combine(WorkspaceRoot, "conversions.json");
        var appConversions = Path.Combine(AppContext.BaseDirectory, "conversions.json");
        ConversionsJsonPath = File.Exists(workspaceConversions) ? workspaceConversions : appConversions;
    }

    public string WorkspaceRoot { get; }
    public string TestToolsRoot { get; }
    public string ToolsJsonPath { get; }
    public string ConversionsJsonPath { get; }

    public string ResolveToolPath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(TestToolsRoot, normalized));
    }

    private static string FindWorkspaceRoot()
    {
        var candidates = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var toolsJson = Path.Combine(directory.FullName, "TestTools", "tools.json");
                if (File.Exists(toolsJson))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }
}
