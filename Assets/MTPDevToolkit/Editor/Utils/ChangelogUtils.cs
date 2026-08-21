using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text.RegularExpressions;
using UnityEditor;
using System.Text;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public static class ChangelogUtils
    {
        private const string CHANGELOG_FILE = "CHANGELOG.md";
        private static readonly Regex VersionRegex = new Regex(@"## \[(\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}");

        public static string ReadChangelog(string projectPath)
        {
            var changelogPath = GetChangelogPath(projectPath);
            if (string.IsNullOrEmpty(changelogPath))
                return null;

            if (File.Exists(changelogPath))
            {
                var content = File.ReadAllText(changelogPath);
                /*
                if (string.IsNullOrWhiteSpace(content) || VersionRegex.IsMatch(content))
                {
                    var changelog = GenerateChangelog(projectPath);
                    if (!string.IsNullOrEmpty(changelog))
                    {
                        return changelog;
                    }
                }
                */
                return content;
            }
            /*
            else
            {
                // Create and fill the changelog file if it doesn't exist
                var changelog = GenerateChangelog(projectPath);
                if (!string.IsNullOrEmpty(changelog))
                {
                    WriteChangelog(projectPath, changelog);
                    return changelog;
                }
            }
            */
            return null;
        }

        public static void WriteChangelog(string projectPath, string changelog)
        {
            if (string.IsNullOrEmpty(changelog))
                return;

            var changelogPath = GetChangelogPath(projectPath);
            if (string.IsNullOrEmpty(changelogPath))
                return;

            File.WriteAllText(changelogPath, changelog);
        }

        public static string GenerateChangelog(string projectPath)
        {
            var (lastTag, commits) = GetGitChanges(projectPath);
            if (string.IsNullOrEmpty(lastTag) || commits.Count == 0)
                return null;

            // Get existing changelog content
            var changelogPath = GetChangelogPath(projectPath);
            var existingContent = File.Exists(changelogPath) ? File.ReadAllText(changelogPath) : null;

            // Generate new changes
            string newChanges;
            if (existingContent != null && existingContent.Contains("## [Unreleased]"))
            {
                // If there's an [Unreleased] section, append to it
                var unreleasedIndex = existingContent.IndexOf("## [Unreleased]");
                var nextSectionIndex = existingContent.IndexOf("## [", unreleasedIndex + 1);
                var beforeUnreleased = existingContent.Substring(0, unreleasedIndex);
                var afterUnreleased = nextSectionIndex > 0 ? existingContent.Substring(nextSectionIndex) : "";

                // Try AI generation first
                AIUtils.GenerateChangelog(commits, OnAIChangelogGenerated);
                // Fallback to default generation if AI fails
                newChanges = GenerateDefaultChangelog(null, lastTag, commits);

                return beforeUnreleased + "## [Unreleased]\n\n" + newChanges + "\n\n" + afterUnreleased;
            }
            else
            {
                // Create new changelog with [Unreleased] section
                var sb = new StringBuilder();
                sb.AppendLine("# Changelog");
                sb.AppendLine();
                sb.AppendLine("## [Unreleased]");
                sb.AppendLine();

                // Try AI generation first
                AIUtils.GenerateChangelog(commits, OnAIChangelogGenerated);
                // Fallback to default generation if AI fails
                newChanges = GenerateDefaultChangelog(null, lastTag, commits);
                sb.AppendLine(newChanges);
                sb.AppendLine();

                // Add existing content if any
                if (!string.IsNullOrEmpty(existingContent))
                {
                    // Skip the title if it exists in the existing content
                    var contentToAdd = existingContent.StartsWith("# Changelog") 
                        ? existingContent.Substring(existingContent.IndexOf("\n\n") + 2)
                        : existingContent;
                    sb.AppendLine(contentToAdd);
                }

                return sb.ToString();
            }
        }

        public static string GetChangelogPath(string projectPath)
        {
            var selectionPath = PackageDevUtils.GetSelectionPath();
            if (string.IsNullOrEmpty(selectionPath))
            {
                Debug.LogError("[ChangelogUtils] No package selected");
                return null;
            }

            return Path.Combine(projectPath, selectionPath, CHANGELOG_FILE);
        }

        public static (string lastTag, List<string> commits) GetGitChanges(string projectPath)
        {
            var lastTag = GitUtils.GetLastReleaseTag(projectPath);
            if (string.IsNullOrEmpty(lastTag))
            {
                Debug.LogWarning("[ChangelogUtils] No previous release tag found");
                return (null, null);
            }

            var commits = GitUtils.GetCommitsSinceTag(projectPath, lastTag);
            if (commits.Count == 0)
            {
                Debug.LogWarning("[ChangelogUtils] No commits found since last release");
                return (null, null);
            }

            return (lastTag, commits);
        }

        private static void OnAIChangelogGenerated(string aiGeneratedChangelog)
        {
            var manifestView = PackagePublishWindow.s_instance?.packageManifestView;
            if (manifestView == null) return;

            var changelogField = manifestView.GetChangelogField();
            if (changelogField == null) return;

            EditorApplication.delayCall += () => {
                changelogField.value = aiGeneratedChangelog;
            };
        }

        public static string GenerateDefaultChangelog(string version, string lastTag, List<string> commits)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### [{version ?? "Unreleased"}] - {System.DateTime.Now.ToString("yyyy-MM-dd")}");
            sb.AppendLine();

            foreach (var commit in commits)
            {
                sb.AppendLine($"- {commit}");
            }

            return sb.ToString();
        }
    }
} 