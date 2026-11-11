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
            var exercised = (candidates ?? new List<PlanCandidate>()).Select(c => (c.Title, Blocks: PlanCandidates.BlocksExercised(c.Plan, dna))).ToList();
            string PlanFor(IEnumerable<string> blocks) => exercised.FirstOrDefault(e => blocks.Any(b => e.Blocks.Contains(b))).Title;

            if (dna.FunctionMap.Count > 0)
            {
                foreach (var n in dna.FunctionMap)
                    rows.Add(new FunctionRow
                    {
                        Need = n.Need, Status = n.Status, Note = n.Note ?? "",
                        BlocksJa = string.Join("・", n.Blocks.Select(b => LabCatalog.Behavior(b)?.LabelJa ?? b)),
                        PlanTitle = n.Status == "unverifiable" ? null : PlanFor(n.Blocks),
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
                    Status = plan != null ? "simulated" : "unverifiable",
                    Note = plan != null ? "" : "この版の実験では動かない（試用で動きを見るだけ）",
                    PlanTitle = plan,
                });
            }
            return rows;
        }

        /// <summary>見出しの件数（検証する・単純化・検証できない）。</summary>
        public static (int simulated, int simplified, int unverifiable) Count(IEnumerable<FunctionRow> rows)
        {
            var list = rows.ToList();
            return (list.Count(r => r.Status == "simulated"), list.Count(r => r.Status == "simplified"), list.Count(r => r.Status == "unverifiable"));
        }
    }
}
