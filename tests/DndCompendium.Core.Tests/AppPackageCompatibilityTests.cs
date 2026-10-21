using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class AppPackageCompatibilityTests
{
    private static readonly AppUpdateManifest Manifest = new()
    {
        VersionCode = 4, VersionName = "1.3", PackageId = "com.march.dndcompendium",
        ApkUrl = new Uri("https://example.test/DndCompendium.apk"), Sha256 = new string('a', 64)
    };
    private static readonly AppPackageIdentity Installed = new(
        "com.march.dndcompendium", 3, new HashSet<string> { "certificate-a" });

    [Fact]
    public void Validate_AcceptsSamePackageNewerVersionAndCompatibleCertificate()
    {
        var candidate = new AppPackageIdentity(
            "com.march.dndcompendium", 4, new HashSet<string> { "certificate-a" });

        Assert.True(AppPackageCompatibility.Validate(Installed, candidate, Manifest, 34).IsValid);
    }

    [Theory]
    [InlineData("other.package", 4, "certificate-a")]
    [InlineData("com.march.dndcompendium", 3, "certificate-a")]
    [InlineData("com.march.dndcompendium", 4, "certificate-b")]
    public void Validate_RejectsWrongPackageOldVersionOrWrongCertificate(string packageId, int versionCode, string certificate)
    {
        var candidate = new AppPackageIdentity(packageId, versionCode, new HashSet<string> { certificate });

        Assert.False(AppPackageCompatibility.Validate(Installed, candidate, Manifest, 34).IsValid);
    }

    [Fact]
    public void Validate_RejectsUnsupportedAndroidVersion()
    {
        var candidate = new AppPackageIdentity(
            "com.march.dndcompendium", 4, new HashSet<string> { "certificate-a" });
        var manifest = Manifest with { MinimumAndroidApi = 35 };

        Assert.False(AppPackageCompatibility.Validate(Installed, candidate, manifest, 34).IsValid);
    }
}
