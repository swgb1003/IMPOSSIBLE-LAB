using System.Collections.Generic;
using System.IO;
using System.Threading;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    public class CoreTests
    {
        static string SampleJson => Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text;
        static InventionDna Sample()
        {
            var r = DnaJson.Parse(SampleJson);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        // ---------- DNA / 検証（AT-02, AT-08） ----------
        [Test] public void Sample_IsValid_WithoutWarnings()
        {
            var r = DnaJson.Parse(SampleJson);
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual(0, r.Warnings.Count, string.Join("\n", r.Warnings));
            Assert.AreEqual(4, r.Dna.Parts.Count);
            Assert.AreEqual(0.6f, r.Dna.GetParameter("rain_shield", "radius", 0), 1e-5);
        }

        [Test] public void RoundTrip_PreservesDna()
        {
            var a = Sample();
            var b = DnaJson.Parse(DnaJson.ToJson(a)).Dna;
            Assert.AreEqual(DnaJson.ToJson(a), DnaJson.ToJson(b));
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("[1,2]")]
        [TestCase("{\"schemaVersion\":1} trailing")]
        public void BrokenJson_IsRejected(string json) => Assert.IsFalse(DnaJson.Parse(json).IsValid);

        [Test] public void UnknownField_IsRejected()
        {
            var r = DnaJson.Parse(SampleJson.Replace("\"revision\": 1,", "\"revision\": 1, \"script\": \"System.IO.File.Delete\","));
            Assert.IsFalse(r.IsValid);
            StringAssert.Contains("未知のフィールド", r.Summary);
        }

        [Test] public void UnknownPartType_And_Behavior_AreRejected()
        {
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"type\": \"sensor\"", "\"type\": \"laser\"")).IsValid);
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"type\": \"stabilize\"", "\"type\": \"teleport\"")).IsValid);
        }

        [Test] public void OutOfRange_IsClampedWithWarning()
        {
            var r = DnaJson.Parse(SampleJson.Replace("\"radius\": 0.6", "\"radius\": 99").Replace("[0, 0, 0]", "[50, 0, 0]"));
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual(1.5f, r.Dna.GetParameter("rain_shield", "radius", 0), 1e-5);
            Assert.AreEqual(LabCatalog.PositionLimitXZ, r.Dna.FindPart("shield").Position.x, 1e-5);
            Assert.GreaterOrEqual(r.Warnings.Count, 2);
        }

        [Test] public void NaN_Duplicate_MissingReference_AreRejected()
        {
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"radius\": 0.6", "\"radius\": \"big\"")).IsValid);
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"id\": \"eye\"", "\"id\": \"core\"")).IsValid);
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"targetPart\": \"shield\"", "\"targetPart\": \"ghost\"")).IsValid);
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"targetPart\": \"shield\"", "\"targetPart\": \"core\"")).IsValid, "rain_shield は canopy 専用");
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"id\": \"inv-handsfree-rain\"", "\"id\": \"../../etc\"")).IsValid);
        }

        [Test] public void TooManyParts_IsRejected()
        {
            var parts = new List<string>();
            for (int i = 0; i < 13; i++) parts.Add($"{{\"id\":\"p{i}\",\"type\":\"body\",\"position\":[0,0,0],\"scale\":[0.1,0.1,0.1]}}");
            string json = "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"x\",\"category\":\"protection\",\"purpose\":\"x\",\"parts\":[" + string.Join(",", parts) + "],\"behaviors\":[]}";
            Assert.IsFalse(DnaJson.Parse(json).IsValid);
        }

        [Test] public void WrongSchemaVersion_IsRejected() =>
            Assert.IsFalse(DnaJson.Parse(SampleJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")).IsValid);

        // ---------- 雨実験（AT-03） ----------
        static RainExperimentConfig Cfg(string mode = LabModes.Dream, float rain = 20, float wind = 3) =>
            new RainExperimentConfig { Mode = mode, RainIntensity = rain, WindSpeed = wind, WindDirectionDeg = 90, Seed = 42, DurationSec = 10 };

        [Test] public void Simulation_IsDeterministic()
        {
            var a = new RainSimulator(Sample(), Cfg()).RunToEnd();
            var b = new RainSimulator(Sample(), Cfg()).RunToEnd();
            Assert.AreEqual(a.DeviceHits, b.DeviceHits);
            Assert.AreEqual(a.BaselineHits, b.BaselineHits);
            Assert.Greater(a.BaselineHits, 100);
        }

        [Test] public void Simulation_StepwiseEqualsImmediate()
        {
            var a = new RainSimulator(Sample(), Cfg()).RunToEnd();
            var sim = new RainSimulator(Sample(), Cfg());
            while (!sim.Finished) sim.Step(37);
            Assert.AreEqual(a.DeviceHits, sim.Result.DeviceHits);
            Assert.AreEqual(a.WetnessScore, sim.Result.WetnessScore, 1e-6);
        }

        [Test] public void LargerRadius_ReducesWetness()
        {
            var small = Sample();
            var big = Sample();
            big.FindBehavior("rain_shield").Parameters["radius"] = 1.0f;
            var rs = new RainSimulator(small, Cfg()).RunToEnd();
            var rb = new RainSimulator(big, Cfg()).RunToEnd();
            Assert.Less(rb.WetnessScore, rs.WetnessScore);
            Assert.Greater(rb.CoverageRatio, rs.CoverageRatio);
        }

        [Test] public void RainAndWind_ChangeWetness()
        {
            var dna = Sample();
            var light = new RainSimulator(dna, Cfg(rain: 10)).RunToEnd();
            var heavy = new RainSimulator(dna, Cfg(rain: 60)).RunToEnd();
            Assert.Greater(heavy.WetnessScore, light.WetnessScore);
            var calm = new RainSimulator(dna, Cfg(wind: 0)).RunToEnd();
            var windy = new RainSimulator(dna, Cfg(wind: 10)).RunToEnd();
            Assert.Greater(windy.WetnessScore, calm.WetnessScore);
            Assert.Less(windy.CoverageRatio, calm.CoverageRatio);
        }

        [Test] public void Skirt_ReducesSideRain()
        {
            var dna = Sample();
            var skirt = Sample();
            skirt.FindBehavior("rain_shield").Parameters["skirtLength"] = 0.5f;
            var a = new RainSimulator(dna, Cfg(wind: 8)).RunToEnd();
            var b = new RainSimulator(skirt, Cfg(wind: 8)).RunToEnd();
            Assert.Less(b.DeviceHits, a.DeviceHits);
        }

        // ---------- DREAM / REALITY（AT-05） ----------
        [Test] public void Reality_AppliesLimitsAndEnergy()
        {
            var dna = Sample();
            dna.FindBehavior("rain_shield").Parameters["radius"] = 1.4f;
            var dream = new RainSimulator(dna, Cfg(LabModes.Dream, wind: 6)).RunToEnd();
            var real = new RainSimulator(dna, Cfg(LabModes.Reality, wind: 6)).RunToEnd();
            Assert.IsNull(dream.EnergyWh);
            Assert.IsNotNull(real.EnergyWh);
            Assert.Greater(real.EnergyWh.Value, 0f);
            Assert.IsNotEmpty(real.Limits);
            Assert.Greater(real.DriftM, 0f);
            Assert.Greater(real.WetnessScore, dream.WetnessScore);
        }

        // ---------- REALITY 物理モデル（docs/reality_model.md） ----------
        static RealityModel Model()
        {
            var m = RealityModel.Default;
            Assert.IsNotNull(m, RealityModel.DefaultError);
            return m;
        }

        [Test] public void RealityModel_LoadsWithSourcesAndStatus()
        {
            var m = Model();
            foreach (var id in RealityModel.RequiredIds) Assert.IsNotNull(m.Get(id), id);
            foreach (var c in m.Coefficients)
            {
                Assert.IsNotEmpty(c.Source, c.Id + " に出典（または要調査）の記載がない");
                Assert.IsTrue(m.StatusLegend.ContainsKey(c.Status), c.Id + " の status が凡例にない");
            }
        }

        [Test] public void RealityModel_HoverPower_MatchesMomentumTheory()
        {
            var m = Model();
            var e = m.Estimate(0f, 0f, 0f, 0f, RealityCase.Nominal);
            float mass = m.Get("baseMassKg").Value + m.Get("batteryMassKg").Value;
            float t = mass * RealityModel.Gravity;
            float ideal = Mathf.Pow(t, 1.5f) / Mathf.Sqrt(2f * m.Get("airDensity").Value * m.Get("rotorDiscArea").Value);
            float power = ideal / (m.Get("figureOfMerit").Value * m.Get("driveEfficiency").Value) + m.Get("avionicsPowerW").Value;
            Assert.AreEqual(mass, e.MassKg, 1e-4f);
            Assert.AreEqual(0f, e.WindForceN, 1e-6f);
            Assert.AreEqual(ideal, e.IdealPowerW, 1e-2f);
            Assert.AreEqual(power, e.PowerW, 1e-2f);
            float wh = m.Get("batteryMassKg").Value * m.Get("batteryEnergyDensity").Value * m.Get("usableFraction").Value;
            Assert.AreEqual(wh / power * 60f, e.RuntimeMinutes, 1e-3f);
        }

        [Test] public void RealityModel_RangeBracketsNominal()
        {
            var m = Model();
            var n = m.Estimate(0.6f, 0.2f, 0.3f, 5f, RealityCase.Nominal);
            var hi = m.Estimate(0.6f, 0.2f, 0.3f, 5f, RealityCase.Optimistic);
            var lo = m.Estimate(0.6f, 0.2f, 0.3f, 5f, RealityCase.Pessimistic);
            Assert.LessOrEqual(lo.RuntimeMinutes, n.RuntimeMinutes);
            Assert.LessOrEqual(n.RuntimeMinutes, hi.RuntimeMinutes);
            Assert.LessOrEqual(hi.PowerW, n.PowerW);
            Assert.LessOrEqual(n.PowerW, lo.PowerW);
        }

        [Test] public void RealityModel_WindSkirtAndStabilization()
        {
            var m = Model();
            var calm = m.Estimate(0.6f, 0f, 0.3f, 0f, RealityCase.Nominal);
            var windy = m.Estimate(0.6f, 0f, 0.3f, 6f, RealityCase.Nominal);
            var skirt = m.Estimate(0.6f, 0.3f, 0.3f, 6f, RealityCase.Nominal);
            var stable = m.Estimate(0.6f, 0f, 1f, 6f, RealityCase.Nominal);
            Assert.AreEqual(0f, calm.DriftM, 1e-6f);
            Assert.Greater(windy.WindForceN, 0f);
            Assert.Greater(windy.PowerW, calm.PowerW);
            Assert.Greater(skirt.WindForceN, windy.WindForceN);
            Assert.Greater(skirt.MassKg, windy.MassKg);
            Assert.Less(stable.DriftM, windy.DriftM);
            // 風荷重は風速の2乗に比例
            var w3 = m.Estimate(0.6f, 0f, 0.3f, 3f, RealityCase.Nominal);
            Assert.AreEqual(4f, windy.WindForceN / w3.WindForceN, 1e-3f);
        }

        [Test] public void Reality_StrongWindWithLargeSkirt_CannotHoldPosition()
        {
            var dna = Sample();
            dna.FindBehavior("rain_shield").Parameters["radius"] = 0.9f;
            dna.FindBehavior("rain_shield").Parameters["skirtLength"] = 0.6f;
            var r = new RainSimulator(dna, Cfg(LabModes.Reality, wind: 15)).RunToEnd();
            Assert.IsFalse(r.CanHoldPosition);
            Assert.IsTrue(r.Limits.Exists(x => x.Contains("位置を保てない")), string.Join("\n", r.Limits));
            Assert.AreEqual(Model().Get("maxDriftM").Value, r.DriftM, 1e-4f);
        }

        [Test] public void RealityModel_Parse_RejectsInvalidData()
        {
            string json = Resources.Load<TextAsset>(RealityModel.ResourcePath).text;
            Assert.IsNotNull(RealityModel.Parse(json, out var ok), ok);
            Assert.IsNull(RealityModel.Parse(json.Replace("\"id\": \"airDensity\"", "\"id\": \"airDensityX\""), out var e1));
            StringAssert.Contains("airDensity", e1);
            Assert.IsNull(RealityModel.Parse(json.Replace("\"value\": 0.8, \"min\": 0.7, \"max\": 0.85", "\"value\": 0.8, \"min\": 0.7, \"max\": 1.5"), out var e2));
            Assert.IsNotNull(e2);
            Assert.IsNull(RealityModel.Parse(json.Replace("\"value\": 1.225, \"min\": 1.16", "\"value\": 1.0, \"min\": 1.16"), out var e3));
            Assert.IsNotNull(e3);
            Assert.IsNull(RealityModel.Parse("{ not json", out var e4));
            Assert.IsNotNull(e4);
        }

        // ---------- AI設計プロキシ（AT-07 / AT-08） ----------
        [Test] public void Catalog_ProxyJson_ListsCatalog()
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(LabCatalog.ToProxyJson());
            Assert.AreEqual(LabCatalog.PartTypes.Length, ((Newtonsoft.Json.Linq.JArray)o["partTypes"]).Count);
            Assert.AreEqual(LabCatalog.Behaviors.Length, ((Newtonsoft.Json.Linq.JArray)o["behaviors"]).Count);
            Assert.AreEqual(RequestRules.MaxRequestLength, (int)o["limits"]["maxRequestLength"]);
        }

        static string OkDesignBody(string dnaJson) => "{\"status\":\"ok\",\"dna\":" + dnaJson + ",\"designPoints\":[\"半径0.6mで肩まで覆う\"],\"message\":\"ok\"}";

        [Test] public void Proxy_ParseDesign_ValidatesDna()
        {
            var ok = ProxyProtocol.ParseDesign(200, OkDesignBody(SampleJson), null);
            Assert.AreEqual(DesignStatus.Success, ok.Status, ok.Message);
            Assert.AreEqual("ai", ok.Source);
            Assert.AreEqual(1, ok.DesignPoints.Count);
            var bad = ProxyProtocol.ParseDesign(200, OkDesignBody(SampleJson.Replace("\"sensor\"", "\"laser_cannon\"")), null);
            Assert.AreEqual(DesignStatus.Failed, bad.Status);
            Assert.IsNotNull(bad.Report);
        }

        [Test] public void Proxy_ParseDesign_ClassifiesErrors()
        {
            Assert.AreEqual(DesignStatus.Offline, ProxyProtocol.ParseDesign(0, null, "接続できません").Status);
            Assert.AreEqual(DesignStatus.Offline, ProxyProtocol.ParseDesign(504, "{\"status\":\"error\",\"code\":\"upstream_timeout\",\"message\":\"t\"}", null).Status);
            Assert.AreEqual(DesignStatus.Offline, ProxyProtocol.ParseDesign(503, "{\"status\":\"error\",\"code\":\"no_api_key\"}", null).Status);
            Assert.AreEqual(DesignStatus.Failed, ProxyProtocol.ParseDesign(422, "{\"status\":\"error\",\"code\":\"refused\",\"message\":\"r\"}", null).Status);
            Assert.AreEqual(DesignStatus.Failed, ProxyProtocol.ParseDesign(502, "{\"status\":\"error\",\"code\":\"unparsable\"}", null).Status);
            Assert.AreEqual(DesignStatus.Failed, ProxyProtocol.ParseDesign(200, "not json", null).Status);
            var un = ProxyProtocol.ParseDesign(200, "{\"status\":\"unsupported\",\"message\":\"未対応\",\"suggestions\":[\"a\",\"b\",\"c\",\"d\"]}", null);
            Assert.AreEqual(DesignStatus.Unsupported, un.Status);
            Assert.AreEqual(3, un.Suggestions.Count);
        }

        [Test] public void Proxy_ParseRevision_ValidatesProposal()
        {
            string body = "{\"status\":\"ok\",\"proposal\":{\"summary\":\"側面カバーを追加\",\"notes\":[],\"operations\":[{\"op\":\"update_parameter\",\"behavior\":\"rain_shield\",\"parameter\":\"skirtLength\",\"value\":0.25}]}}";
            var r = ProxyProtocol.ParseRevision(200, body, null);
            Assert.AreEqual(DesignStatus.Success, r.Status, r.Message);
            Assert.AreEqual("ai", r.Proposal.Source);
            var applied = RevisionEngine.Apply(Sample(), r.Proposal);
            Assert.IsTrue(applied.Success);
            Assert.AreEqual(0.25f, applied.NewDna.GetParameter("rain_shield", "skirtLength", 0), 1e-5f);
            var bad = ProxyProtocol.ParseRevision(200, body.Replace("update_parameter", "run_code"), null);
            Assert.AreEqual(DesignStatus.Failed, bad.Status);
        }

        [Test] public void Proxy_ReviseRequest_CarriesDnaAndMetrics()
        {
            var dna = Sample();
            var exp = new RainSimulator(dna, Cfg()).RunToEnd();
            var o = Newtonsoft.Json.Linq.JObject.Parse(ProxyProtocol.ReviseRequestJson(dna, exp, " 横からの雨も防いで "));
            Assert.AreEqual("横からの雨も防いで", (string)o["instruction"]);
            Assert.AreEqual(dna.Id, (string)o["dna"]["id"]);
            Assert.AreEqual(exp.CoverageRatio, (float)o["lastResult"]["coverageRatio"], 1e-5f);
        }

        sealed class FakeService : IInventionDesignService
        {
            public DesignStatus Status;
            public int Calls;
            public string DisplayName => "fake";
            public System.Threading.Tasks.Task<DesignOutcome> GenerateAsync(string t, System.IProgress<DesignStage> p, CancellationToken ct)
            { Calls++; return System.Threading.Tasks.Task.FromResult(new DesignOutcome { Status = Status, Message = "m", Source = "ai" }); }
            public System.Threading.Tasks.Task<RevisionOutcome> ReviseAsync(InventionDna c, RainExperimentResult l, string t, CancellationToken ct)
            { Calls++; return System.Threading.Tasks.Task.FromResult(new RevisionOutcome { Status = Status, Message = "m" }); }
        }

        [Test] public void Fallback_SwitchesToOfflineOnlyOnConnectivityProblems()
        {
            var offline = new OfflineDesignService(new Dictionary<string, string> { ["protection"] = SampleJson });
            var down = new FallbackDesignService(new FakeService { Status = DesignStatus.Offline }, offline);
            var r = down.GenerateAsync(LabAppTextRain, null, CancellationToken.None).Result;
            Assert.AreEqual(DesignStatus.Success, r.Status);
            Assert.AreEqual("offline", r.Source);
            Assert.IsTrue(down.LastUsedFallback);
            StringAssert.Contains("AIに接続できなかった", r.Message);

            var badAi = new FakeService { Status = DesignStatus.Failed };
            var f = new FallbackDesignService(badAi, offline);
            var r2 = f.GenerateAsync(LabAppTextRain, null, CancellationToken.None).Result;
            Assert.AreEqual(DesignStatus.Failed, r2.Status, "AIの出力が不正なときは勝手にサンプルへ差し替えない");
            Assert.IsFalse(f.LastUsedFallback);
        }

        const string LabAppTextRain = "手を使わず雨を防ぐ装置を作って";

        static string Fixture(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "_ImpossibleLab/Tests/EditMode/Fixtures", name));

        /// <summary>実際に Claude（claude-opus-5-5）が返した応答（2026-11-04 記録）がアプリの検証を通り、実験・改良まで進めること。</summary>
        [Test] public void Proxy_RecordedClaudeResponses_WorkEndToEnd()
        {
            var d = ProxyProtocol.ParseDesign(200, Fixture("ai_design_response.json"), null);
            Assert.AreEqual(DesignStatus.Success, d.Status, d.Message + " / " + d.Report?.Summary);
            Assert.AreEqual(0, d.Report.Errors.Count);
            Assert.IsNotNull(d.Dna.FindBehavior("rain_shield"));
            var exp = new RainSimulator(d.Dna, Cfg(wind: 4)).RunToEnd();
            Assert.Greater(exp.BaselineHits, 0);
            var r = ProxyProtocol.ParseRevision(200, Fixture("ai_revise_response.json"), null);
            Assert.AreEqual(DesignStatus.Success, r.Status, r.Message);
            var applied = RevisionEngine.Apply(d.Dna, r.Proposal);
            Assert.IsTrue(applied.Success, string.Join(" / ", applied.Errors));
        }

        // ---------- 改良（AT-04） ----------
        [Test] public void OfflineRevision_WidenRange_ChangesOnlyRadius()
        {
            var dna = Sample();
            var proposal = OfflineRevisionRules.Propose(dna, "防ぐ範囲を広げて");
            Assert.AreEqual(1, proposal.Operations.Count);
            var res = RevisionEngine.Apply(dna, proposal);
            Assert.IsTrue(res.Success, string.Join(",", res.Errors));
            Assert.AreEqual(0.8f, res.NewDna.GetParameter("rain_shield", "radius", 0), 1e-5);
            Assert.AreEqual(0.6f, dna.GetParameter("rain_shield", "radius", 0), 1e-5, "元のDNAは変更しない");
            Assert.AreEqual(2, res.NewDna.Revision);
            Assert.AreEqual(1, res.NewDna.ParentRevision);
            Assert.AreEqual(1, res.Changes.Count);
            var before = new RainSimulator(dna, Cfg()).RunToEnd();
            var after = new RainSimulator(res.NewDna, Cfg()).RunToEnd();
            Assert.Less(after.WetnessScore, before.WetnessScore);
        }

        [Test] public void OfflineRevision_SideRain_UsesSkirt()
        {
            var p = OfflineRevisionRules.Propose(Sample(), "横からの雨も防いで");
            Assert.AreEqual("skirtLength", p.Operations[0].Parameter);
        }

        [Test] public void OfflineRevision_Unknown_ReturnsAlternatives()
        {
            var p = OfflineRevisionRules.Propose(Sample(), "ビームを出して");
            Assert.AreEqual(0, p.Operations.Count);
            Assert.IsNotEmpty(p.Notes);
        }

        [Test] public void Revision_RejectsInvalidOperations()
        {
            var dna = Sample();
            var bad = new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = "rain_shield", Parameter = "laser", Value = 1 } } };
            Assert.IsFalse(RevisionEngine.Apply(dna, bad).Success);
            var used = new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.RemovePart, PartId = "shield" } } };
            Assert.IsFalse(RevisionEngine.Apply(dna, used).Success, "使用中の部品は削除不可");
            var huge = new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = "rain_shield", Parameter = "radius", Value = 1000 } } };
            var res = RevisionEngine.Apply(dna, huge);
            Assert.IsTrue(res.Success);
            Assert.AreEqual(1.5f, res.NewDna.GetParameter("rain_shield", "radius", 0), 1e-5);
        }

        [Test] public void ProposalJson_IsValidated()
        {
            Assert.IsTrue(RevisionEngine.TryParseProposal("{\"summary\":\"広げる\",\"operations\":[{\"op\":\"update_parameter\",\"behavior\":\"rain_shield\",\"parameter\":\"radius\",\"value\":0.9}]}", out var p, out _));
            Assert.AreEqual(0.9f, p.Operations[0].Value, 1e-5);
            Assert.IsFalse(RevisionEngine.TryParseProposal("{\"operations\":[{\"op\":\"exec\",\"code\":\"rm\"}]}", out _, out _));
            Assert.IsFalse(RevisionEngine.TryParseProposal("not json", out _, out _));
        }

        // ---------- オフライン設計（AT-02, AT-07） ----------
        [Test] public void OfflineDesign_RainRequest_ReturnsValidDna()
        {
            var svc = new OfflineDesignService(new Dictionary<string, string> { ["protection"] = SampleJson });
            var stages = new List<DesignStage>();
            var outcome = svc.GenerateAsync("手を使わず雨を防ぐ装置を作って", new SyncProgress<DesignStage>(stages.Add), CancellationToken.None).Result;
            Assert.AreEqual(DesignStatus.Success, outcome.Status, outcome.Message);
            Assert.AreEqual("protection", outcome.Dna.Category);
            CollectionAssert.AreEqual(new[] { DesignStage.IntentAnalysis, DesignStage.FeatureSelection, DesignStage.PartDesign }, stages);
        }

        [Test] public void OfflineDesign_UnsupportedAndEmpty()
        {
            var svc = new OfflineDesignService(new Dictionary<string, string> { ["protection"] = SampleJson });
            Assert.AreEqual(DesignStatus.InvalidInput, svc.GenerateAsync("   ", null, CancellationToken.None).Result.Status);
            Assert.AreEqual(DesignStatus.InvalidInput, svc.GenerateAsync(new string('あ', 201), null, CancellationToken.None).Result.Status);
            var shoes = svc.GenerateAsync("空を飛べる靴を作って", null, CancellationToken.None).Result;
            Assert.AreEqual(DesignStatus.Unsupported, shoes.Status);
            Assert.IsNotEmpty(shoes.Suggestions);
        }

        // ---------- 保存（AT-06） ----------
        [Test] public void Repository_SaveLoadListDelete()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new InventionRepository(dir);
                var dna = Sample();
                var exp = new RainSimulator(dna, Cfg(LabModes.Reality)).RunToEnd();
                var inv = new SavedInvention { Current = dna, History = { dna.Clone() }, Experiments = { exp } };
                repo.Save(inv);
                // 再起動相当：新しいリポジトリインスタンスで読む
                var repo2 = new InventionRepository(dir);
                var loaded = repo2.Load(dna.Id, out var err);
                Assert.IsNull(err);
                Assert.AreEqual(DnaJson.ToJson(dna), DnaJson.ToJson(loaded.Current));
                Assert.AreEqual(1, loaded.Experiments.Count);
                Assert.AreEqual(exp.DeviceHits, loaded.Experiments[0].DeviceHits);
                Assert.AreEqual(LabModes.Reality, loaded.Experiments[0].Config.Mode);
                Assert.IsNotNull(loaded.Experiments[0].RuntimeMinutesMin);
                Assert.AreEqual(exp.RuntimeMinutesMax.Value, loaded.Experiments[0].RuntimeMinutesMax.Value, 0.01f);
                Assert.AreEqual(exp.CanHoldPosition, loaded.Experiments[0].CanHoldPosition);
                var list = repo2.List();
                Assert.AreEqual(1, list.Count);
                Assert.IsTrue(list[0].Tested);
                Assert.IsNull(repo2.Load("../evil", out var e2));
                Assert.IsNotNull(e2);
                Assert.IsTrue(repo2.Delete(dna.Id));
                Assert.AreEqual(0, repo2.List().Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void Repository_CorruptFile_IsSkipped()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new InventionRepository(dir);
                File.WriteAllText(Path.Combine(repo.DirectoryPath, "broken.json"), "{ not json");
                Assert.AreEqual(0, repo.List().Count);
                Assert.IsNull(repo.Load("broken", out var err));
                Assert.IsNotNull(err);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ---------- 状態遷移 ----------
        [Test] public void StateMachine_BlocksInvalidTransitions()
        {
            var sm = new AppStateMachine();
            Assert.IsFalse(sm.Transition(LabState.RunningTest));
            Assert.IsTrue(sm.Transition(LabState.Drafting));
            Assert.IsTrue(sm.Transition(LabState.GeneratingDesign));
            Assert.IsTrue(sm.Busy);
            Assert.IsFalse(sm.Transition(LabState.GeneratingDesign), "二重送信防止");
            Assert.IsTrue(sm.Transition(LabState.Error));
            Assert.IsTrue(sm.Transition(LabState.Drafting));
        }

        sealed class SyncProgress<T> : System.IProgress<T>
        {
            readonly System.Action<T> _a;
            public SyncProgress(System.Action<T> a) { _a = a; }
            public void Report(T value) => _a(value);
        }
    }
}
