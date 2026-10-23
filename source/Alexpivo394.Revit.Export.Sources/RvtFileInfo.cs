using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenMcdf;

namespace Alexpivo394.Revit.Export.Sources;

/// <summary>
///     Версия Revit, в которой сохранён файл .rvt, без запуска Revit. Файл .rvt — составной OLE-файл; в потоке
///     «BasicFileInfo» Revit пишет текст в UTF-16 со строкой «Format: 2022» (до 2019 — «Autodesk Revit 2018»).
/// </summary>
public static class RvtFileInfo
{
    private const string StreamName = "BasicFileInfo";

    private static readonly Regex FormatLine = new(@"Format:\s*(\d{4})", RegexOptions.CultureInvariant);
    private static readonly Regex LegacyBuildLine = new(@"Autodesk Revit (\d{4})", RegexOptions.CultureInvariant);

    /// <summary>Год версии Revit; null — файла нет, он не OLE, в нём нет «BasicFileInfo» или года не найти.</summary>
    public static int? TryReadYear(string filePath)
    {
        try
        {
            // Модель может быть открыта в Revit — читаем, не мешая ему.
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            if (!root.TryOpenStream(StreamName, out var info)) return null;

            using (info)
            using (var buffer = new MemoryStream())
            {
                info.CopyTo(buffer);
                return ParseYear(buffer.ToArray());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OpenMcdf.FileFormatException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Год из содержимого «BasicFileInfo». Перед текстом идут служебные байты, и их число может быть нечётным —
    ///     тогда UTF-16 читается только со сдвигом на байт, поэтому пробуются оба выравнивания.
    /// </summary>
    private static int? ParseYear(byte[] data)
    {
        for (var offset = 0; offset < 2 && offset < data.Length; offset++)
        {
            if (ParseYear(Encoding.Unicode.GetString(data, offset, data.Length - offset)) is { } year) return year;
        }

        return null;
    }

    /// <summary>Год из текста «BasicFileInfo»; служебные символы вокруг текста не мешают.</summary>
    public static int? ParseYear(string basicFileInfoText)
    {
        var match = FormatLine.Match(basicFileInfoText);
        if (!match.Success) match = LegacyBuildLine.Match(basicFileInfoText);
        if (!match.Success) return null;

        var year = int.Parse(match.Groups[1].Value);
        return year is >= 2000 and <= 2100 ? year : null;
    }
}
