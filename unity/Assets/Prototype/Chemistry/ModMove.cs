// Moves a player's mods out of the install, once. Saves used to land in the install's
// rules/mods, which an update replaces, so a save now goes to the player's data folder
// (ModCatalog) and this carries over what an older copy left behind where that folder
// survived. A copy Velopack updated has lost them before any of this runs.
//
// The build writes the shipped mods' ids beside them (ShippedList), so any other mod
// there is a player's. With no list nothing can be told apart and nothing moves. It
// copies, then tries to delete the install's file: a read-only install keeps it, and the
// ledger in the data folder stops it being copied again if the player later deletes
// theirs. It never overwrites the data folder: a different mod of that id stays there
// and the install's file stays where it is.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chem
{
    public static class ModMove
    {
        /// <summary>In the build's rules/mods: the shipped mods' ids, one a line. Not *.json, so nobody reads it as a mod.</summary>
        public const string ShippedList = "shipped-mods.txt";

        /// <summary>In the data folder's mods: the ids dealt with, one a line, never looked at again.</summary>
        public const string MovedList = "moved-from-install.txt";

        /// <summary>Writes <see cref="ShippedList"/> for the mods in the folder (the build's pack step).</summary>
        public static void WriteShippedList(string modsDir)
        {
            var ids = Directory.GetFiles(modsDir, "*.json").Select(ModCatalog.IdOf).OrderBy(id => id, StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(modsDir, ShippedList), string.Concat(ids.Select(id => id + "\n")));
        }

        /// <summary>Moves the player's mods from the install's rules/mods to their own folder. Never throws: returns a sentence for each thing it did or couldn't.</summary>
        public static IReadOnlyList<string> FromInstall(string installDir, string playerDir)
        {
            var said = new List<string>();
            string shipped = Path.Combine(installDir, ShippedList), ledger = Path.Combine(playerDir, MovedList);
            try
            {
                if (!File.Exists(shipped)) return said;
                var leave = new HashSet<string>(File.ReadAllLines(shipped), StringComparer.Ordinal);
                if (File.Exists(ledger)) leave.UnionWith(File.ReadAllLines(ledger));
                foreach (var file in Directory.GetFiles(installDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                    if (!leave.Contains(ModCatalog.IdOf(file))) said.Add(Move(file, playerDir, ledger));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                said.Add($"Mods in {installDir} couldn't be read: {e.Message}");
            }
            return said;
        }

        static string Move(string file, string playerDir, string ledger)
        {
            string id = ModCatalog.IdOf(file), to = Path.Combine(playerDir, Path.GetFileName(file));
            try
            {
                Directory.CreateDirectory(playerDir);
                bool clash = File.Exists(to) && File.ReadAllText(to) != File.ReadAllText(file);
                if (!File.Exists(to))
                {
                    // Never a half-written mod under its real name.
                    string part = to + ".part";
                    MakeWritable(part);
                    File.Copy(file, part, true);
                    MakeWritable(part);
                    File.Move(part, to);
                }
                File.AppendAllText(ledger, id + "\n");
                if (clash) return $"{id} is in the data folder already and differs, so that one stays and {file} is left where it is.";
                return Deleted(file) ? $"{id} moved to {to}." : $"{id} copied to {to}; the install can't be written, so its copy stays.";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return $"{id} couldn't be moved, and stays in the install for next time: {e.Message}";
            }
        }

        // A copy keeps its source's read-only state, and the player's file is theirs to overwrite and
        // delete. Before the copy too, as a crash can leave a read-only .part that a copy can't replace.
        static void MakeWritable(string file)
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
        }

        static bool Deleted(string file)
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
