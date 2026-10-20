using System;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Data
{
    /// <summary>効果 1 つ分。「条件」と「処理」の組み合わせで表す（要件書 8.3）。</summary>
    [Serializable]
    public sealed class EffectData
    {
        public ConditionKind condition = ConditionKind.Always;
        public SymbolTag conditionTag;
        [Min(1)] public int minCount = 1;
        [Tooltip("ConditionKind.Chance の成立確率（0〜1）")]
        public double probability;
        public EffectOp op = EffectOp.AddFlat;
        public double amount;
        [Tooltip("RetriggerSameColumnTag の対象")]
        public SymbolTag targetTag;
        [Tooltip("条件が不成立だったときの処理")]
        public EffectOp elseOp = EffectOp.None;
        public bool oncePerSpin;

        public EffectDefinition ToDefinition() =>
            new EffectDefinition(new EffectCondition(condition, conditionTag, minCount, probability), op, amount, targetTag, elseOp, oncePerSpin);

        public static EffectData From(EffectDefinition e) => new EffectData
        {
            condition = e.Condition.Kind,
            conditionTag = e.Condition.Tag,
            minCount = e.Condition.MinCount,
            probability = e.Condition.Probability,
            op = e.Op,
            amount = e.Amount,
            targetTag = e.TargetTag,
            elseOp = e.ElseOp,
            oncePerSpin = e.OncePerSpin,
        };
    }
}
