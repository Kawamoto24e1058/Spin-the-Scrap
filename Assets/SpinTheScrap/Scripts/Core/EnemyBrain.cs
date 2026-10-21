using System;
using System.Numerics;

namespace SpinTheScrap.Core
{
    public enum EnemyKind
    {
        /// <summary>這う者（雑魚）。群れで来る。倒すとガラクタを落とすことがある。</summary>
        Crawler,
        /// <summary>徘徊者。音と光に寄ってくる強い敵。</summary>
        Lurker,
        /// <summary>ボス（地下 3 階）：目の見えない巨体。音へ突進し、壁や柱にぶつかるとひるむ。</summary>
        Brute,
        /// <summary>ボス（地下 6 階）：電気を吸う機械。電気の武器か、ひるんでいるときしかまともに効かない。周りへ放電する。</summary>
        Dynamo,
        /// <summary>ボス（地下 9 階）：群れを呼ぶ人形。距離をとって這う者を呼ぶ。爆発に弱い。</summary>
        Puppeteer,
    }

    /// <summary>敵の数値（docs/floor-design.md「敵」）。すべて調整前の初期値。</summary>
    public sealed class EnemyRules
    {
        /// <summary>階層ごとの体力の倍率。</summary>
        public double HealthFloorGrowth = 1.25;
        /// <summary>階層ごとの攻撃の倍率。</summary>
        public double DamageFloorGrowth = 1.15;
        /// <summary>這う者を倒したときにガラクタを落とす確率。</summary>
        public double CrawlerScrapDropChance = 0.35;

