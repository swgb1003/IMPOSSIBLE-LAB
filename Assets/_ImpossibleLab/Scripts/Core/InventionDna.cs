using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>発明DNA（仕様5章）。AI出力・保存データはすべて DnaValidator を通してから本型になる。</summary>
    public sealed class InventionDna
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string Id = "";
        public string Name = "";
        public string Category = "";
        public string Purpose = "";
        public string Mode = LabModes.Dream;
        public List<PartSpec> Parts = new List<PartSpec>();
        public List<BehaviorSpec> Behaviors = new List<BehaviorSpec>();
        public InventionConstraints Constraints = new InventionConstraints();
        public int Revision = 1;
        public int? ParentRevision;

        public PartSpec FindPart(string id) => Parts.Find(p => p.Id == id);
        public BehaviorSpec FindBehavior(string type) => Behaviors.Find(b => b.Type == type);

        public float GetParameter(string behaviorType, string parameter, float fallback)
        {
            var b = FindBehavior(behaviorType);
            return b != null && b.Parameters.TryGetValue(parameter, out var v) ? v : fallback;
        }

        public InventionDna Clone()
        {
            var c = (InventionDna)MemberwiseClone();
            c.Parts = Parts.ConvertAll(p => p.Clone());
            c.Behaviors = Behaviors.ConvertAll(b => b.Clone());
            c.Constraints = new InventionConstraints { MaxParts = Constraints.MaxParts, MaxSpeed = Constraints.MaxSpeed };
            return c;
        }
    }

    public sealed class PartSpec
    {
        public string Id = "";
        public string Type = "";
        public Vector3 Position;
        public Vector3 Scale = Vector3.one;

        public PartSpec Clone() => (PartSpec)MemberwiseClone();
    }

    public sealed class BehaviorSpec
    {
        public string Type = "";
        public string TargetPart;
        public Dictionary<string, float> Parameters = new Dictionary<string, float>();

        public BehaviorSpec Clone()
        {
            var c = (BehaviorSpec)MemberwiseClone();
            c.Parameters = new Dictionary<string, float>(Parameters);
            return c;
        }
    }

    public sealed class InventionConstraints
    {
        public const int HardMaxParts = 12;
        public int MaxParts = HardMaxParts;
        public float MaxSpeed = 3f;
    }

    public static class LabModes
    {
        public const string Dream = "dream";
        public const string Reality = "reality";
        public static bool IsValid(string m) => m == Dream || m == Reality;
        public static string Label(string m) => m == Reality ? "REALITY MODE" : "DREAM MODE";
        public static string Caption(string m) => m == Reality ? "簡易モデルによる参考値" : "空想シミュレーション";
    }
}
