using System.Collections.Generic;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>S02 の機能対応表の1行（表示用）。</summary>
    public sealed class FunctionRow
    {
        public string Need = "", Status = "simulated", Note = "";
        /// <summary>使う機能ブロック（日本語、「・」区切り）。</summary>
        public string BlocksJa = "";
        public List<string> Blocks = new List<string>();
        /// <summary>計測項目で確かめる要望の計測項目（キー）と、その名前（「・」区切り）。</summary>
        public List<string> Metrics = new List<string>();
        public string MetricsJa = "";
        /// <summary>結果画面用：表示中の実験で、この要望の機能が動いたか（ForResult で入る）。</summary>
        public bool CoveredHere;
        /// <summary>この要望を確かめる実験計画の候補（なければ null）。</summary>
        public string PlanTitle;
        public bool Verifiable => Status != "unverifiable";
        /// <summary>「検証する／単純化」なのに、どの候補の実験でも使う機能が動かない。</summary>
        public bool NoPlan => Verifiable && PlanTitle == null;
    }

    /// <summary>
    /// 機能対応表の表示（6週目 S02, docs/GENERAL_INVENTION_DESIGN.md 7章）。要望ごとに、検証の状態・使う機能・確かめる実験計画を並べる。
    /// AI の対応表がない設計（オフラインのサンプル設計図など）は、機能ごとの行を作る（Derived = true。AI の判断ではないことを画面に出す）。
    /// </summary>
    public static class FunctionMapView
    {
        public static List<FunctionRow> Build(InventionDna dna, IList<PlanCandidate> candidates, out bool derived)
        {
            var rows = new List<FunctionRow>();
            derived = false;
            if (dna == null) return rows;
            var exercised = (candidates ?? new List<PlanCandidate>()).Select(c => (c.Title, Blocks: PlanCandidates.BlocksExercised(c.Plan, dna), Metrics: Measurable(c.Plan, dna))).ToList();
            string PlanFor(IEnumerable<string> blocks, IEnumerable<string> metrics = null) =>
                exercised.FirstOrDefault(e => blocks.Any(b => e.Blocks.Contains(b)) || (metrics ?? Enumerable.Empty<string>()).Any(m => e.Metrics.Contains(m))).Title;

            if (dna.FunctionMap.Count > 0)
            {
                foreach (var n in dna.FunctionMap)
                    rows.Add(new FunctionRow
                    {
                        Need = n.Need, Status = n.Status, Note = n.Note ?? "",
                        BlocksJa = string.Join("・", n.Blocks.Select(b => LabCatalog.Behavior(b)?.LabelJa ?? b)),
                        Blocks = new List<string>(n.Blocks),
                        Metrics = new List<string>(n.Metrics),
                        MetricsJa = string.Join("・", n.Metrics.Select(m => ExperimentCatalog.Metric(m)?.LabelJa ?? m)),
                        // 動きで見せる要望は、動作プログラムの実験（近づいて離れる）で再現する
                        PlanTitle = n.Status == "unverifiable" ? null
                            : n.Status == "demonstrated" && dna.Program != null ? exercised.FirstOrDefault(e => e.Title == "近づいて離れる").Title ?? PlanFor(n.Blocks)
                            : PlanFor(n.Blocks, n.Metrics),
                    });
                return rows;
            }

            derived = true;
            foreach (var b in dna.Behaviors)
            {
                var info = LabCatalog.Behavior(b.Type);
                string plan = PlanFor(new[] { b.Type });
                rows.Add(new FunctionRow
                {
                    Need = info?.LabelJa ?? b.Type,
                    BlocksJa = info?.LabelJa ?? b.Type,
                    Blocks = new List<string> { b.Type },
                    Status = plan != null ? "simulated" : "unverifiable",
                    Note = plan != null ? "" : "この版の実験では動かない（試用で動きを見るだけ）",
                    PlanTitle = plan,
                });
            }
            return rows;
        }

        /// <summary>
        /// 結果画面（S05）用：要望ごとに、この実験（plan）で機能が動いたかを入れる。
        /// 動かなかった「検証する」要望は、別の候補の実験名（PlanTitle）で確かめられる。
        /// </summary>
        public static List<FunctionRow> ForResult(InventionDna dna, IList<PlanCandidate> candidates, ExperimentPlan plan, out bool derived)
        {
            var rows = Build(dna, candidates, out derived);
            var here = PlanCandidates.BlocksExercised(plan, dna);
            var measured = plan == null ? new HashSet<string>() : new HashSet<string>(PlanEditor.ExpectedMetrics(plan, dna).Select(m => m.Key));
            foreach (var r in rows) r.CoveredHere = r.Verifiable && (r.Blocks.Any(here.Contains) || r.Metrics.Any(measured.Contains));
            return rows;
        }

        /// <summary>
        /// その計画で測れる計測項目（モードは問わない：部品代のように REALITY でだけ測るものも、REALITY に切り替えれば測れる）。
        /// </summary>
        static HashSet<string> Measurable(ExperimentPlan plan, InventionDna dna)
        {
            var set = new HashSet<string>();
            if (plan == null || PlanCandidates.IsLegacy(plan)) return set;
            var p = PlanEditor.Clone(plan);
            p.Mode = LabModes.Reality;
            foreach (var m in PlanEditor.ExpectedMetrics(p, dna)) set.Add(m.Key);
            return set;
        }

        /// <summary>見出しの件数（検証する・単純化・検証できない）。</summary>
        public static (int simulated, int simplified, int unverifiable) Count(IEnumerable<FunctionRow> rows)
        {
            var list = rows.ToList();
            return (list.Count(r => r.Status == "simulated"), list.Count(r => r.Status == "simplified"), list.Count(r => r.Status == "unverifiable"));
        }
    }
}
