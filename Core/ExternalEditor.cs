using System;
using System.Diagnostics;
using System.IO;

namespace UnknownMod.Core
{
    /// <summary>Opens mod files in whatever the user edits JSON with; hot reload picks the changes up.</summary>
    public static class ExternalEditor
    {
        /// <summary>The JSON file that holds an entity, or null. Cards live in subfolders, so the search is recursive.</summary>
        public static string FindEntityFile(string modId, string folder, string id, bool isPatch)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(id)) return null;
            string dir = Path.Combine(ModProjectLoader.ModFolder(modId), isPatch ? Path.Combine("_patches", folder) : folder);
            if (!Directory.Exists(dir)) return null;
            try
            {
                foreach (var file in Directory.GetFiles(dir, id + ".json", SearchOption.AllDirectories)) return file;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[External] Search failed: {ex.Message}"); }
            return null;
        }

        public static bool OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            return Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static bool Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (File.Exists(path)) return Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            if (Directory.Exists(path)) return Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            return false;
        }

        /// <summary>Open a folder in VS Code through the <c>code</c> command; falls back to Explorer when it is not installed.</summary>
        public static bool OpenFolderInCode(string folder)
        {
            if (!Directory.Exists(folder)) return false;
            var psi = new ProcessStartInfo("cmd.exe", "/c code \"" + folder + "\"") { UseShellExecute = false, CreateNoWindow = true };
            try
            {
                var p = Process.Start(psi);
                if (p != null && p.WaitForExit(1500) && p.ExitCode != 0) return Reveal(folder);
                return true;
            }
            catch { return Reveal(folder); }
        }

        private static bool Start(ProcessStartInfo psi)
        {
            try { Process.Start(psi); return true; }
            catch (Exception ex) { Plugin.Log.LogWarning($"[External] Could not start '{psi.FileName}': {ex.Message}"); return false; }
        }
    }
}
