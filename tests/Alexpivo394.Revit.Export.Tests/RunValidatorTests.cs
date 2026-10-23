using Alexpivo394.Revit.Export.Core;
using Shouldly;

namespace Alexpivo394.Revit.Export.Tests;

public class RunValidatorTests
{
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "te-out-" + Guid.NewGuid().ToString("N"));

    private static ExportOptions Options() => new();

    [Test]
    public void ValidRun_NoErrors() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, true)], Options(), Output).ShouldBeEmpty();

    [Test]
    public void NoModels_IsError() =>
        RunValidator.Errors([], Options(), Output).ShouldContain(e => e.Contains("Не отмечено ни одной модели"));

    [Test]
    public void EmptyOutputFolder_IsError() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, false)], Options(), "")
            .ShouldContain(e => e.Contains("папку для результатов"));

    [Test]
    public void OutputEqualsSourceFolder_IsError()
    {
        var job = new ModelJob("AR.rvt", Path.Combine(Output.ToUpperInvariant(), "AR.rvt"), false, true, false);

        RunValidator.Errors([job], Options(), Output)
            .ShouldContain(e => e.Contains("совпадает с папкой модели AR.rvt"));
    }

    [Test]
    public void OutputIsJunctionToSourceFolder_IsError()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "te-jn-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            File.WriteAllText(Path.Combine(source, "AR.rvt"), "");
            var junction = Path.Combine(root, "out");
            var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/c mklink /J \"{junction}\" \"{source}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            mklink.WaitForExit();
            Directory.Exists(junction).ShouldBeTrue();

            RunValidator.Errors([new ModelJob("AR.rvt", Path.Combine(source, "AR.rvt"), false, true, false)], Options(), junction)
                .ShouldContain(e => e.Contains("совпадает с папкой модели AR.rvt"));
        }
        finally
        {
            var link = Path.Combine(root, "out");
            if (Directory.Exists(link)) Directory.Delete(link); // сама ссылка, без содержимого папки
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void NwcWithoutViewName_IsError() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, false, true)], Options() with { NwcViewName = " " }, Output)
            .ShouldContain(e => e.Contains("3D-вида для NWC"));

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(double.NaN)]
    public void NwcWithInvalidFacetingFactor_IsError(double factor) =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, false, true)], Options() with { NwcFacetingFactor = factor }, Output)
            .ShouldContain(e => e.Contains("Коэффициент фасетизации"));

    [Test]
    public void RvtOnly_IgnoresFacetingFactor() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, false)], Options() with { NwcFacetingFactor = 0 }, Output)
            .ShouldBeEmpty();

    [Test]
    public void ServerJobs_NoFolderCheck() =>
        RunValidator.Errors([new ModelJob("AR.rvt", "RSN://rs01/AR.rvt", true, true, false)], Options(), Output).ShouldBeEmpty();

    [Test]
    public void Duplicates_ByFileNameIgnoreCase()
    {
        ModelJob[] jobs =
        [
            new("AR.rvt", @"D:\a\AR.rvt", false, true, false),
            new("ar.rvt", @"D:\b\ar.rvt", false, true, false),
            new("KR.rvt", @"D:\a\KR.rvt", false, true, false)
        ];

        var duplicates = RunValidator.Duplicates(jobs);

        duplicates.Count.ShouldBe(1);
        duplicates[0].Count().ShouldBe(2);
    }
}
