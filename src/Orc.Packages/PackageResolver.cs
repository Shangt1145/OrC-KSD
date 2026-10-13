using Orc.Core;

namespace Orc.Packages;

/// <summary>已解析的包（可加载）：清单 ＋ 包根目录。</summary>
/// <param name="Manifest">清单。</param>
/// <param name="RootDirectory">包根目录。</param>
public sealed record ResolvedPackage(PackageManifest Manifest, string RootDirectory);

/// <summary>包解析报告：加载顺序（＝拓扑序 ＋ 声明序）＋ 失败明细（隔离整包、不阻断其余）。</summary>
/// <param name="LoadOrder">加载顺序（依赖先于依赖方；同层按声明序）。</param>
/// <param name="Failures">失败明细（<c>包：原因</c>）。</param>
public sealed record PackageResolutionReport(
    IReadOnlyList<ResolvedPackage> LoadOrder,
    IReadOnlyList<string> Failures);

/// <summary>
/// 包解析（S6）：①版本选择（依赖 → 满足约束的**最高版**；多版本并存下逐依赖择版）＋②依赖拓扑排序
/// （依赖先于依赖方；同层＝声明序）＋③失败隔离（依赖缺失／无满足版本／依赖无效传递／循环依赖＝隔离该包，
/// 不阻断其余）。全部发现到的包均参与加载（无"根集裁剪"——目录即加载集）。
/// </summary>
public static class PackageResolver
{
    /// <summary>解析并排序（输入＝发现报告；输出＝加载顺序＋失败明细）。</summary>
    /// <exception cref="ArgumentNullException">discovery 为 null。</exception>
    public static PackageResolutionReport Resolve(PackageDiscoveryReport discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        var packages = discovery.Packages;
        var failures = new List<string>(discovery.Failures);
        if (packages.Count == 0)
        {
            return new PackageResolutionReport(Array.Empty<ResolvedPackage>(), failures);
        }

        var byId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < packages.Count; i++)
        {
            var id = packages[i].Manifest.Id;
            if (!byId.TryGetValue(id, out var list))
            {
                list = new List<int>();
                byId.Add(id, list);
            }

            list.Add(i);
        }

        // ① 依赖边（依赖 → 被选中的具体版本）；失败＝该包无效
        var edges = new List<int>[packages.Count];
        var invalidReason = new string?[packages.Count];
        for (var i = 0; i < packages.Count; i++)
        {
            var manifest = packages[i].Manifest;
            var deps = new List<int>();
            edges[i] = deps;

            foreach (var dependency in manifest.Dependencies)
            {
                if (!byId.TryGetValue(dependency.Id, out var candidates))
                {
                    invalidReason[i] = $"依赖 '{dependency.Id}' 未找到（缺失）";
                    break;
                }

                if (!AssetVersionConstraint.TryParse(dependency.Version, out var constraint))
                {
                    invalidReason[i] = $"依赖 '{dependency.Id}' 的版本约束 '{dependency.Version}' 非法";
                    break;
                }

                var selected = SelectHighest(packages, candidates, constraint);
                if (selected < 0)
                {
                    invalidReason[i] = $"依赖 '{dependency.Id}' 无满足约束 '{dependency.Version}' 的版本";
                    break;
                }

                deps.Add(selected);
            }
        }

        // ③ 无效传递（依赖到无效包 ⇒ 本包无效）
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < packages.Count; i++)
            {
                if (invalidReason[i] is not null)
                {
                    continue;
                }

                foreach (var dep in edges[i])
                {
                    if (invalidReason[dep] is not null)
                    {
                        invalidReason[i] = $"依赖 '{packages[dep].Manifest.Id}' 无效（传递）";
                        changed = true;
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < packages.Count; i++)
        {
            if (invalidReason[i] is { } reason)
            {
                var m = packages[i].Manifest;
                failures.Add($"{m.Id}@{m.Version}：{reason}");
            }
        }

        // ② 拓扑排序（依赖先于依赖方；同层按声明序）；剩余＝循环依赖
        var indegree = new int[packages.Count];
        for (var i = 0; i < packages.Count; i++)
        {
            if (invalidReason[i] is not null)
            {
                continue;
            }

            foreach (var dep in edges[i])
            {
                if (invalidReason[dep] is null)
                {
                    indegree[i]++;
                }
            }
        }

        var order = new List<ResolvedPackage>();
        var emitted = new bool[packages.Count];
        for (var step = 0; step < packages.Count; step++)
        {
            var next = -1;
            for (var i = 0; i < packages.Count; i++)
            {
                if (!emitted[i] && invalidReason[i] is null && indegree[i] == 0)
                {
                    next = i;
                    break;
                }
            }

            if (next < 0)
            {
                break;
            }

            emitted[next] = true;
            var manifest = packages[next].Manifest;
            order.Add(new ResolvedPackage(manifest, packages[next].RootDirectory));

            for (var i = 0; i < packages.Count; i++)
            {
                if (emitted[i] || invalidReason[i] is not null)
                {
                    continue;
                }

                if (edges[i].Contains(next))
                {
                    indegree[i]--;
                }
            }
        }

        for (var i = 0; i < packages.Count; i++)
        {
            if (!emitted[i] && invalidReason[i] is null)
            {
                var m = packages[i].Manifest;
                failures.Add($"{m.Id}@{m.Version}：依赖存在循环（无法拓扑排序）");
            }
        }

        return new PackageResolutionReport(order, failures);
    }

    private static int SelectHighest(
        IReadOnlyList<DiscoveredPackage> packages, List<int> candidates, AssetVersionConstraint constraint)
    {
        var best = -1;
        for (var k = 0; k < candidates.Count; k++)
        {
            var index = candidates[k];
            var version = packages[index].Manifest.Version;
            if (!constraint.IsSatisfiedBy(version))
            {
                continue;
            }

            if (best < 0 || version.CompareTo(packages[best].Manifest.Version) > 0)
            {
                best = index;
            }
        }

        return best;
    }
}
