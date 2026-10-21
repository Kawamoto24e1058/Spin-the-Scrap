using System;

namespace SpinTheScrap.Core
{
    /// <summary>道具の数値（要件書 4.2 道具）。すべて調整前の初期値。今は台車だけ。</summary>
    public sealed class ToolRules
    {
        /// <summary>台車そのものの重さ (kg)。</summary>
        public double CartMass = 15;
        /// <summary>空の台車を押すときの移動速度の倍率。</summary>
        public double CartEmptySpeedFactor = 0.85;
        /// <summary>CartFullLoadKg 積んだときの移動速度の倍率。</summary>
        public double CartFullSpeedFactor = 0.45;
        public double CartFullLoadKg = 60;

        /// <summary>この速さ (m/s) を超えて押すとガタガタ鳴る。</summary>
        public double CartRattleSpeed = 1.6;
        /// <summary>ガタガタ鳴る音が届く半径 (m)。積んでいる物が重いほど大きい。</summary>
        public double CartRattleNoiseRadius = 5;
        public double CartRattleNoisePerKg = 0.08;
        public double CartMaxRattleNoiseRadius = 10;

        /// <summary>積んだ重さから、押しているときの移動速度の倍率を求める。重いほど遅い。</summary>
        public double CartSpeedFactor(double loadKg)
        {
            if (loadKg < 0) throw new ArgumentOutOfRangeException(nameof(loadKg));
            double t = Math.Min(loadKg / CartFullLoadKg, 1);
            return CartEmptySpeedFactor + (CartFullSpeedFactor - CartEmptySpeedFactor) * t;
        }

        /// <summary>押す速さと積んだ重さから、ガタガタ鳴る音の半径を求める（鳴らなければ 0）。</summary>
        public double CartRattleRadius(double speed, double loadKg)
        {
            if (speed <= CartRattleSpeed) return 0;
            return Math.Min(CartRattleNoiseRadius + Math.Max(0, loadKg) * CartRattleNoisePerKg, CartMaxRattleNoiseRadius);
        }
    }
}
