using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Install only in Assets/Editor of a disposable recovery copy, never in clinic-int.
public static class UnifiedClinicArchiveDependencies
{
    [Serializable]
    public class SceneEntry
    {
        public string path;
        public string guid;
        public string[] dependencies;
    }

    [Serializable]
    public class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public string projectRoot;
        public SceneEntry[] scenes;
    }

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var outputIndex = Array.IndexOf(args, "-archiveAuditOutput");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length)
            throw new ArgumentException("Supply -archiveAuditOutput with an absolute output path.");
        var outputPath = args[outputIndex + 1];
        if (!Path.IsPathRooted(outputPath))
            throw new ArgumentException("Archive audit output must be absolute.");
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var scenes = Directory.GetFiles(Application.dataPath, "*.unity", SearchOption.AllDirectories)
            .Select(p => "Assets/" + Path.GetRelativePath(Application.dataPath, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new SceneEntry
            {
                path = p,
                guid = AssetDatabase.AssetPathToGUID(p),
                dependencies = AssetDatabase.GetDependencies(p, true)
                    .OrderBy(d => d, StringComparer.Ordinal).ToArray()
            }).ToArray();
        var report = new Report
        {
            generatedUtc = DateTime.UtcNow.ToString("o"),
            unityVersion = Application.unityVersion,
            projectRoot = projectRoot,
            scenes = scenes
        };
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
        Debug.Log("Archive dependency report: " + outputPath);
    }
}
