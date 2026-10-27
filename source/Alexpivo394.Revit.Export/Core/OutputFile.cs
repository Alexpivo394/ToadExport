using System.IO;

namespace Alexpivo394.Revit.Export.Core;

/// <summary>
///     Подмена файла результата готовым файлом так, чтобы прошлая выгрузка не терялась, если новая не удалась.
///     <para>
///         Готовый файл копируется во временный рядом с целевым (та же папка — тот же том, в том числе на SMB-ресурсе), и только
///         потом старый подменяется одной операцией <see cref="File.Replace(string, string, string, bool)" />. Копирование
///         по сети не атомарно — поэтому оно идёт во временный файл; атомарна только подмена внутри одной папки.
///     </para>
///     <para>
///         Обрывки упавших публикаций (<c>имя.toadexport-*.tmp</c> и <c>.bak</c>) убираются при следующей публикации того же
///         файла; резервная копия без целевого файла возвращается на место, а не удаляется.
///     </para>
/// </summary>
public static class OutputFile
{
    public const string Marker = ".toadexport-";

    private const string TempExtension = ".tmp";
    private const string BackupExtension = ".bak";
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>Обрывки моложе этого не трогаем: их может прямо сейчас писать другая выгрузка.</summary>
    public static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

    /// <summary>Только для тестов: вместо <see cref="File.Replace(string, string, string, bool)" />.</summary>
    internal static Action<string, string, string>? ReplaceOverride { get; set; }

    /// <summary>Проверяет готовый файл и подменяет им <paramref name="target" />. Возвращает размер файла.</summary>
    /// <exception cref="InvalidOperationException">Готовый файл не найден, пуст или не читается.</exception>
    /// <exception cref="IOException">Не удалось скопировать или подменить; прежний <paramref name="target" /> не тронут.</exception>
    public static long Publish(string staged, string target)
    {
        var size = Verify(staged);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        CleanupLeftovers(target);

        var token = Guid.NewGuid().ToString("N");
        var temp = target + Marker + token + TempExtension;
        var backup = target + Marker + token + BackupExtension;
        try
        {
            CopyVerified(staged, temp, size);
            if (File.Exists(target)) Replace(temp, target, backup);
            else File.Move(temp, target);
        }
        finally
        {
            TryDelete(temp);
            // Без целевого файла резервная копия — единственная копия прошлой выгрузки: её не трогаем.
            if (File.Exists(target)) TryDelete(backup);
        }

        return size;
    }

    /// <summary>Убирает обрывки прошлых публикаций <paramref name="target" />.</summary>
    public static void CleanupLeftovers(string target)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.GetFiles(folder, Path.GetFileName(target) + Marker + "*"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < LeftoverAge) continue;
                if (file.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase) && !File.Exists(target)) File.Move(file, target);
                else File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Файл занят — значит, его ещё пишут; уберём в следующий раз.
            }
        }
    }

    /// <summary>NWC и RVT без Navisworks и Revit не проверить — проверяем, что файл есть, не пуст и читается.</summary>
    private static long Verify(string staged)
    {
        var info = new FileInfo(staged);
        if (!info.Exists) throw new InvalidOperationException($"готовый файл {info.Name} не найден");
        if (info.Length == 0) throw new InvalidOperationException($"готовый файл {info.Name} пуст");

        using var stream = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[Math.Min(64 * 1024, info.Length)];
        if (stream.Read(buffer, 0, buffer.Length) == 0) throw new InvalidOperationException($"готовый файл {info.Name} не читается");
        return info.Length;
    }

    private static void CopyVerified(string source, string temp, long size)
    {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
        {
            input.CopyTo(output, 1024 * 1024);
            output.Flush(true);
        }

        var copied = new FileInfo(temp).Length;
        if (copied != size) throw new IOException($"копия не совпала по размеру ({copied} из {size} байт)");
    }

    /// <summary>
    ///     ReplaceFile при сбое оставляет старый файл под именем <paramref name="backup" /> — тогда возвращаем его. Если сервер
    ///     SMB не умеет ReplaceFile, меняем файлы двумя переименованиями внутри папки; занятый целевой файл так не обойти —
    ///     это ошибка, а старый файл остаётся.
    /// </summary>
    private static void Replace(string temp, string target, string backup)
    {
        try
        {
            if (ReplaceOverride is { } replace) replace(temp, target, backup);
            else File.Replace(temp, target, backup, true);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Restore(target, backup);
            if (exception.HResult is SharingViolation or LockViolation)
                throw new IOException($"файл {Path.GetFileName(target)} занят другой программой", exception);
            if (!File.Exists(temp)) throw;
        }

        File.Move(target, backup);
        try
        {
            File.Move(temp, target);
        }
        catch
        {
            Restore(target, backup);
            throw;
        }
    }

    private static void Restore(string target, string backup)
    {
        if (!File.Exists(target) && File.Exists(backup)) File.Move(backup, target);
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Удалим при следующей публикации этого файла (CleanupLeftovers).
        }
    }
}
