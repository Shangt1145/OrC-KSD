using Orc.Core;
using Orc.Packages;
using Xunit;

namespace Orc.Packages.Tests;

/// <summary>
/// 资产化 S6 验收：版本约束解析/匹配；版本选择（满足约束取最高版）；依赖拓扑排序；失败隔离（缺失／无满足版本／循环）。
/// </summary>
public class PackageResolverTests
{
    // ---------- 版本约束 ----------

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.*.*", true)]
    [InlineData("1.2.*", true)]
    [InlineData("1.^.*", true)]
    [InlineData("1.^2.*", true)]
    [InlineData("1.2.^1", true)]
    [InlineData("1.2", false)]
    [InlineData("*.1.1", false)]
    [InlineData("1.x.3", false)]
    [InlineData("1.^x.3", false)]
    public void 版本约束解析(string text, bool expected)
        => Assert.Equal(expected, AssetVersionConstraint.TryParse(text, out _));

    [Fact]
    public void 数字下界_该段须不小于下界()
    {
        var constraint = AssetVersionConstraint.Parse("1.^2.*");

        Assert.False(constraint.IsSatisfiedBy(new AssetVersion(1, 1, 9)));
        Assert.True(constraint.IsSatisfiedBy(new AssetVersion(1, 2, 0)));
        Assert.True(constraint.IsSatisfiedBy(new AssetVersion(1, 7, 3)));
        Assert.False(constraint.IsSatisfiedBy(new AssetVersion(2, 2, 0)));

        // `^` 省略数字＝下界 0（任意）——与既有语义兼容
        var anyBound = AssetVersionConstraint.Parse("1.^.*");
        Assert.True(anyBound.IsSatisfiedBy(new AssetVersion(1, 0, 0)));
        Assert.Equal("1.^.*", anyBound.ToString());
        Assert.Equal("1.^2.*", constraint.ToString());
    }

    [Fact]
    public void 版本约束匹配_主版锁定与通配()
    {
        var exact = AssetVersionConstraint.Parse("1.2.3");
        Assert.True(exact.IsSatisfiedBy(new AssetVersion(1, 2, 3)));
        Assert.False(exact.IsSatisfiedBy(new AssetVersion(1, 2, 4)));
        Assert.False(exact.IsSatisfiedBy(new AssetVersion(2, 2, 3)));

        var wildcard = AssetVersionConstraint.Parse("1.2.*");
        Assert.True(wildcard.IsSatisfiedBy(new AssetVersion(1, 2, 9)));
        Assert.False(wildcard.IsSatisfiedBy(new AssetVersion(1, 3, 0)));

        var atLeast = AssetVersionConstraint.Parse("1.^.*");
        Assert.True(atLeast.IsSatisfiedBy(new AssetVersion(1, 0, 0)));
        Assert.True(atLeast.IsSatisfiedBy(new AssetVersion(1, 9, 9)));
        Assert.False(atLeast.IsSatisfiedBy(new AssetVersion(2, 0, 0)));
    }

    [Fact]
    public void 版本约束_往返一致()
    {
        foreach (var text in new[] { "1.2.3", "1.*.*", "1.2.*", "1.^.*" })
        {
            Assert.Equal(text, AssetVersionConstraint.Parse(text).ToString());
        }
    }

    // ---------- 解析与排序 ----------

    [Fact]
    public void 依赖先于依赖方_同层按声明序()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("b", "1.0.0", deps: [new PackageDependency("a", "1.0.0")]),
            Pkg("a", "1.0.0")));

        Assert.Empty(report.Failures);
        Assert.Equal(new[] { "a", "b" }, report.LoadOrder.Select(p => p.Manifest.Id));
    }

    [Fact]
    public void 版本选择_满足约束取最高版()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("b", "1.0.0", deps: [new PackageDependency("a", "1.*.*")]),
            Pkg("a", "1.0.0"),
            Pkg("a", "1.3.0"),
            Pkg("a", "1.1.0")));

        // 多版本并存：a 的三个版本均参与加载（目录即加载集）；b 解析到最高满足版（1.3.0）——b 须在其之后
        Assert.Empty(report.Failures);
        Assert.Equal(4, report.LoadOrder.Count);

        var indexOfB = report.LoadOrder.ToList().FindIndex(p => p.Manifest.Id == "b");
        var indexOfA130 = report.LoadOrder.ToList().FindIndex(
            p => p.Manifest.Id == "a" && p.Manifest.Version == new AssetVersion(1, 3, 0));

        Assert.True(indexOfB > indexOfA130, $"b 应在 a@1.3.0 之后（b={indexOfB}, a@1.3.0={indexOfA130}）。");
    }

    [Fact]
    public void 依赖缺失_隔离该包且不阻断其余()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("good", "1.0.0"),
            Pkg("bad", "1.0.0", deps: [new PackageDependency("missing", "1.0.0")])));

        Assert.Single(report.LoadOrder);
        Assert.Equal("good", report.LoadOrder[0].Manifest.Id);
        Assert.Single(report.Failures);
        Assert.Contains("missing", report.Failures[0]);
    }

    [Fact]
    public void 无满足版本_隔离()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("b", "1.0.0", deps: [new PackageDependency("a", "2.0.0")]),
            Pkg("a", "1.0.0")));

        Assert.Single(report.LoadOrder);
        Assert.Equal("a", report.LoadOrder[0].Manifest.Id);
        Assert.Contains("无满足约束", report.Failures[0]);
    }

    [Fact]
    public void 无效传递_连带隔离()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("c", "1.0.0", deps: [new PackageDependency("b", "1.0.0")]),
            Pkg("b", "1.0.0", deps: [new PackageDependency("missing", "1.0.0")])));

        Assert.Empty(report.LoadOrder);
        Assert.Equal(2, report.Failures.Count);
        Assert.Contains(report.Failures, f => f.Contains("传递"));
    }

    [Fact]
    public void 循环依赖_隔离()
    {
        var report = PackageResolver.Resolve(Report(
            Pkg("a", "1.0.0", deps: [new PackageDependency("b", "1.0.0")]),
            Pkg("b", "1.0.0", deps: [new PackageDependency("a", "1.0.0")])));

        Assert.Empty(report.LoadOrder);
        Assert.Equal(2, report.Failures.Count);
        Assert.All(report.Failures, f => Assert.Contains("循环", f));
    }

    [Fact]
    public void 发现阶段失败_并入报告()
    {
        var report = PackageResolver.Resolve(new PackageDiscoveryReport(
            new[] { Pkg("a", "1.0.0") }, new[] { "x/manifest.json：缺少清单文件" }));

        Assert.Single(report.LoadOrder);
        Assert.Single(report.Failures);
    }

    private static DiscoveredPackage Pkg(
        string id, string version, PackageDependency[]? deps = null)
        => new(
            new PackageManifest(
                PackageKind.Plugin, id, Guid.NewGuid(), AssetVersion.Parse(version),
                deps ?? Array.Empty<PackageDependency>(), Array.Empty<PackageAsset>()),
            "root/" + id + "/" + version);

    private static PackageDiscoveryReport Report(params DiscoveredPackage[] packages)
        => new(packages, Array.Empty<string>());
}
