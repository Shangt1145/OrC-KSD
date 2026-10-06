// [aura] field={{field}} amount={{amount}} filter={{auraFilter}}
var runtime = self is null ? null : Orc.Game.Effects.EffectRuntime.ResolveFor(self);
if (runtime is not null)
{
    await runtime.DeclareAuraAsync(self!, {{field}}, {{amount}}, {{auraFilter}}, ct);
}
