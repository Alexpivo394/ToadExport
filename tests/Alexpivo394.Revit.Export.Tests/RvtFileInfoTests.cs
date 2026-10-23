using System.Text;
using Alexpivo394.Revit.Export.Sources;
using OpenMcdf;
using Shouldly;

namespace Alexpivo394.Revit.Export.Tests;

public class RvtFileInfoTests
{
    [Test]
    public void ParseYear_FormatLine() =>
        RvtFileInfo.ParseYear("Worksharing: Not enabled\r\nFormat: 2022\r\nBuild: 20220112_1230(x64)").ShouldBe(2022);

    [Test]
    public void ParseYear_LegacyBuildLine() =>
        RvtFileInfo.ParseYear("Revit Build: Autodesk Revit 2018 (Build: 20170927_1515(x64))").ShouldBe(2018);

    [Test]
    public void ParseYear_NoYear_Null() => RvtFileInfo.ParseYear("garbage").ShouldBeNull();

    [Test]
    public void ParseYear_YearOutOfRange_Null() => RvtFileInfo.ParseYear("Format: 1234").ShouldBeNull();

    [Test]
    public void TryReadYear_CompoundFileWithBasicFileInfo()
    {
        using var file = new TempFile();
        byte[] data = [0x04, 0x00, 0x00, .. Encoding.Unicode.GetBytes("Worksharing: Not enabled\r\nFormat: 2024\r\n"), 0x00];
        WriteCompound(file.Path, "BasicFileInfo", data);

        RvtFileInfo.TryReadYear(file.Path).ShouldBe(2024);
    }

    [Test]
    public void TryReadYear_NotOleFile_Null()
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path, "not a revit file");

        RvtFileInfo.TryReadYear(file.Path).ShouldBeNull();
    }

    [Test]
    public void TryReadYear_MissingFile_Null() =>
        RvtFileInfo.TryReadYear(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.rvt")).ShouldBeNull();

    [Test]
    public void TryReadYear_NoBasicFileInfoStream_Null()
    {
        using var file = new TempFile();
        WriteCompound(file.Path, "Contents", [1, 2, 3]);

        RvtFileInfo.TryReadYear(file.Path).ShouldBeNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public void TryReadYear_CorruptedOleFile_Null(int seed)
    {
        using var file = new TempFile();
        var garbage = new byte[4096];
        new Random(seed).NextBytes(garbage);
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(garbage, 0);
        File.WriteAllBytes(file.Path, garbage);

        RvtFileInfo.TryReadYear(file.Path).ShouldBeNull();
    }

    [Test]
    public void TryReadYear_TruncatedRealHeader_Null()
    {
        using var file = new TempFile();
        WriteCompound(file.Path, "BasicFileInfo", Encoding.Unicode.GetBytes("Format: 2024"));
        var bytes = File.ReadAllBytes(file.Path);
        File.WriteAllBytes(file.Path, bytes.Take(600).ToArray());

        RvtFileInfo.TryReadYear(file.Path).ShouldBeNull();
    }

    private static void WriteCompound(string path, string streamName, byte[] data)
    {
        using var root = RootStorage.Create(path);
        using var stream = root.CreateStream(streamName);
        stream.Write(data, 0, data.Length);
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rvtinfo_{Guid.NewGuid():N}.rvt");

        public void Dispose() => File.Delete(Path);
    }
}
