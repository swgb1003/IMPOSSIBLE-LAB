using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 重心と転倒（地面を走る・置く発明）。部品の位置と重さ（MassModel）から重心を出し、
    /// 床に着いている範囲（車輪の接地、車輪が無ければ床に着いた部品の底）から重心が外に出る傾きを、倒れる角度とする。
    /// 前（+Z）を坂の上に向けて上るので、坂で効くのは後ろへ倒れる角度 θ = atan(d ÷ h)
    /// （d：重心から後ろの接地までの前後の距離、h：床からの重心の高さ）。止まった状態で見る（加速・減速・揺れは数えない）。
    /// </summary>
    public static class StabilityModel
    {
        /// <summary>床に着いているとみなす隙間（m）。</summary>
        const float Touch = 0.005f;

        public struct Result
        {
            /// <summary>床に着いている部品があり、計算できた。</summary>
            public bool Valid;
            /// <summary>重心（発明の座標。荷物を含む）。</summary>
            public Vector3 Center;
            /// <summary>床の高さ（発明の座標の y）。</summary>
            public float Floor;
            /// <summary>床に着いている範囲（発明の座標の x・z）。</summary>
            public float MinX, MaxX, MinZ, MaxZ;
            /// <summary>この角度を超える坂で倒れる（度）。後ろ・前・横。0 なら平らな床でも倒れる。</summary>
            public float TipBackDeg, TipFrontDeg, TipSideDeg;
            /// <summary>後ろに倒れたときの支点（後ろの接地。発明の座標）。</summary>
            public Vector3 BackPivot;
            /// <summary>後ろに倒れて、ほかの部品が床に着いて止まる角度（度）。</summary>
            public float RestBackDeg;
            public float TotalKg;
        }

        /// <summary>地面を走る・置く発明か（重心と転倒を計算する対象）。</summary>
        public static bool Applies(InventionDna dna) => dna?.Parts != null && dna.Parts.Count > 0 && dna.Mount == "ground";

        /// <summary>
        /// 荷物の中心（発明の座標）。格納部に収まる大きさなら格納部の中の底に置き、収まらなければ格納部（無ければいちばん大きい部品）の上に載せる。
        /// </summary>
        public static Vector3 PayloadCenter(InventionDna dna, float sizeM)
        {
            var parts = dna.Parts.Where(p => p.Type != "canopy" && p.Type != "wheel").ToList();
            if (parts.Count == 0) parts = dna.Parts.ToList();
            var holder = parts.Where(p => p.Type == "container").OrderByDescending(Volume).FirstOrDefault() ?? parts.OrderByDescending(Volume).First();
            var h = LayoutFixer.Half(holder);
            float s = Mathf.Max(0f, sizeM) * 0.5f;
            bool fits = holder.Type == "container" && s <= h.x && s <= h.y && s <= h.z;
            float y = fits ? holder.Position.y - h.y + s : holder.Position.y + h.y + s;
            return new Vector3(holder.Position.x, y, holder.Position.z);
        }

        /// <summary>
        /// 坂の実験で運ばせる荷物の一辺（m）。格納部があれば、その中に入る大きさ（5 cm 刻み・1 cm の余裕）まで小さくする。
        /// 格納部が無い、または入る大きさが size 以上なら size のまま。
        /// </summary>
        public static float FitPayloadSize(InventionDna dna, float size)
        {
            var holder = dna?.Parts?.Where(p => p.Type == "container").OrderByDescending(Volume).FirstOrDefault();
            if (holder == null) return size;
            var h = LayoutFixer.Half(holder);
            float inner = 2f * Mathf.Min(h.x, Mathf.Min(h.y, h.z)) - 0.01f;
            float fit = Mathf.Max(0.05f, Mathf.Floor(inner / 0.05f + 1e-4f) * 0.05f);
            return Mathf.Min(size, fit);
        }

        public static Result Evaluate(InventionDna dna, float payloadKg = 0f, float payloadSizeM = 0f)
        {
            var r = new Result();
            if (!Applies(dna)) return r;
            var solid = dna.Parts.Where(p => p.Type != "canopy").ToList();
            if (solid.Count == 0) return r;
            r.Floor = solid.Min(Bottom); // いちばん低い部品の底が床（ふつうは車輪。車輪より下に出た部品があれば、それが床に着く）

            // 床に着いている範囲：車輪は接地する線（車輪の幅 × 中心の前後位置）、ほかの部品は底の四角
            bool any = false;
            float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
            foreach (var p in solid)
            {
                if (Bottom(p) > r.Floor + Touch) continue;
                var h = LayoutFixer.Half(p);
                float x0 = p.Position.x - h.x, x1 = p.Position.x + h.x;
                float z0 = p.Type == "wheel" ? p.Position.z : p.Position.z - h.z, z1 = p.Type == "wheel" ? p.Position.z : p.Position.z + h.z;
                if (!any) { minX = x0; maxX = x1; minZ = z0; maxZ = z1; any = true; }
                else { minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1); minZ = Mathf.Min(minZ, z0); maxZ = Mathf.Max(maxZ, z1); }
            }
            if (!any) return r;
            r.MinX = minX; r.MaxX = maxX; r.MinZ = minZ; r.MaxZ = maxZ;

            // 重心：部品ごとの重さ × 位置（荷物を含む）
            Vector3 sum = Vector3.zero; float kg = 0f;
            foreach (var p in dna.Parts)
            {
                float m = MassModel.Part(p).Kg;
                sum += p.Position * m; kg += m;
            }
            if (payloadKg > 0f) { sum += PayloadCenter(dna, payloadSizeM) * payloadKg; kg += payloadKg; }
            if (kg <= 0f) return r;
            r.TotalKg = kg;
            r.Center = sum / kg;
            r.Valid = true;

            float hgt = Mathf.Max(1e-4f, r.Center.y - r.Floor);
            r.TipBackDeg = Angle(r.Center.z - minZ, hgt);
            r.TipFrontDeg = Angle(maxZ - r.Center.z, hgt);
            r.TipSideDeg = Angle(Mathf.Min(r.Center.x - minX, maxX - r.Center.x), hgt);
            r.BackPivot = new Vector3(r.Center.x, r.Floor, minZ);
            r.RestBackDeg = RestBack(dna, r.BackPivot);
            return r;
        }

        /// <summary>重心が支える範囲の端から d だけ内側、高さ h のとき、倒れる傾き（度）。外側なら 0。</summary>
        static float Angle(float d, float h) => d <= 0f ? 0f : Mathf.Atan2(d, h) * Mathf.Rad2Deg;

        /// <summary>
        /// 後ろの接地を支点に後ろへ倒れたとき、支点より後ろの部品の角が床に着いて止まる角度（度。着かなければ 90）。
        /// 前を上げる向きに φ 回すと、支点の後ろ dz・上 dy の点は dz sinφ + dy cosφ の高さに来るので、tanφ = dy ÷ |dz|。
        /// </summary>
        static float RestBack(InventionDna dna, Vector3 pivot)
        {
            float best = 90f;
            foreach (var p in dna.Parts)
            {
                if (p.Type == "canopy" || p.Type == "wheel") continue; // 車輪は丸いので転がる
                var h = LayoutFixer.Half(p);
                float dz = p.Position.z - h.z - pivot.z, dy = p.Position.y - h.y - pivot.y;
                if (dz >= -1e-4f) continue;
                best = Mathf.Min(best, Mathf.Atan2(Mathf.Max(0f, dy), -dz) * Mathf.Rad2Deg);
            }
            return Mathf.Max(best, 5f);
        }

        static float Bottom(PartSpec p) => p.Position.y - LayoutFixer.Half(p).y;
        static float Volume(PartSpec p) { var h = LayoutFixer.Half(p); return h.x * h.y * h.z; }
    }
}
