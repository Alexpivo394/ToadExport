using Alexpivo394.Revit.Export.Core;
using Shouldly;

namespace Alexpivo394.Revit.Export.Tests;

public class VersionGuardTests
{
    [Test]
    public void SameYear_Ok() =>
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("AR.rvt", 2022, 2022, allowUpgrade: false));

    [Test]
    public void OlderModel_UpgradeForbidden_Throws() =>
        Should.Throw<ModelVersionException>(() => VersionGuard.EnsureCanOpen("AR.rvt", 2022, 2024, allowUpgrade: false))
            .Message.ShouldContain("обновление запрещено");

    [Test]
    public void OlderModel_UpgradeAllowed_Ok() =>
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("AR.rvt", 2022, 2024, allowUpgrade: true));

    [Test]
    public void NewerModel_AlwaysThrows() =>
        Should.Throw<ModelVersionException>(() => VersionGuard.EnsureCanOpen("AR.rvt", 2025, 2024, allowUpgrade: true))
            .Message.ShouldContain("её не открыть");

    [Test]
    public void UnknownYear_Skipped() =>
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("AR.rvt", null, 2024, allowUpgrade: false));

    [Test]
    public void ExportOptions_Defaults_MatchToadExporter()
    {
        var options = new ExportOptions();

        options.SkipLinks.ShouldBeTrue();
        options.Audit.ShouldBeFalse();
        options.DeleteRevitLinks.ShouldBeTrue();
        options.Purge.ShouldBeTrue();
        options.NwcViewName.ShouldBe("Navisworks");
        options.NwcParameters.ShouldBe(NwcParameters.All);
        options.NwcFacetingFactor.ShouldBe(1.0);
        options.AllowUpgrade.ShouldBeTrue();
    }
}
