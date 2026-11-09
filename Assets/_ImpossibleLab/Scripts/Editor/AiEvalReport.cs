using System;
using System.IO;
using System.Linq;
using System.Text;
using ImpossibleLab.Core;
using UnityEditor;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// AI 評価セットの集計（proxy/eval/results の最新の JSONL → docs/eval/ に .md と人の評価用 .csv）。API は呼ばない。
    /// Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod ImpossibleLab.EditorTools.AiEvalReport.Run [-ilabEvalFile path]
    /// </summary>
    public static class AiEvalReport
    {
        [MenuItem("IMPOSSIBLE LAB/5. AI評価セットを集計")]
        public static void Run()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string file = ArgValue("-ilabEvalFile");
            if (string.IsNullOrEmpty(file))
            {
                var dir = Path.Combine(root, "proxy", "eval", "results");
                file = Directory.Exists(dir) ? Directory.GetFiles(dir, "design-*.jsonl").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault() : null;
            }
            if (file == null || !File.Exists(file)) { Debug.LogError("[ILAB] eval: 結果ファイルがありません（proxy で npm run eval を実行してください）"); return; }
            var records = File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Trim().Length > 0).Select(AiEvalRecord.FromJsonLine).ToList();
            var rows = AiEvalAnalyzer.Analyze(records);
            string model = "";
            foreach (var r in records)
                try { model = Newtonsoft.Json.Linq.JObject.Parse(r.Body).Value<string>("model") ?? ""; if (model.Length > 0) break; }
                catch (Newtonsoft.Json.JsonException) { }
            string outDir = Path.Combine(root, "docs", "eval");
            Directory.CreateDirectory(outDir);
            string name = Path.GetFileNameWithoutExtension(file);
            File.WriteAllText(Path.Combine(outDir, name + ".md"), AiEvalAnalyzer.Markdown(rows, "proxy/eval/results/" + Path.GetFileName(file), model), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, name + "-rating.csv"), AiEvalAnalyzer.RatingCsv(rows), new UTF8Encoding(false));
            int ok = rows.Count(r => r.Status == "設計OK");
            Debug.Log($"[ILAB] eval: {rows.Count} records, design ok {ok}, plans valid {rows.Sum(r => r.PlansValid)}/{rows.Sum(r => r.PlansProposed)} -> docs/eval/{name}.md");
        }

        static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
