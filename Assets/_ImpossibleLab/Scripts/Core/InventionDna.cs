using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>発明DNA（仕様5章）。AI出力・保存データはすべて DnaValidator を通してから本型になる。</summary>
    public sealed class InventionDna
    {
        /// <summary>2 = タグ・取り付け位置・部品の形と素材・機能対応表（docs/GENERAL_INVENTION_DESIGN.md 2章）。1 は読み込み時に変換する。</summary>
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;
        public string Id = "";
        public string Name = "";
        /// <summary>分類タグ（1〜3個、LabCatalog.Tags）。</summary>
        public List<string> Tags = new List<string>();
        /// <summary>取り付け位置（LabCatalog.Mounts）。</summary>
        public string Mount = "fixed";
        /// <summary>要望ごとに、どの機能ブロックで実現・検証するか、検証できるか。</summary>
        public List<FunctionNeed> FunctionMap = new List<FunctionNeed>();

        /// <summary>
        /// 実験場の選択に使う旧カテゴリ（protection / carrier / mobility）。タグのうち最初に見つかったもの。
        /// 組み立て式の実験（週4〜）に移るまでの互換用。設定するとタグの先頭に入る。
        /// </summary>
        public string Category
        {
            get => Tags.FirstOrDefault(t => t == "protection" || t == "carrier" || t == "mobility") ?? "";
            set
            {
                Tags.RemoveAll(t => t == "protection" || t == "carrier" || t == "mobility");
                if (!string.IsNullOrEmpty(value)) Tags.Insert(0, value);
            }
        }
        public string Purpose = "";
        public string Mode = LabModes.Dream;
        /// <summary>外観プリセットID（LabCatalog.Appearances）。性能には影響しない。</summary>
        public string Appearance = LabCatalog.DefaultAppearance;
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
            c.Tags = new List<string>(Tags);
            c.FunctionMap = FunctionMap.ConvertAll(f => f.Clone());
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
        /// <summary>形（LabCatalog.Shapes）。空なら部品タイプの既定の形。</summary>
        public string Shape = "";
        /// <summary>素材（LabCatalog.Materials）。空なら部品タイプの既定の素材。</summary>
        public string Material = "";

        public PartSpec Clone() => (PartSpec)MemberwiseClone();
        public string ShapeOrDefault => !string.IsNullOrEmpty(Shape) ? Shape : LabCatalog.Part(Type)?.DefaultShape ?? "cylinder";
        public string MaterialOrDefault => !string.IsNullOrEmpty(Material) ? Material : LabCatalog.Part(Type)?.DefaultMaterial ?? "plastic";
    }

    /// <summary>機能対応表の1行。</summary>
    public sealed class FunctionNeed
    {
        public string Need = "";
        public List<string> Blocks = new List<string>();
        /// <summary>simulated / simplified / unverifiable</summary>
        public string Status = "simulated";
        public string Note = "";
        public FunctionNeed Clone() => new FunctionNeed { Need = Need, Blocks = new List<string>(Blocks), Status = Status, Note = Note };
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
        public const int HardMaxParts = 24;
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
