using Orc.Core;
using Orc.Packages;
using Xunit;

namespace Orc.Packages.Tests;

/// <summary>
/// 资产化 S5 验收：manifest 读写（往返一致／非法拒绝）与资产名派生（包 GUID8 ＋ 包版本继承）。
/// </summary>
public class PackageManifestTests
{
    private static readonly Guid SampleGuid = Guid.Parse("ab12cd34-0000-0000-0000-000000000000");

    private static PackageManifest SampleManifest() => new(
        PackageKind.Plugin,
        "orc.core.keywords",
        SampleGuid,
        new AssetVersion(1, 2, 0),
        new[] { new PackageDependency("orc.core.base", "1.*.*") },
        new[]
        {
            new PackageAsset("csx", "faction.service", "csx/faction.csx"),
            new PackageAsset("json", "faction.table", "data/factions.json"),
        });

    [Fact]
    public void Manifest_读写往返一致()
    {
        var manifest = SampleManifest();

        var parsed = PackageManifest.Parse(manifest.ToJson());

        Assert.Equal(manifest.Kind, parsed.Kind);
        Assert.Equal(manifest.Id, parsed.Id);
        Assert.Equal(manifest.Guid, parsed.Guid);
        Assert.Equal(manifest.Version, parsed.Version);
        Assert.Equal(manifest.Dependencies, parsed.Dependencies);
        Assert.Equal(manifest.Assets, parsed.Assets);
    }

    [Fact]
    public void Guid8_与缓存域_派生正确()
    {
        var manifest = SampleManifest();

        Assert.Equal("ab12cd34", manifest.Guid8);
        Assert.Equal("ab12cd34#1.2.0", manifest.CacheScope);
    }

    [Fact]
    public void 资产名派生_继承包GUID8与包版本()
    {
        var manifest = SampleManifest();

        var name = manifest.AssetNameOf(manifest.Assets[0]);

        Assert.Equal(AssetLoadKind.Csx, name.Kind);
        Assert.Equal("faction.service", name.LocalName);
        Assert.Equal("ab12cd34", name.PackageGuid8);
        Assert.Equal(new AssetVersion(1, 2, 0), name.Version);
        Assert.Equal("csx：faction.service@ab12cd34#1.2.0", name.ToString());
    }

    [Fact]
    public void 键名大小写不敏感_读宽()
    {
        var json = """
        {
          "Kind": "plugin",
          "ID": "p1",
          "Guid": "ab12cd34-0000-0000-0000-000000000000",
          "Version": "1.0.0"
        }
        """;

        var manifest = PackageManifest.Parse(json);

        Assert.Equal("p1", manifest.Id);
        Assert.Empty(manifest.Assets);
        Assert.Empty(manifest.Dependencies);
    }

    [Theory]
    [InlineData("""{"kind":"plugin","id":"p","guid":"not-a-guid","version":"1.0.0"}""")]
    [InlineData("""{"kind":"plugin","id":"p","guid":"ab12cd34-0000-0000-0000-000000000000","version":"1.0"}""")]
    [InlineData("""{"kind":"nope","id":"p","guid":"ab12cd34-0000-0000-0000-000000000000","version":"1.0.0"}""")]
    [InlineData("""{"kind":"plugin","id":"","guid":"ab12cd34-0000-0000-0000-000000000000","version":"1.0.0"}""")]
    public void 非法manifest_明确拒绝(string json)
    {
        Assert.Throws<FormatException>(() => PackageManifest.Parse(json));
    }

    [Fact]
    public void 资产短名重复_拒绝()
    {
        var json = """
        {
          "kind": "plugin",
          "id": "p1",
          "guid": "ab12cd34-0000-0000-0000-000000000000",
          "version": "1.0.0",
          "assets": [
            { "type": "csx", "name": "a", "path": "a.csx" },
            { "type": "json", "name": "a", "path": "b.json" }
          ]
        }
        """;

        Assert.Throws<FormatException>(() => PackageManifest.Parse(json));
    }
}
