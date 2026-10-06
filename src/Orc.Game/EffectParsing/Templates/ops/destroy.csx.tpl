// [destroy] 消灭（游戏层死亡链：亡计/词条注销/修饰清理/card.died） selector(sel={{sel}} side={{side}})
var runtime = self is null ? null : Orc.Game.Effects.EffectRuntime.ResolveFor(self);
if (runtime is not null)
{
    var targets = await runtime.SelectAsync(self!, new Orc.Game.Effects.EffectSelector({{sel}}, {{side}}, {{selZone}}, {{filterUnitType}}, {{filterKeyword}}, null, {{selThreshold}}));
    foreach (var target in targets)
    {
        await runtime.KillAsync(target, self, ct);
    }
}
