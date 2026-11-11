using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// REALITY の共通層：発明の総質量（推定）と、装着部位ごとの重さの目安（docs/GENERAL_INVENTION_DESIGN.md 5.5、docs/blocks.md 7章）。
    /// 部品は中空の殻と仮定し、重さ = min(中身が詰まった体積, 表面積 × 外殻の厚み) × 素材の密度。
    /// 発泡材は中身が詰まったもの、布は面密度 × 片面の面積、電池は中身が詰まった電池の密度で数える。
    /// 部品の大きさは発明DNAの scale（m。円柱・カプセルは x・z が直径、y が高さ）。
    /// </summary>
    public static class MassModel
    {
        public struct PartMass { public float SolidM3, AreaM2, Kg; }

        public static PartMass Part(PartSpec p, RealityCase c = RealityCase.Nominal)
        {
            var s = p.Scale;
            float x = Mathf.Abs(s.x), y = Mathf.Abs(s.y), z = Mathf.Abs(s.z);
            float solid, area;
            switch (p.ShapeOrDefault)
            {
                case "box":
                    solid = x * y * z; area = 2f * (x * y + y * z + z * x); break;
                case "sphere":
                    solid = Mathf.PI / 6f * x * y * z;
                    float d = (x + y + z) / 3f; area = Mathf.PI * d * d; break;
                default: // cylinder / capsule（カプセルも円柱として数える）
                    solid = Mathf.PI / 4f * x * z * y;
                    area = 2f * Mathf.PI / 4f * x * z + Mathf.PI * 0.5f * (x + z) * y; break;
            }
            float kg;
            if (p.Type == "battery") kg = solid * Coef("batteryDensity", c);
            else
            {
                string mat = p.MaterialOrDefault;
                float rho = LabCatalog.Material(mat)?.DensityKgM3 ?? 1000f;
                if (mat == "foam") kg = solid * rho;
                else if (mat == "fabric") kg = 0.5f * area * Coef("fabricArealDensity", c);
                else kg = Mathf.Min(solid, area * Coef("shellThicknessM", c)) * rho;
            }
            return new PartMass { SolidM3 = solid, AreaM2 = area, Kg = kg };
        }

        /// <summary>発明の総質量（kg）。悲観＝重い側、楽観＝軽い側。</summary>
        public static float TotalKg(InventionDna dna, RealityCase c = RealityCase.Nominal)
        {
            float sum = 0f;
            foreach (var p in dna.Parts) sum += Part(p, c).Kg;
            return sum;
        }

        /// <summary>装着部位ごとの重さの目安（kg）。体に付けないもの（床・空中・設置）は null。</summary>
        public static float? MountLimitKg(string mount)
        {
            switch (mount)
            {
                case "head": return BlockCoefficients.V("mountLimitHead");
                case "back": return BlockCoefficients.V("mountLimitBack");
                case "waist": return BlockCoefficients.V("mountLimitWaist");
                case "wrist": return BlockCoefficients.V("mountLimitWrist");
                case "hand": return BlockCoefficients.V("mountLimitHand");
                case "foot": return BlockCoefficients.V("mountLimitFoot");
                default: return null;
            }
        }

        // 悲観（重い）＝厚い殻・重い布・重い電池
        static float Coef(string id, RealityCase c)
        {
            var k = BlockCoefficients.Get(id);
            return c == RealityCase.Pessimistic ? k.Max : c == RealityCase.Optimistic ? k.Min : k.Value;
        }
    }

    /// <summary>
    /// 走行（drive）で坂を上る（docs/blocks.md 8章）。
    /// 上れる条件：坂の角度 ≤ 上れる角度（駆動の設計値）。速さは制限速度で一定（加速・減速は数えない）。
    /// 必要な電力 P = m g (sinθ + Crr cosθ) v ÷ 駆動効率（REALITY の参考値）。
    /// </summary>
    public static class DriveModel
    {
        public struct Result { public bool CanClimb; public float Speed, PowerW, TimeSec, ForceN; public string Reason; }

        public static Result Climb(float massKg, float speedLimit, float maxSlopeDeg, float slopeDeg, float lengthM)
        {
            var r = new Result { Speed = Mathf.Max(0.01f, speedLimit) };
            float th = slopeDeg * Mathf.Deg2Rad;
            r.ForceN = massKg * RealityModel.Gravity * (Mathf.Sin(th) + BlockCoefficients.V("rollingResistance") * Mathf.Cos(th));
            r.PowerW = r.ForceN * r.Speed / BlockCoefficients.V("driveEfficiency");
            r.CanClimb = slopeDeg <= maxSlopeDeg + 1e-4f;
            if (!r.CanClimb) r.Reason = $"坂の角度 {slopeDeg:0.0}° が上れる角度 {maxSlopeDeg:0.0}° を超える";
            r.TimeSec = lengthM / r.Speed;
            return r;
        }
    }
}
