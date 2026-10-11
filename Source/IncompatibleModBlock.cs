using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Finds an active mod that blocks these patches.
    ///
    /// Находит включённый мод, из-за которого эти патчи не ставятся.
    /// </summary>
    public static class IncompatibleModBlock
    {
        /// <summary>
        /// Writes the blocking mods into the report.
        ///
        /// Записывает блокирующие моды в отчёт.
        /// </summary>
        public static bool TryFindActive(out string report)
        {
            List<string> found = new List<string>();
            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null || !mod.Active || mod.Official || IsThisMod(mod))
                {
                    continue;
                }

                if (!IsBlocked(mod))
                {
                    continue;
                }

                found.Add(mod.Name + " (" + mod.PackageIdPlayerFacing + ")");
            }

            if (found.Count == 0)
            {
                report = null;
                return false;
            }

            report = string.Join(", ", found.ToArray());
            return true;
        }

        private static bool IsThisMod(ModMetaData mod)
        {
            return string.Equals(mod.PackageId, "kebabebak.hsk.kebab.limits", StringComparison.Ordinal);
        }

        private static bool IsBlocked(ModMetaData mod)
        {
            string packageId = mod.PackageId ?? string.Empty;
            if (packageId.IndexOf("stackgap", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (NameMatchesBlockKey(mod.Name))
            {
                return true;
            }

            return HasBlockedAssembly(mod);
        }

        private static bool NameMatchesBlockKey(string name)
        {
            if (name.NullOrEmpty())
            {
                return false;
            }

            StringBuilder compact = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '-' || c == '_')
                {
                    continue;
                }

                compact.Append(char.ToLowerInvariant(c));
            }

            return compact.ToString().StartsWith("stackgap", StringComparison.Ordinal);
        }

        private static bool HasBlockedAssembly(ModMetaData mod)
        {
            DirectoryInfo root = mod.RootDir;
            if (root == null || !root.Exists)
            {
                return false;
            }

            try
            {
                foreach (string _ in Directory.EnumerateFiles(root.FullName, "StackGap.dll", SearchOption.AllDirectories))
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}
