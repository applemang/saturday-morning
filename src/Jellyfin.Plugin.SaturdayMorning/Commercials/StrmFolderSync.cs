using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.SaturdayMorning.Commercials;

/// <summary>
/// Makes the top level of a folder hold exactly the desired .strm files, touching nothing else.
/// </summary>
public static class StrmFolderSync
{
    /// <summary>
    /// Writes, updates and deletes top-level .strm files so the folder matches.
    /// </summary>
    /// <remarks>
    /// Unchanged files are not rewritten. Only <c>*.strm</c> files directly in the folder are
    /// ever deleted; other files and subfolders are left alone.
    /// </remarks>
    /// <param name="folder">Existing folder of the commercials library.</param>
    /// <param name="desired">File name to file content.</param>
    /// <returns>Counts of written, unchanged and removed files.</returns>
    /// <exception cref="CommercialsException">The folder is missing or a name is unsafe.</exception>
    public static SyncReport Sync(string folder, IReadOnlyDictionary<string, string> desired)
    {
        if (!Directory.Exists(folder))
        {
            throw new CommercialsException($"Commercials folder {folder} does not exist.");
        }

        foreach (var name in desired.Keys)
        {
            if (!name.EndsWith(".strm", StringComparison.Ordinal)
                || name.StartsWith('.')
                || name.Contains('/', StringComparison.Ordinal)
                || name.Contains('\\', StringComparison.Ordinal))
            {
                throw new CommercialsException($"Refusing invalid .strm file name '{name}'.");
            }
        }

        int written = 0, unchanged = 0, removed = 0;
        foreach (var (name, content) in desired.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                unchanged++;
                continue;
            }

            var temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
            written++;
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.strm", SearchOption.TopDirectoryOnly).ToList())
        {
            if (!desired.ContainsKey(Path.GetFileName(path)))
            {
                File.Delete(path);
                removed++;
            }
        }

        return new SyncReport(written, unchanged, removed);
    }
}
