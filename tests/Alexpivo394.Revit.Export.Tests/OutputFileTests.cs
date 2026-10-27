using Shouldly;
using Alexpivo394.Revit.Export.Core;

namespace Alexpivo394.Revit.Export.Tests;

[NotInParallel(nameof(OutputFile))]
public class OutputFileTests
{
    [Test]
    public void Publish_ReplacesPreviousFile_AndLeavesNoTempFiles()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        File.WriteAllText(dirs.Staged, "new export");

        var size = OutputFile.Publish(dirs.Staged, dirs.Target);

        File.ReadAllText(dirs.Target).ShouldBe("new export");
        size.ShouldBe(10);
        Directory.GetFiles(dirs.Output).ShouldBe([dirs.Target]);
    }

    [Test]
    public void Publish_NoPreviousFile_CreatesIt()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Staged, "new");

        OutputFile.Publish(dirs.Staged, dirs.Target);

        File.ReadAllText(dirs.Target).ShouldBe("new");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Publish_MissingOrEmptyExport_KeepsPreviousFile(bool empty)
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        if (empty) File.WriteAllText(dirs.Staged, "");

        Should.Throw<InvalidOperationException>(() => OutputFile.Publish(dirs.Staged, dirs.Target));

        File.ReadAllText(dirs.Target).ShouldBe("old");
        Directory.GetFiles(dirs.Output).ShouldBe([dirs.Target]);
    }

    [Test]
    public void Publish_ReplaceFailsHalfway_RestoresPreviousFile()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        File.WriteAllText(dirs.Staged, "new");
        // Как ReplaceFile с ERROR_UNABLE_TO_MOVE_REPLACEMENT: старый файл уже переименован в резервный, новый не на месте,
        // и обход двумя переименованиями тоже падает (сеть отвалилась).
        OutputFile.ReplaceOverride = (temp, target, backup) =>
        {
            File.Move(target, backup);
            File.Delete(temp);
            throw new IOException("сетевой ресурс недоступен");
        };

        Should.Throw<IOException>(() => OutputFile.Publish(dirs.Staged, dirs.Target));

        File.ReadAllText(dirs.Target).ShouldBe("old");
        Directory.GetFiles(dirs.Output).ShouldBe([dirs.Target]);
    }

    [Test]
    public void Publish_ReplaceFileUnsupported_SwapsByRename()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        File.WriteAllText(dirs.Staged, "new");
        OutputFile.ReplaceOverride = (_, _, _) => throw new PlatformNotSupportedException();

        OutputFile.Publish(dirs.Staged, dirs.Target);

        File.ReadAllText(dirs.Target).ShouldBe("new");
        Directory.GetFiles(dirs.Output).ShouldBe([dirs.Target]);
    }

    [Test]
    public void Publish_TargetOpenWithoutDeleteSharing_FailsAndKeepsPreviousFile()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        File.WriteAllText(dirs.Staged, "new");

        using (new FileStream(dirs.Target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Should.Throw<IOException>(() => OutputFile.Publish(dirs.Staged, dirs.Target));
        }

        File.ReadAllText(dirs.Target).ShouldBe("old");
        Directory.GetFiles(dirs.Output).ShouldBe([dirs.Target]);
    }

    [Test]
    public void Publish_LeftoversOfCrashedPublish_AreCleanedAndDoNotBlock()
    {
        using var dirs = new Dirs();
        File.WriteAllText(dirs.Target, "old");
        File.WriteAllText(dirs.Staged, "new");
        var staleTemp = dirs.Leftover("dead.tmp", age: TimeSpan.FromHours(2));
        var freshTemp = dirs.Leftover("live.tmp", age: TimeSpan.Zero);

        OutputFile.Publish(dirs.Staged, dirs.Target);

        File.ReadAllText(dirs.Target).ShouldBe("new");
        File.Exists(staleTemp).ShouldBeFalse();
        File.Exists(freshTemp).ShouldBeTrue(); // может быть публикацией другого процесса прямо сейчас
    }

    [Test]
    public void CleanupLeftovers_BackupWithoutTarget_IsRestored()
    {
        using var dirs = new Dirs();
        var backup = dirs.Leftover("x.bak", age: TimeSpan.FromHours(2), content: "last good");

        OutputFile.CleanupLeftovers(dirs.Target);

        File.Exists(backup).ShouldBeFalse();
        File.ReadAllText(dirs.Target).ShouldBe("last good");
    }

    private sealed class Dirs : IDisposable
    {
        private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "te-out-" + Guid.NewGuid().ToString("N"))).FullName;

        public Dirs()
        {
            Output = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
            Staged = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName, "АР.nwc");
            Target = Path.Combine(Output, "АР.nwc");
        }

        public string Output { get; }
        public string Staged { get; }
        public string Target { get; }

        public string Leftover(string suffix, TimeSpan age, string content = "partial")
        {
            var path = Target + OutputFile.Marker + suffix;
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        public void Dispose()
        {
            OutputFile.ReplaceOverride = null;
            Directory.Delete(_root, true);
        }
    }
}
