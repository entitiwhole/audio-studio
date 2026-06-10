using System.IO;
using System.Text.Json;
using AudioStudio.Models;

namespace AudioStudio.Services;

public static class ProjectService
{
    public const string Extension = ".bfproj";
    private const string AudioSubfolder = "Audio";

    public static string ProjectsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BFStudio", "Projects");

    public static StudioProject CreateSnapshot(PlaylistViewModel model, IEnumerable<TrackItemViewModel> clips, string? name = null)
    {
        return new StudioProject
        {
            Name = name ?? "Untitled",
            Bpm = model.Bpm,
            NumTracks = model.NumTracks,
            Clips = clips.Select(c => new StudioProjectClip
            {
                Id = c.Id,
                Name = c.Name,
                FilePath = c.FilePath ?? "",
                StartTick = c.StartTick,
                DurationTicks = c.DurationTicks,
                TrackIndex = c.TrackIndex,
                SampleRate = c.SampleRate,
                Channels = c.Channels,
                SourceDurationSeconds = c.SourceDurationSeconds
            }).ToList()
        };
    }

    public static void Save(string projectFilePath, StudioProject project, bool copyAudioFiles)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException("Invalid project path");
        Directory.CreateDirectory(projectDir);

        var audioDir = Path.Combine(projectDir, AudioSubfolder);
        if (copyAudioFiles)
            Directory.CreateDirectory(audioDir);

        var saved = new StudioProject
        {
            Version = project.Version,
            Name = project.Name,
            Bpm = project.Bpm,
            NumTracks = project.NumTracks,
            Clips = new List<StudioProjectClip>()
        };

        foreach (var clip in project.Clips)
        {
            var entry = new StudioProjectClip
            {
                Id = clip.Id,
                Name = clip.Name,
                StartTick = clip.StartTick,
                DurationTicks = clip.DurationTicks,
                TrackIndex = clip.TrackIndex,
                SampleRate = clip.SampleRate,
                Channels = clip.Channels,
                SourceDurationSeconds = clip.SourceDurationSeconds
            };

            if (string.IsNullOrWhiteSpace(clip.FilePath) || !File.Exists(clip.FilePath))
            {
                entry.FilePath = clip.FilePath;
            }
            else if (copyAudioFiles)
            {
                string destName = Path.GetFileName(clip.FilePath);
                string destPath = Path.Combine(audioDir, destName);
                if (!File.Exists(destPath) || !PathsEqual(destPath, clip.FilePath))
                {
                    if (File.Exists(destPath))
                        destName = $"{Path.GetFileNameWithoutExtension(destName)}_{clip.Id:N}{Path.GetExtension(destName)}";
                    destPath = Path.Combine(audioDir, destName);
                    File.Copy(clip.FilePath, destPath, overwrite: true);
                }
                entry.FilePath = $"{AudioSubfolder}/{destName}".Replace('\\', '/');
            }
            else
            {
                entry.FilePath = GetRelativePath(projectDir, clip.FilePath);
            }

            saved.Clips.Add(entry);
        }

        var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(projectFilePath, json);
    }

    public static StudioProject Load(string projectFilePath)
    {
        var json = File.ReadAllText(projectFilePath);
        var project = JsonSerializer.Deserialize<StudioProject>(json)
            ?? throw new InvalidOperationException("Не удалось прочитать проект");

        var projectDir = Path.GetDirectoryName(projectFilePath) ?? "";
        foreach (var clip in project.Clips)
        {
            if (string.IsNullOrWhiteSpace(clip.FilePath))
                continue;

            if (!Path.IsPathRooted(clip.FilePath))
                clip.FilePath = Path.GetFullPath(Path.Combine(projectDir, clip.FilePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        return project;
    }

    public static string GetDefaultProjectPath(string projectName)
    {
        Directory.CreateDirectory(ProjectsDirectory);
        string safeName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Untitled";
        return Path.Combine(ProjectsDirectory, safeName + Extension);
    }

    private static string GetRelativePath(string baseDir, string fullPath)
    {
        if (!Path.IsPathRooted(fullPath))
            return fullPath;

        var baseUri = new Uri(AppendDirectorySeparator(baseDir));
        var pathUri = new Uri(fullPath);
        if (baseUri.IsBaseOf(pathUri))
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        return fullPath;
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar))
            return path + Path.DirectorySeparatorChar;
        return path;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
