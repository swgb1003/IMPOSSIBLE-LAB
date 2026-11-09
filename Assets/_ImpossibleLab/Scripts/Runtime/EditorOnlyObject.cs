using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>XR Device Simulator など、Editor での開発確認用オブジェクトを実機ビルドから除外する。</summary>
    public sealed class EditorOnlyObject : MonoBehaviour
    {
        void Awake()
        {
#if !UNITY_EDITOR
            Destroy(gameObject);
#else
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ilabVerify") >= 0) Destroy(gameObject);
#endif
        }
    }
}
