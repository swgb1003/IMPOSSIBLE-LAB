using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 電池の持ち（1日使う：daily_use）。式は docs/blocks.md 9章、決めたことは docs/DECISIONS.md D-45。
    /// 部品の消費電力・電池の容量・太陽電池の発電は品番の値を使い、品番のない部品は機能の値か、同じ種類の品番の中央値で見積もる。
    /// 使っている間ずっと動く部品（照明・センサーなど）と、必要なときだけ動く部品（モーターなど）に分け、後者は「動いている割合」だけ電力を使う。
    /// 動作プログラムで入り切りする機能の部品は、照明でも必要なときだけ動く部品として数える。動かす機能のないモーター・脚などは数えない。
    /// ゲーム・教育向けの簡易モデルで、実際の電池の持ちの予測ではない。
    /// </summary>
    public static class EnergyModel
    {
        /// <summary>使っている間ずっと動く部品の種類。</summary>
        public static readonly HashSet<string> Continuous = new HashSet<string> { "light", "display", "sensor", "radio", "heater", "cooler", "fan", "pump" };
        /// <summary>必要なときだけ動く部品の種類。</summary>
        public static readonly HashSet<string> Intermittent = new HashSet<string> { "motor", "leg", "thruster", "latch", "valve", "vibrator", "speaker" };
        /// <summary>電気を使うが品番がなく、電力を見積もれない種類。</summary>
        public static readonly HashSet<string> Unmodeled = new HashSet<string> { "stabilizer" };
        /// <summary>電気をためる部品の種類。</summary>
        public static readonly HashSet<string> Storage = new HashSet<string> { "battery", "capacitor" };
        public const string Solar = "solar";
        /// <summary>電気を使う機能（部品の種類によらず、機能の値から電力が分かる）。</summary>
        static readonly string[] PoweredBehaviors = { "heat", "cool", "illuminate", "detect" };
        /// <summary>部品を動かす機能（対象の部品を決めなくても、モーター・脚・推進ユニットを使う）。</summary>
        static readonly HashSet<string> MovingBehaviors = new HashSet<string> { "follow_user", "avoid_obstacle", "carry_load", "hover", "move_forward", "stabilize", "follow_head", "grip", "lift_load", "drive" };
        static readonly HashSet<string> Movers = new HashSet<string> { "motor", "leg", "thruster" };
        /// <summary>1日の流れ（再生用）：使い始める時刻と、日なたに出る時刻。</summary>
        public const float UseStartHour = 8f, SunStartHour = 10f;
        /// <summary>充電1回で使える日数の上限（発電で足りるときもこの値）。</summary>
        public const float MaxDays = 365f;
        public const float MaxHours = 10000f;

        public enum Source { Model, Behavior, TypeMedian, Mass }

        /// <summary>Switched：動作プログラムで入り切りするので、ずっと動く種類でも必要なときだけ動く部品として数えた。</summary>
        public struct Consumer { public string PartId, Type; public float Watts; public bool Continuous, Switched; public Source From; }
        public struct Store { public string PartId; public float Wh; public Source From; }

        public sealed class Result
        {
            /// <summary>使っている間の平均電力（W）。</summary>
            public float UsePowerW;
            /// <summary>1日に使う電力量（Wh）。</summary>
            public float DailyWh;
            /// <summary>1日の発電量（Wh）。</summary>
            public float SolarWh, SolarRatedW;
            /// <summary>電池の容量（Wh）と、そのうち使える分。</summary>
            public float NominalWh, UsableWh;
            /// <summary>電池で動ける時間（使っている間、時間）。</summary>
            public float BatteryHours;
            /// <summary>充電1回で使える日数（発電で足りるときは MaxDays）。</summary>
            public float ChargeDays;
            public bool NoBattery, SelfSufficient;
            /// <summary>満充電で使い始めて、1日の使う時間の途中で電池が切れる（使い始めからの時間）。</summary>
            public float? RunsOutAfterHours;
            public float HoursPerDay, ActiveRatio, SunHours;
            public readonly List<Consumer> Consumers = new List<Consumer>();
            public readonly List<Store> Stores = new List<Store>();
            /// <summary>電気を使うのに電力を見積もれなかった部品の id。</summary>
            public readonly List<string> Unknown = new List<string>();
            /// <summary>動かす機能がないので電力を数えなかった部品の id（モーター・脚など）。</summary>
            public readonly List<string> Idle = new List<string>();
            /// <summary>再生用：0〜24 時の電池の残り（%、0.1 時間ごと）。</summary>
            public float[] PercentByTenthHour = new float[0];

            public float PercentAt(float hour)
            {
                var a = PercentByTenthHour;
                if (a.Length == 0) return 0f;
                float x = Mathf.Clamp(hour * 10f, 0f, a.Length - 1);
                int i = Mathf.Min((int)x, a.Length - 2);
                return i < 0 ? a[0] : Mathf.Lerp(a[i], a[i + 1], x - i);
            }
        }

        /// <summary>電気を使う部品か機能がある（1日使う実験で測れる）。</summary>
        public static bool UsesPower(InventionDna dna) =>
            dna != null && (dna.Parts.Any(p => Continuous.Contains(p.Type) || Intermittent.Contains(p.Type) || Unmodeled.Contains(p.Type))
                            || dna.Behaviors.Any(b => PoweredBehaviors.Contains(b.Type)));

        static Dictionary<string, float> _medianPower, _medianCapacity;

        /// <summary>同じ種類の品番の仕様の中央値（品番がなければ null）。</summary>
        public static float? TypeMedian(string type, string spec)
        {
            var cache = spec == "powerW" ? (_medianPower ?? (_medianPower = new Dictionary<string, float>()))
                      : spec == "capacityWh" ? (_medianCapacity ?? (_medianCapacity = new Dictionary<string, float>())) : null;
            if (cache != null && cache.TryGetValue(type, out var hit)) return hit > 0f ? hit : (float?)null;
            var values = PartModels.OfType(type).Select(m => m.Spec(spec) ?? 0f).Where(v => v > 0f).OrderBy(v => v).ToList();
            float med = values.Count == 0 ? 0f : values.Count % 2 == 1 ? values[values.Count / 2] : 0.5f * (values[values.Count / 2 - 1] + values[values.Count / 2]);
            if (cache != null) cache[type] = med;
            return med > 0f ? med : (float?)null;
        }

        /// <summary>動作プログラムで入り切りする機能（set_behavior の対象）。</summary>
        public static bool Switched(InventionDna dna, string behaviorType) =>
            dna?.Program != null && dna.Program.Rules.Any(r => r.Enabled && r.Do.Any(a => a.Action == ProgramAction.SetBehavior && a.Behavior == behaviorType));

        /// <summary>
        /// 必要なときだけ動く部品に、動かすものがあるか：対象にする機能・その部品の関節、
        /// モーター・脚・推進ユニットは部品を動かす機能（走行・浮遊など）、モーターはほかの部品の関節も。
        /// </summary>
        public static bool IsMoved(InventionDna dna, PartSpec p) =>
            dna.Behaviors.Any(b => b.TargetPart == p.Id) || p.Joint != null
            || (Movers.Contains(p.Type) && dna.Behaviors.Any(b => MovingBehaviors.Contains(b.Type)))
            || (p.Type == "motor" && dna.Parts.Any(x => x.Joint != null));

        /// <summary>
        /// 部品の消費電力：加温・冷却の機能の電力 → 品番の消費電力 → 照明・検知の機能の値 → 同じ種類の品番の中央値 の順。
        /// 電気を使わない部品と、動かす機能のない「必要なときだけ動く部品」は null。対象にする機能を動作プログラムで入り切りする部品は Switched。
        /// </summary>
        public static Consumer? PartPower(InventionDna dna, PartSpec p)
        {
            var c = PartPowerRaw(dna, p);
            if (!c.HasValue) return null;
            var v = c.Value;
            if (v.Continuous && dna.Behaviors.Any(b => b.TargetPart == p.Id && Switched(dna, b.Type))) { v.Continuous = false; v.Switched = true; }
            return v;
        }

        static Consumer? PartPowerRaw(InventionDna dna, PartSpec p)
        {
            bool cont = Continuous.Contains(p.Type), inter = Intermittent.Contains(p.Type);
            if (inter && !IsMoved(dna, p)) return null;
            var targeting = dna.Behaviors.Where(b => b.TargetPart == p.Id).ToList();
            var thermal = targeting.FirstOrDefault(b => b.Type == "heat" || b.Type == "cool");
            if (thermal != null && thermal.Parameters.TryGetValue("powerW", out float tw))
                return new Consumer { PartId = p.Id, Type = p.Type, Watts = Mathf.Max(0f, tw), Continuous = true, From = Source.Behavior };
            if (!cont && !inter) return null;
            var model = p.ModelInfo;
            float? mw = model?.Spec("powerW");
            if (mw.HasValue && mw.Value > 0f) return new Consumer { PartId = p.Id, Type = p.Type, Watts = mw.Value, Continuous = cont, From = Source.Model };
            var lamp = targeting.FirstOrDefault(b => b.Type == "illuminate");
            if (lamp != null) return new Consumer { PartId = p.Id, Type = p.Type, Watts = LampWatts(lamp), Continuous = true, From = Source.Behavior };
            if (targeting.Any(b => b.Type == "detect")) return new Consumer { PartId = p.Id, Type = p.Type, Watts = BlockCoefficients.V("detectPowerW"), Continuous = true, From = Source.Behavior };
            var med = TypeMedian(p.Type, "powerW");
            if (med.HasValue) return new Consumer { PartId = p.Id, Type = p.Type, Watts = med.Value, Continuous = cont, From = Source.TypeMedian };
            return null;
        }

        static float LampWatts(BehaviorSpec b) => Mathf.Max(0f, b.Parameters.TryGetValue("lumens", out var lm) ? lm : 150f) / BlockCoefficients.V("ledEfficacy");

        /// <summary>電池・キャパシタの容量（Wh、使える割合をかける前）。品番の容量（大きさの違いは体積の比）→ 電池は重さ × エネルギー密度 → 同じ種類の中央値。</summary>
        public static Store? PartStore(PartSpec p, RealityCase c = RealityCase.Nominal)
        {
            if (!Storage.Contains(p.Type)) return null;
            var model = p.ModelInfo;
            float? cap = model?.Spec("capacityWh");
            if (cap.HasValue && cap.Value > 0f) return new Store { PartId = p.Id, Wh = cap.Value * PartModels.SizeRatio(p, model), From = Source.Model };
            if (p.Type == "battery") return new Store { PartId = p.Id, Wh = MassModel.Part(p, c).Kg * Reality("batteryEnergyDensity", 190f, c), From = Source.Mass };
            var med = TypeMedian(p.Type, "capacityWh");
            return med.HasValue ? new Store { PartId = p.Id, Wh = med.Value, From = Source.TypeMedian } : (Store?)null;
        }

        static float Reality(string id, float fallback, RealityCase c)
        {
            var coef = RealityModel.Default?.Get(id);
            return coef == null ? fallback : coef.Pick(c);
        }

        /// <param name="hoursPerDay">1日に使う時間（時間）。</param>
        /// <param name="activePercent">必要なときだけ動く部品（動作プログラムで入り切りする部品を含む）が、使う時間のうち動いている割合（%）。</param>
        /// <param name="sunHours">太陽電池が日なたにある時間（時間）。</param>
        public static Result Evaluate(InventionDna dna, float hoursPerDay, float activePercent, float sunHours)
        {
            var r = new Result { HoursPerDay = Mathf.Clamp(hoursPerDay, 0f, 24f), ActiveRatio = Mathf.Clamp01(activePercent / 100f), SunHours = Mathf.Clamp(sunHours, 0f, 24f) };
            var counted = new HashSet<string>();
            foreach (var p in dna.Parts)
            {
                var c = PartPower(dna, p);
                if (c.HasValue) { r.Consumers.Add(c.Value); counted.Add(p.Id); }
                else if (Unmodeled.Contains(p.Type)) r.Unknown.Add(p.Id);
                else if (Intermittent.Contains(p.Type)) r.Idle.Add(p.Id);
                var s = PartStore(p);
                if (s.HasValue) r.Stores.Add(s.Value);
                if (p.Type == Solar)
                {
                    float? w = p.ModelInfo?.Spec("powerW") ?? TypeMedian(Solar, "powerW");
                    if (w.HasValue) r.SolarRatedW += w.Value;
                }
            }
            // 電気を使う部品の種類でない部品（本体など）に付いた機能の電力も数える
            foreach (var b in dna.Behaviors)
            {
                if (!PoweredBehaviors.Contains(b.Type) || (b.TargetPart != null && counted.Contains(b.TargetPart))) continue;
                float w = b.Type == "illuminate" ? LampWatts(b)
                        : b.Type == "detect" ? BlockCoefficients.V("detectPowerW")
                        : b.Parameters.TryGetValue("powerW", out var pw) ? Mathf.Max(0f, pw) : 0f;
                if (w <= 0f) continue;
                bool sw = Switched(dna, b.Type);
                r.Consumers.Add(new Consumer { PartId = b.TargetPart ?? "", Type = b.Type, Watts = w, Continuous = !sw, Switched = sw, From = Source.Behavior });
                if (b.TargetPart != null) counted.Add(b.TargetPart);
            }
            float cont = r.Consumers.Where(x => x.Continuous).Sum(x => x.Watts), inter = r.Consumers.Where(x => !x.Continuous).Sum(x => x.Watts);
            r.UsePowerW = cont + inter * r.ActiveRatio;
            r.DailyWh = r.UsePowerW * r.HoursPerDay;
            r.SolarWh = r.SolarRatedW * r.SunHours * BlockCoefficients.V("solarDerate");
            r.NominalWh = r.Stores.Sum(x => x.Wh);
            r.UsableWh = r.NominalWh * Reality("usableFraction", 0.8f, RealityCase.Nominal);
            r.NoBattery = r.UsableWh <= 0f;
            r.BatteryHours = r.UsePowerW > 0f ? Mathf.Min(MaxHours, r.UsableWh / r.UsePowerW) : (r.NoBattery ? 0f : MaxHours);
            float net = r.DailyWh - r.SolarWh;
            if (r.NoBattery) r.ChargeDays = 0f;
            else if (net <= 1e-6f) { r.ChargeDays = MaxDays; r.SelfSufficient = r.SolarWh > 0f; }
            else r.ChargeDays = Mathf.Min(MaxDays, r.UsableWh / net);
            r.PercentByTenthHour = Timeline(r);
            float start = UseStart(r.HoursPerDay);
            for (int i = 0; i < r.PercentByTenthHour.Length; i++)
                if (r.PercentByTenthHour[i] <= 0f && !r.NoBattery) { float h = i / 10f - start; if (h > 0f && h <= r.HoursPerDay + 1e-3f) r.RunsOutAfterHours = h; break; }
            return r;
        }

        /// <summary>使い始める時刻（使う時間が長くて 24 時を越えるときは前にずらす）。</summary>
        public static float UseStart(float hoursPerDay) => Mathf.Clamp(24f - hoursPerDay, 0f, UseStartHour);

        /// <summary>満充電から 1 日を 0.1 時間ずつ進めた電池の残り（%）。使う時間は電力を使い、日なたの時間は発電でためる。</summary>
        static float[] Timeline(Result r)
        {
            var a = new float[241];
            if (r.NoBattery) return a;
            float e = r.UsableWh, use0 = UseStart(r.HoursPerDay), sun0 = Mathf.Min(SunStartHour, 24f - r.SunHours);
            a[0] = 100f;
            for (int i = 1; i < a.Length; i++)
            {
                float t = (i - 0.5f) / 10f;
                if (t >= use0 && t < use0 + r.HoursPerDay) e -= r.UsePowerW * 0.1f;
                if (t >= sun0 && t < sun0 + r.SunHours) e += r.SolarRatedW * BlockCoefficients.V("solarDerate") * 0.1f;
                e = Mathf.Clamp(e, 0f, r.UsableWh);
                a[i] = 100f * e / r.UsableWh;
            }
            return a;
        }

        public static string SourceLabel(Source s) =>
            s == Source.Model ? "品番" : s == Source.Behavior ? "機能の値" : s == Source.Mass ? "重さから" : "同じ種類の品番の中央値";
    }

    /// <summary>
    /// 部品代（推定）：品番の値段の合計。品番のない部品は同じ種類の品番の中央値で見積もり、種類に品番が 1 つもない部品（本体・骨組みなど）は数えない。
    /// 加工・組み立ての費用、送料、税は入れない。
    /// </summary>
    public static class CostModel
    {
        public sealed class Result
        {
            public float Jpy;
            public int FromModel, FromMedian;
            /// <summary>数えなかった部品の種類（日本語）。</summary>
            public readonly List<string> Uncounted = new List<string>();
        }

        static readonly Dictionary<string, float> _median = new Dictionary<string, float>();

        public static float? TypeMedianJpy(string type)
        {
            if (!_median.TryGetValue(type, out var med))
            {
                var v = PartModels.OfType(type).Select(m => m.PriceJpy).Where(x => x > 0f).OrderBy(x => x).ToList();
                med = v.Count == 0 ? 0f : v.Count % 2 == 1 ? v[v.Count / 2] : 0.5f * (v[v.Count / 2 - 1] + v[v.Count / 2]);
                _median[type] = med;
            }
            return med > 0f ? med : (float?)null;
        }

        public static Result Evaluate(InventionDna dna)
        {
            var r = new Result();
            foreach (var p in dna.Parts)
            {
                var m = p.ModelInfo;
                if (m != null && m.PriceJpy > 0f) { r.Jpy += m.PriceJpy; r.FromModel++; continue; }
                var med = TypeMedianJpy(p.Type);
                if (med.HasValue) { r.Jpy += med.Value; r.FromMedian++; continue; }
                r.Uncounted.Add(LabCatalog.Part(p.Type)?.LabelJa ?? p.Type);
            }
            return r;
        }
    }
}
