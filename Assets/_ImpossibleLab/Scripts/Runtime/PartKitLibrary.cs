using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 部品キット（Blender 制作の FBX、DECISIONS D-26・D-27）から部品の見た目を引く。
    /// 名前は KIT_[部品タイプ]_[形]（無ければ KIT_any_[形]）。KIT_…__ACC は機能の色で塗る飾り、それ以外の __X は FBX のマテリアルのまま。
    /// キットに無い組み合わせは null（従来の基本形状で表示）。
    /// </summary>
    public static class PartKitLibrary
    {
        public sealed class Extra { public string Name; public Mesh Mesh; public Material Material; public bool IsAccent; }

        public sealed class Entry
        {
            public Mesh Mesh;
            public readonly List<Extra> Extras = new List<Extra>();
            public bool HasAccent => Extras.Exists(x => x.IsAccent);
        }

        static LabAssets _for;
        static Dictionary<string, Entry> _entries;

        public static Entry Find(LabAssets assets, string type, string shape)
        {
            var all = Load(assets);
            if (all == null) return null;
            return all.TryGetValue($"KIT_{type}_{shape}", out var e) ? e : all.TryGetValue($"KIT_any_{shape}", out e) ? e : null;
        }

        static Dictionary<string, Entry> Load(LabAssets assets)
        {
            if (assets == null || assets.PartKit == null) return null;
            if (_for == assets && _entries != null) return _entries;
            _for = assets;
            _entries = new Dictionary<string, Entry>();
            var extras = new List<(string key, Extra x)>();
            foreach (var mf in assets.PartKit.GetComponentsInChildren<MeshFilter>(true))
            {
                string n = mf.gameObject.name;
                if (!n.StartsWith("KIT_") || mf.sharedMesh == null) continue;
                if (Quaternion.Angle(mf.transform.localRotation, Quaternion.identity) > 0.1f || (mf.transform.localScale - Vector3.one).sqrMagnitude > 1e-4f)
                    Debug.LogWarning($"[ILAB] PartKit {n}: 回転・拡大が残っています（{mf.transform.localEulerAngles} / {mf.transform.localScale}）。取り込み設定を確認してください");
                int sep = n.IndexOf("__", System.StringComparison.Ordinal);
                if (sep < 0) { Get(n).Mesh = mf.sharedMesh; continue; }
                var r = mf.GetComponent<MeshRenderer>();
                extras.Add((n.Substring(0, sep), new Extra { Name = n.Substring(sep + 2), Mesh = mf.sharedMesh, Material = r != null ? r.sharedMaterial : null, IsAccent = n.EndsWith("__ACC") }));
            }
            foreach (var (key, x) in extras) Get(key).Extras.Add(x);
            // 本体のメッシュが無いものは使わない
            var drop = new List<string>();
            foreach (var kv in _entries) if (kv.Value.Mesh == null) drop.Add(kv.Key);
            foreach (var k in drop) _entries.Remove(k);
            return _entries;
        }

        static Entry Get(string key)
        {
            if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();
            return e;
        }
    }
}
