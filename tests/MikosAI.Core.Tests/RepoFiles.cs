using System;
using System.IO;

namespace MikosAI.Core.Tests
{
    /// <summary>
    /// Menemukan file artefak di akar repo dari direktori output test.
    ///
    /// Test di bawah ini memuat capabilities.yaml, tools.json, dan contoh plan
    /// yang SEBENARNYA - bukan salinan fixture. Itu disengaja: kalau artefak itu
    /// jadi tidak konsisten, test harus merah, bukan diam.
    /// </summary>
    internal static class RepoFiles
    {
        private static readonly Lazy<string> RootLazy = new Lazy<string>(FindRoot);

        internal static string Root => RootLazy.Value;

        internal static string CapabilitiesYaml => Path.Combine(Root, "capabilities.yaml");

        internal static string ToolsJson => Path.Combine(Root, "schemas", "tools.json");

        internal static string MountingPlatePlan
            => Path.Combine(Root, "examples", "mounting-plate.plan.json");

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "capabilities.yaml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Tidak bisa menemukan akar repo (yang memuat capabilities.yaml) dari " + AppContext.BaseDirectory);
        }
    }
}