        public double BaseHealth(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Crawler: return 40;
                case EnemyKind.Lurker: return 150;
                case EnemyKind.Brute: return 900;
                case EnemyKind.Dynamo: return 1400;
                case EnemyKind.Puppeteer: return 700;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static bool IsBoss(EnemyKind kind) => kind == EnemyKind.Brute || kind == EnemyKind.Dynamo || kind == EnemyKind.Puppeteer;

        // ---- ボスの数値 ----

        /// <summary>電気を吸う機械：電気以外の攻撃で受けるダメージの倍率（ひるんでいる間は等倍）。</summary>
        public double DynamoResistance = 0.3;
        /// <summary>電気を吸う機械：放電の間隔 (s)、光ってから撃つまで (s)、届く半径 (m)。</summary>
        public double DynamoDischargeInterval = 5;
        public double DynamoDischargeWarning = 1;
        public double DynamoDischargeRadius = 7;
        /// <summary>群れを呼ぶ人形：這う者を呼ぶ間隔 (s) と、同時に呼べる数。</summary>
        public double PuppeteerSummonInterval = 6;
        public int PuppeteerMaxMinions = 6;
        /// <summary>群れを呼ぶ人形：この距離 (m) より近づかれると離れる。</summary>
        public double PuppeteerKeepDistance = 9;
        /// <summary>群れを呼ぶ人形：爆発で受けるダメージの倍率。</summary>
        public double PuppeteerExplosionWeakness = 1.5;

        /// <summary>
        /// 敵が受けるダメージの倍率。電気を吸う機械は電気の武器かひるんでいるときだけ等倍、
        /// 群れを呼ぶ人形は爆発に弱い。
        /// </summary>
        public double DamageTakenMultiplier(EnemyKind kind, SymbolTag weaponFamily, bool explosive, bool stunned)
        {
            switch (kind)
            {
                case EnemyKind.Dynamo:
                    return weaponFamily == SymbolTag.Electric || stunned ? 1 : DynamoResistance;
                case EnemyKind.Puppeteer:
                    return explosive ? PuppeteerExplosionWeakness : 1;
                default:
                    return 1;
            }
        }

        public double BaseDamage(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Crawler: return 10;
                case EnemyKind.Lurker: return 35;
                case EnemyKind.Brute: return 45;
                case EnemyKind.Dynamo: return 30;
                case EnemyKind.Puppeteer: return 15;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public double HealthFor(EnemyKind kind, int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            return Math.Round(BaseHealth(kind) * Math.Pow(HealthFloorGrowth, floor - 1));
        }

        public double DamageFor(EnemyKind kind, int floor)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            return Math.Round(BaseDamage(kind) * Math.Pow(DamageFloorGrowth, floor - 1));
        }

        /// <summary>
        /// 敵の種類ごとの動き（速さ・見える距離・攻撃の届く距離と間隔・攻撃力）にした写しを作る。
        /// 音の大きさなど共通の値はそのまま引き継ぐ。
        /// </summary>
        public EnemyRules Profile(EnemyKind kind, int floor)
        {
            var p = (EnemyRules)MemberwiseClone();
            p.AttackDamage = DamageFor(kind, floor);
            switch (kind)
            {
                case EnemyKind.Crawler:
                    p.WanderSpeed = 1.4;
                    p.InvestigateSpeed = 3;
                    p.ChaseSpeed = 4.2;
                    p.SightRange = 14;
                    p.SightHalfAngle = 80;
                    p.SenseRange = 3;
                    p.AttackRange = 1.3;
                    p.AttackCooldown = 0.9;
                    p.StunSeconds = 1.5;
                    break;
                case EnemyKind.Brute:
                    // 目が見えない：視界の代わりに、すぐ近くと音だけで動く。突進は速い
                    p.WanderSpeed = 1.2;
                    p.InvestigateSpeed = 6;
                    p.ChaseSpeed = 6;
                    p.SightRange = 0;
                    p.SenseRange = 2.5;
                    p.AttackRange = 2.2;
                    p.AttackCooldown = 1.4;
                    p.StunSeconds = 4;
                    p.LookAroundSeconds = 1.5;
                    p.FlashlightNoticeRange = 0;
                    break;
                case EnemyKind.Dynamo:
                    // 重くて遅いが、よく見ている
                    p.WanderSpeed = 0.8;
                    p.InvestigateSpeed = 1.6;
                    p.ChaseSpeed = 2.2;
                    p.SightRange = 18;
                    p.SightHalfAngle = 90;
                    p.AttackRange = 2.4;
                    p.AttackCooldown = 1.6;
                    p.StunSeconds = 3;
                    p.LoseSightSeconds = 8;
                    break;
                case EnemyKind.Puppeteer:
                    // 見つけたら距離をとって呼び続ける（移動は Enemy 側で「離れる」に置き換える）
                    p.WanderSpeed = 1.4;
                    p.InvestigateSpeed = 2.5;
                    p.ChaseSpeed = 3.2;
                    p.SightRange = 20;
                    p.SightHalfAngle = 100;
                    p.SenseRange = 4;
                    p.AttackRange = 1.6;
                    p.AttackCooldown = 1;
                    p.StunSeconds = 2;
                    p.LoseSightSeconds = 10;
                    break;
            }
            return p;
        }

        /// <summary>通常の階で同時にいる這う者の数。倒されると湧き直す。</summary>
        public int CrawlerPopulationFor(int floor) => Math.Min(4 + floor, 12);

        /// <summary>這う者が湧き直す間隔 (s)。階層に長くいるほど短くなる（長居するほど危ない）。</summary>
        public double CrawlerRespawnSeconds(double secondsOnFloor) => Math.Max(4, 14 - secondsOnFloor / 30);

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

        /// <summary>
        /// 階層に出る徘徊者の数。階層 1〜2 で 1 体、2 階層ごとに +1、マネキンの首・呪いのラインなどの増加分を足す。
        /// 入口以外の部屋数が上限（1 部屋 1 体）。
        /// </summary>
        public int CountFor(int floor, int bonus, int roomCount)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            if (bonus < 0) throw new ArgumentOutOfRangeException(nameof(bonus));
            int rooms = Math.Max(0, roomCount - 1);
            return Math.Min(1 + (floor - 1) / 2 + bonus, rooms);
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
        /// <summary>プレイヤーの見つかりにくさ（人形の強化）。見える距離が (1 - Stealth) 倍になる。</summary>
        public double Stealth;
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
            if (distance > rules.SightRange * (1 - p.Stealth)) return false;
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
