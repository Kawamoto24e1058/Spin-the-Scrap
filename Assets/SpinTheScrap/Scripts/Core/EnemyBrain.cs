using System;
using System.Numerics;

namespace SpinTheScrap.Core
{
    /// <summary>敵の数値（要件書 4.2 敵）。すべて調整前の初期値。</summary>
    public sealed class EnemyRules
    {
        public double WanderSpeed = 1.6;
        public double InvestigateSpeed = 2.8;
        /// <summary>プレイヤーの歩き（4m/s）より速く、走り（6.5m/s）より遅い。</summary>
        public double ChaseSpeed = 4.6;

        /// <summary>前方のこの距離 (m) 以内で、視線が通れば見つける。</summary>
        public double SightRange = 11;
        /// <summary>視野の半分の角度（度）。</summary>
        public double SightHalfAngle = 60;
        /// <summary>この距離 (m) 以内なら後ろにいても気づく。</summary>
        public double SenseRange = 2.5;
        /// <summary>見失ってから追うのをやめるまでの時間 (s)。</summary>
        public double LoseSightSeconds = 4;
        /// <summary>音のした場所に着いてから見回す時間 (s)。</summary>
        public double LookAroundSeconds = 3;

        public double AttackRange = 1.5;
        public double AttackDamage = 35;
        public double AttackCooldown = 1.2;

        /// <summary>この速さ (m/s) 以上で投げた物が当たるとひるむ。ためて投げれば届く。</summary>
        public double StunHitSpeed = 7;
        public double StunSeconds = 3;

        /// <summary>懐中電灯の光がこの距離 (m) 以内で当たると、プレイヤーの位置に気づく。</summary>
        public double FlashlightNoticeRange = 14;

        // ---- 音の大きさ（届く半径 m） ----
        public double RunNoiseRadius = 9;
        public double WalkNoiseRadius = 2.5;
        public double LandNoiseRadius = 6;
        /// <summary>ガラクタの衝突音。衝突の速さ (m/s) × この値。</summary>
        public double ImpactNoisePerSpeed = 1.6;
        public double MaxImpactNoiseRadius = 15;
        public double BreakNoiseRadius = 16;
        /// <summary>爆竹の音。敵をおびき寄せるため、どの音より遠くまで届く。</summary>
        public double FirecrackerNoiseRadius = 24;
        /// <summary>爆竹を投げてから鳴るまでの時間 (s)。</summary>
        public double FirecrackerFuseSeconds = 1.5;

        /// <summary>この速さ (m/s) 未満の衝突は音にならない。潜行の開始時にガラクタを 0.4m 上から置く着地（約 2.8m/s）は数えない。</summary>
        public double ImpactNoiseMinSpeed = 3;

        public double ImpactNoiseRadius(double speed)
        {
            if (speed < ImpactNoiseMinSpeed) return 0;
            return Math.Min(speed * ImpactNoisePerSpeed, MaxImpactNoiseRadius);
        }

        /// <summary>潜行に出る敵の数。サイクル 1 で 1 体、サイクルごとに +1、マネキンの首などの増加分を足す。入口以外の部屋数が上限。</summary>
        public int CountFor(int cycle, int bonus, int roomCount)
        {
            if (cycle < 1) throw new ArgumentOutOfRangeException(nameof(cycle));
            if (bonus < 0) throw new ArgumentOutOfRangeException(nameof(bonus));
            int rooms = Math.Max(0, roomCount - 1);
            return Math.Min(cycle + bonus, rooms);
        }
    }

    public enum EnemyState
    {
        Wander,
        Investigate,
        Chase,
        Stunned,
    }

    /// <summary>1 フレーム分の、敵が知り得たこと。</summary>
    public struct EnemyPerception
    {
        public Vector3 Position;
        /// <summary>敵の向き（水平、長さ 1）。</summary>
        public Vector3 Forward;
        public Vector3 PlayerPosition;
        /// <summary>敵の頭からプレイヤーまで遮る物がない。</summary>
        public bool HasLineOfSight;
        /// <summary>プレイヤーが倒れている（もう狙わない）。</summary>
        public bool PlayerDown;
        /// <summary>このフレームに聞こえた音の位置（なければ null）。</summary>
        public Vector3? HeardNoise;
        /// <summary>懐中電灯の光が当たった。</summary>
        public bool LitByFlashlight;
    }

    /// <summary>敵がこのフレームにすること。</summary>
    public struct EnemyDecision
    {
        public EnemyState State;
        /// <summary>向かう先（なければその場にとどまる）。</summary>
        public Vector3? MoveTarget;
        public double Speed;
        /// <summary>このフレームに攻撃する。</summary>
        public bool Attack;
    }

