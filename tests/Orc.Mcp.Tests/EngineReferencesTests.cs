using Orc.Core;
using Xunit;

namespace Orc.Mcp.Tests;

/// <summary>
/// 引擎托管引用表测试（Q22/Q22a/Q22b 决策）。不依赖卡牌构造——引用表对任意对象同构。
/// </summary>
public class EngineReferencesTests
{
    private sealed class Marker
    {
        public Marker(string name) => Name = name;

        public string Name { get; }
    }

    [Fact]
    public void Register_WithGuid_IsResolvable()
    {
        var table = new EngineReferences();
        var target = new Marker("a");
        var id = Guid.NewGuid();

        var key = table.Register(id, target);

        Assert.Equal(id.ToString("N"), key);
        Assert.True(table.TryResolve(key, out var resolved));
        Assert.Same(target, resolved);
        Assert.True(table.TryResolve(id.ToString("N"), out _));
    }

    [Fact]
    public void Register_SameObjectSameKey_IsIdempotent()
    {
        var table = new EngineReferences();
        var target = new Marker("a");
        var id = Guid.NewGuid();

        table.Register(id, target);
        table.Register(id, target);

        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Register_DifferentObjectOnSameKey_IsRejected()
    {
        var table = new EngineReferences();
        var id = Guid.NewGuid();

        table.Register(id, new Marker("first"));

        Assert.Throws<InvalidOperationException>(() => table.Register(id, new Marker("second")));
    }

    [Fact]
    public void Register_SameObjectOnDifferentKey_IsRejected()
    {
        var table = new EngineReferences();
        var target = new Marker("a");

        table.Register(Guid.NewGuid(), target);

        Assert.Throws<InvalidOperationException>(() => table.Register(Guid.NewGuid(), target));
    }

    [Fact]
    public void Assign_IsStableForSameObject()
    {
        var table = new EngineReferences();
        var target = new Marker("guid-less");

        var key = table.Assign(target);
        var again = table.Assign(target);

        Assert.Equal(key, again);
        Assert.True(table.TryResolve(key, out var resolved));
        Assert.Same(target, resolved);
        Assert.True(table.TryGetKey(target, out var reversed));
        Assert.Equal(key, reversed);
    }

    [Fact]
    public void Unregister_RemovesEntryAndIsIdempotent()
    {
        var table = new EngineReferences();
        var key = table.Assign(new Marker("a"));

        Assert.True(table.Unregister(key));
        Assert.False(table.TryResolve(key, out _));
        Assert.False(table.Unregister(key));
    }

    [Fact]
    public void TryResolve_WithExpectedType_MismatchReturnsFalse()
    {
        var table = new EngineReferences();
        var target = new Marker("a");
        var key = table.Assign(target);

        Assert.False(table.TryResolve<string>(key, out _));
        Assert.True(table.TryResolve<Marker>(key, out var resolved));
        Assert.Same(target, resolved);
    }

    [Fact]
    public void Clear_EmptiesTable()
    {
        var table = new EngineReferences();
        table.Assign(new Marker("a"));
        table.Register(Guid.NewGuid(), new Marker("b"));

        table.Clear();

        Assert.Equal(0, table.Count);
        Assert.Empty(table.Keys);
    }

    [Fact]
    public void Contains_HandlesBlankKey()
    {
        var table = new EngineReferences();

        Assert.False(table.Contains("   "));
        Assert.False(table.TryResolve("   ", out _));
    }
}
