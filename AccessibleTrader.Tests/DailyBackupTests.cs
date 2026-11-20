using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A copy a day of the files a bad save would otherwise lose: the paper account, saved
    /// workspaces and alerts (code review, 2026-11-20). The rename in <see cref="AtomicFile"/>
    /// already makes a crash safe; this is for a save that is wrong.
    /// </summary>
    public class DailyBackupTests
    {
        private static readonly DateTime Day1 = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

        private static string Backup(string path, DateTime day) =>
            Path.Combine(Path.GetDirectoryName(path)!, AtomicFile.BackupFolderName,
                         $"{Path.GetFileNameWithoutExtension(path)}.{day:yyyy-MM-dd}{Path.GetExtension(path)}");

        [Fact]
        public void The_first_save_of_a_day_keeps_the_file_as_it_stood_and_later_saves_leave_that_copy()
        {
            string dir = TestTemp.NewDir("att-backup-");
            string path = Path.Combine(dir, "paper_account.json");

            AtomicFile.WriteAllTextKeepingDailyBackup(path, "first", nowUtc: Day1);
            Assert.False(Directory.Exists(Path.Combine(dir, AtomicFile.BackupFolderName)));   // nothing to keep yet

            AtomicFile.WriteAllTextKeepingDailyBackup(path, "second", nowUtc: Day1.AddHours(1));
            AtomicFile.WriteAllTextKeepingDailyBackup(path, "third", nowUtc: Day1.AddHours(2));

            Assert.Equal("third", File.ReadAllText(path));
            Assert.Equal("first", File.ReadAllText(Backup(path, Day1)));

            AtomicFile.WriteAllTextKeepingDailyBackup(path, "fourth", nowUtc: Day1.AddDays(1));
            Assert.Equal("third", File.ReadAllText(Backup(path, Day1.AddDays(1))));
            Assert.Equal("first", File.ReadAllText(Backup(path, Day1)));
        }

        [Fact]
        public void Seven_days_are_kept_and_another_files_copies_are_left_alone()
        {
            string dir = TestTemp.NewDir("att-backup-");
            string path = Path.Combine(dir, "Main.json");
            string other = Path.Combine(dir, "Main.Other.json");
            File.WriteAllText(path, "day0");
            File.WriteAllText(other, "o0");
            AtomicFile.WriteAllTextKeepingDailyBackup(other, "o1", nowUtc: Day1);

            for (int d = 0; d < 10; d++)
                AtomicFile.WriteAllTextKeepingDailyBackup(path, $"day{d + 1}", nowUtc: Day1.AddDays(d));

            var kept = Directory.GetFiles(Path.Combine(dir, AtomicFile.BackupFolderName), "Main.20*.json")
                .Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.Equal(Enumerable.Range(3, 7).Select(d => $"Main.{Day1.AddDays(d):yyyy-MM-dd}.json"), kept);
            Assert.Equal("day9", File.ReadAllText(Backup(path, Day1.AddDays(9))));
            Assert.Equal("o0", File.ReadAllText(Backup(other, Day1)));
        }

        [Fact]
        public void A_saved_workspace_and_the_alerts_keep_a_copy_but_a_session_autosave_slot_does_not()
        {
            var paths = new TempWorkspacePaths();
            var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, paths);
            string dir = Path.Combine(paths.AppDataDirectory, "Workspaces");
            string slot = AccessibleTrader.Core.Services.Workspace.SessionAutosaveService.LastSessionProfileName + "abc";

            library.SaveProfile("Main", new WorkspaceConfiguration());
            library.SaveProfile("Main", new WorkspaceConfiguration());
            library.SaveProfile(slot, new WorkspaceConfiguration());
            library.SaveProfile(slot, new WorkspaceConfiguration());
            library.SaveAlerts(new List<AccessibleTrader.Sdk.Alerts.AlertDefinition>());
            library.SaveAlerts(new List<AccessibleTrader.Sdk.Alerts.AlertDefinition>());

            var copies = Directory.GetFiles(Path.Combine(dir, AtomicFile.BackupFolderName))
                .Select(Path.GetFileName).Select(f => f!.Split('.')[0]).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "Main", "alerts" }, copies);
            Assert.Equal(new[] { "Main" }, library.GetAvailableProfiles());
        }

        [Fact]
        public void A_backup_that_cannot_be_written_does_not_stop_the_save()
        {
            string dir = TestTemp.NewDir("att-backup-");
            string path = Path.Combine(dir, "paper_account.json");
            File.WriteAllText(path, "before");
            File.WriteAllText(Path.Combine(dir, AtomicFile.BackupFolderName), "a file where the folder would go");

            AtomicFile.WriteAllTextKeepingDailyBackup(path, "after", nowUtc: Day1);

            Assert.Equal("after", File.ReadAllText(path));
        }
    }
}