    /// <summary>
    /// 敵の判断（状態遷移）。エンジン非依存なので、フェーズ 1 ではホストだけがこれを動かして結果を配る。
    /// 徘徊 → 音や光に気づいたら調べに行く → 見つけたら追う → 見失ったら最後に見た場所を調べる。
    /// ためて投げた物を当てるとひるむ。
    /// </summary>
    public sealed class EnemyBrain
    {
        readonly EnemyRules rules;
        Vector3? investigateTarget;
        double lookAroundLeft;
        double lostSightFor;
        double attackCooldownLeft;
        double stunLeft;
        Vector3 lastSeenPlayer;

        public EnemyState State { get; private set; } = EnemyState.Wander;

        public EnemyBrain(EnemyRules enemyRules)
        {
            rules = enemyRules ?? throw new ArgumentNullException(nameof(enemyRules));
        }

        /// <summary>投げられた物が当たった。速ければひるむ。ひるんだら true。</summary>
        public bool OnHit(double impactSpeed)
        {
            if (impactSpeed < rules.StunHitSpeed) return false;
            State = EnemyState.Stunned;
            stunLeft = rules.StunSeconds;
            return true;
        }

        public bool CanSee(EnemyPerception p)
        {
            if (p.PlayerDown || !p.HasLineOfSight) return false;
            var to = p.PlayerPosition - p.Position;
            to.Y = 0;
            float distance = to.Length();
            if (distance <= rules.SenseRange) return true;
            if (distance > rules.SightRange) return false;
            double cos = Vector3.Dot(Vector3.Normalize(to), p.Forward);
            return cos >= Math.Cos(rules.SightHalfAngle * Math.PI / 180.0);
        }

        /// <param name="wanderTarget">徘徊中に向かう先（呼び出し側が部屋を選ぶ）。</param>
        public EnemyDecision Tick(double dt, EnemyPerception p, Vector3? wanderTarget)
        {
            if (attackCooldownLeft > 0) attackCooldownLeft = Math.Max(0, attackCooldownLeft - dt);

            if (State == EnemyState.Stunned)
            {
                stunLeft -= dt;
                if (stunLeft > 0) return new EnemyDecision { State = State };
                // ひるみが解けたら、プレイヤーが見えていれば追い、見えなければその場を調べる
                State = EnemyState.Investigate;
                investigateTarget = p.Position;
                lookAroundLeft = rules.LookAroundSeconds;
            }

            if (CanSee(p))
            {
                State = EnemyState.Chase;
                lastSeenPlayer = p.PlayerPosition;
                lostSightFor = 0;
            }
            else if (State == EnemyState.Chase)
            {
                lostSightFor += dt;
                if (lostSightFor >= rules.LoseSightSeconds || p.PlayerDown) StartInvestigate(lastSeenPlayer);
            }
            else if (p.LitByFlashlight && !p.PlayerDown)
            {
                StartInvestigate(p.PlayerPosition);
            }
            else if (p.HeardNoise.HasValue)
            {
                StartInvestigate(p.HeardNoise.Value);
            }

            switch (State)
            {
                case EnemyState.Chase:
                {
                    var to = p.PlayerPosition - p.Position;
                    to.Y = 0;
                    bool inReach = to.Length() <= rules.AttackRange && CanSee(p);
                    bool attack = inReach && attackCooldownLeft <= 0;
                    if (attack) attackCooldownLeft = rules.AttackCooldown;
                    return new EnemyDecision { State = State, MoveTarget = lastSeenPlayer, Speed = rules.ChaseSpeed, Attack = attack };
                }
                case EnemyState.Investigate:
                {
                    var target = investigateTarget.Value;
                    var to = target - p.Position;
                    to.Y = 0;
                    if (to.Length() > 0.8f) return new EnemyDecision { State = State, MoveTarget = target, Speed = rules.InvestigateSpeed };
                    lookAroundLeft -= dt;
                    if (lookAroundLeft > 0) return new EnemyDecision { State = State };
                    State = EnemyState.Wander;
                    investigateTarget = null;
                    goto default;
                }
                default:
                    return new EnemyDecision { State = EnemyState.Wander, MoveTarget = wanderTarget, Speed = rules.WanderSpeed };
            }
        }

        void StartInvestigate(Vector3 target)
        {
            State = EnemyState.Investigate;
            investigateTarget = target;
            lookAroundLeft = rules.LookAroundSeconds;
        }
    }
}
