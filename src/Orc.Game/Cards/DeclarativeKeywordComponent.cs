using Orc.Cards;

namespace Orc.Game.Cards;

/// <summary>Content-authored keyword behavior uses the same embedded effect lifecycle as built-ins.</summary>
public sealed class DeclarativeKeywordComponent : KeywordComponent
{
    private DeclarativeKeywordComponent(string id,Card card,int? value,IReadOnlyList<EffectSnapshot> snapshots):base(id,value)
    {
        foreach(var snapshot in snapshots)
        {
            var result=DynamicEffectFactory.Instantiate(card.Engine,snapshot);
            if(!result.Success)throw new InvalidDataException("Keyword behavior could not be instantiated: "+result.Error);
            EmbedEffect(result.Effect!);
        }
    }
    /// <summary>Use content-versioned ids so edits cannot change keywords in an existing match.</summary>
    public static void Register(string id,IReadOnlyList<EffectSnapshot> snapshots,bool battle=false)
    {
        if(snapshots.Count==0)throw new ArgumentException("Keyword behavior cannot be empty.",nameof(snapshots));
        var frozen=snapshots.ToArray();KeywordRegistry.RegisterOrReplace(id,(card,value)=>new DeclarativeKeywordComponent(id,card,value,frozen),battle);
    }
}
