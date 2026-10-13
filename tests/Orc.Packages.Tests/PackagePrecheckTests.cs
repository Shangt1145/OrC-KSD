using Orc.Core;
using Orc.Packages;
using Xunit;

namespace Orc.Packages.Tests;

/// <summary>
/// 资产化 S11 验收：包级 dry-run 预检——依赖可解析性／资产名全局唯一／包 GUID 冲突／卡包空资产告警；
/// 纯只读（Errors/Warnings 分列，无 Error 放行）。
/// </summary>
public class PackagePrecheckTests
{
    private static readonly Guid SharedGuid = Guid.Parse("ab12cd34-0000-0000-0000-000000000000");

    [Fact]
    public void 依赖齐备_放行()
    {
        var report = PackagePrecheck.Validate(Report(
            Pkg("p1", "1.0.0"),
            Pkg("p2", "1.0.0", guid: null, dependsOn: ("p1", "1.*.*"))));

        Assert.True(report.Ok);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void 依赖缺失_阻断()
    {
        var report = PackagePrecheck.Validate(Report(Pkg("p1", "1.0.0", dependsOn: ("missing", "1.0.0"))));

        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains("missing"));
    }

    [Fact]
    public void 资产名全局唯一_GUID冲突触发阻断()
    {
        var report = PackagePrecheck.Validate(Report(
            Pkg("a", "1.0.0", guid: SharedGuid, assets: ("json", "table", "t.json")),
            Pkg("b", "1.0.0", guid: SharedGuid, assets: ("json", "table", "t.json"))));

        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains("重复"));
        Assert.Contains(report.Errors, e => e.Contains("包身份须唯一"));
    }

    [Fact]
    public void 同包多版本_资产名随版本不同_不误报()
    {
        var report = PackagePrecheck.Validate(Report(
            Pkg("a", "1.0.0", assets: ("json", "table", "t.json")),
            Pkg("a", "1.2.0", assets: ("json", "table", "t.json"))));

        Assert.True(report.Ok);
    }

    [Fact]
    public void 卡包空资产_告警不阻断()
    {
        var report = PackagePrecheck.Validate(Report(Pkg("pack", "1.0.0", kind: PackageKind.CardPack)));

        Assert.True(report.Ok);
        Assert.Single(report.Warnings);
        Assert.Contains("未声明任何资产", report.Warnings[0]);
    }

    [Fact]
    public void 快照预检_依赖须在本局内可满足()
    {
        var snapshot = AssetCatalogSnapshot.FromPackages(new[]
        {
            Package("p1", PackageKind.Plugin, ("1.0.0", null)),
            Package("p2", PackageKind.Plugin, ("1.0.0", ("p1", "2.0.0"))),
        });

        var report = PackagePrecheck.Validate(snapshot);

        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains("无满足约束"));
    }

    private static PackageManifest Pkg(
        string id,
        string version,
        Guid? guid = null,
        PackageKind kind = PackageKind.Plugin,
        (string Type, string Name, string Path)? assets = null,
        (string Id, string Version)? dependsOn = null)
        => new(
            kind,
            id,
            guid ?? Guid.NewGuid(),
            AssetVersion.Parse(version),
            dependsOn is { } dep ? new[] { new PackageDependency(dep.Id, dep.Version) } : Array.Empty<PackageDependency>(),
            assets is { } a ? new[] { new PackageAsset(a.Type, a.Name, a.Path) } : Array.Empty<PackageAsset>());

    private static PackageDiscoveryReport Report(params PackageManifest[] manifests)
        => new(manifests.Select(m => new DiscoveredPackage(m, "root/" + m.Id)).ToArray(), Array.Empty<string>());

    private static LoadedPackage Package(
        string id, PackageKind kind, (string Version, (string Id, string Version)? Dep) spec)
    {
        var manifest = Pkg(id, spec.Version, kind: kind, dependsOn: spec.Dep);
        return new LoadedPackage(manifest, "root/" + id, Array.Empty<LoadedAsset>(), Array.Empty<LoadedPluginScript>());
    }
}
