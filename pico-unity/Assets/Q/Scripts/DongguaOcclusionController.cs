// ============================================================
// DongguaOcclusionController.cs
// 屏蔽听众时：在人脸位置放置冬瓜遮挡
// 资源：StreamingAssets/species/deer/（冬瓜 Benincasa hispida）
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Q.Pico
{
    public class DongguaOcclusionController : MonoBehaviour
    {
        [Header("遮挡")]
        public bool occlusionEnabled = false;
        [Tooltip("冬瓜模型缩放（匹配人脸大小）。典型人脸宽约 0.15-0.18m。")]
        public float faceScale = 0.16f;
        public float followLerp = 12f;
        public float heightOffset = 0.02f;
        public int maxFaces = 3;
        [Tooltip("无 SecureMR 检测器时生成模拟位置（仅调试用）")]
        public bool mockFacesWhenNoDetector = false;
        public float mockDistance = 1.75f;
        public float mockSpread = 0.5f;

        [Header("模型")]
        public GameObject dongguaPrefab;
        public Material fallbackMaterial;

        readonly List<GameObject> masks = new List<GameObject>();
        readonly List<Vector3> targetPos = new List<Vector3>();
        FaceOcclusionManager faceMgr;
        Transform head;
        bool warnedNoModel;

        void Start()
        {
            faceMgr = FindObjectOfType<FaceOcclusionManager>();
            EnsureMaskPool();
            SetOcclusionEnabled(occlusionEnabled);
        }

        void Update()
        {
            if (!occlusionEnabled) return;
            EnsureHead();
            UpdateTargets();
            FollowTargets();
        }

        public void SetOcclusionEnabled(bool enabled)
        {
            occlusionEnabled = enabled;
            if (!enabled)
            {
                foreach (var m in masks)
                    if (m != null) m.SetActive(false);
            }
            else
            {
                EnsureMaskPool();
                Debug.Log("[Donggua] 听众遮挡已开启（冬瓜）");
            }
        }

        void EnsureHead()
        {
            if (head != null) return;
            var cam = Camera.main;
            if (cam != null) head = cam.transform;
        }

        void EnsureMaskPool()
        {
            while (masks.Count < maxFaces)
            {
                var go = CreateDongguaInstance(masks.Count);
                go.SetActive(false);
                masks.Add(go);
            }
        }

        GameObject CreateDongguaInstance(int index)
        {
            GameObject go;
            if (dongguaPrefab != null)
            {
                go = Instantiate(dongguaPrefab, transform);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"DongguaMask_{index}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.95f, 1.2f, 0.95f) * faceScale;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    var sh = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
                    var mat = fallbackMaterial != null ? new Material(fallbackMaterial) : new Material(sh);
                    TryApplyDongguaTexture(mat);
                    if (mat.HasProperty("_Color"))
                        mat.color = new Color(0.42f, 0.72f, 0.38f, 1f);
                    r.material = mat;
                }

                if (!warnedNoModel)
                {
                    warnedNoModel = true;
                    Debug.Log("[Donggua] 使用程序化冬瓜遮挡体 + 冬瓜贴图");
                }
            }
            go.name = $"DongguaMask_{index}";
            return go;
        }

        void TryApplyDongguaTexture(Material mat)
        {
            try
            {
                string texPath = Path.Combine(Application.streamingAssetsPath,
                    "species/deer/textures/donggua-1_baseColor.jpeg");
                if (!File.Exists(texPath)) return;
                byte[] bytes = File.ReadAllBytes(texPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = TextureWrapMode.Repeat;
                    mat.mainTexture = tex;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Donggua] 贴图加载失败: {e.Message}");
            }
        }

        void UpdateTargets()
        {
            targetPos.Clear();

            // SecureMR 自己渲染 glTF 时，Unity 占位隐藏
            if (faceMgr != null && faceMgr.IsRunning && faceMgr.OccludeAudienceFaces)
            {
                for (int i = 0; i < masks.Count; i++)
                    if (masks[i] != null) masks[i].SetActive(false);
                return;
            }

            if (mockFacesWhenNoDetector && head != null)
            {
                int n = Mathf.Min(2, maxFaces);
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) * 0.5f) * mockSpread;
                    Vector3 pos = head.position
                        + head.forward * mockDistance
                        + head.right * x
                        + Vector3.up * (heightOffset + 0.03f * Mathf.Sin(Time.time * 1.2f + i));
                    targetPos.Add(pos);
                }
            }
        }

        void FollowTargets()
        {
            for (int i = 0; i < masks.Count; i++)
            {
                var m = masks[i];
                if (m == null) continue;
                if (i >= targetPos.Count)
                {
                    m.SetActive(false);
                    continue;
                }
                m.SetActive(true);
                Vector3 t = targetPos[i];
                float k = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
                m.transform.position = Vector3.Lerp(m.transform.position, t, k);
                if (head != null)
                {
                    Vector3 look = head.position - m.transform.position;
                    if (look.sqrMagnitude > 1e-4f)
                        m.transform.rotation = Quaternion.Slerp(
                            m.transform.rotation,
                            Quaternion.LookRotation(-look.normalized, Vector3.up), k);
                }
                m.transform.localScale = Vector3.one * faceScale;
            }
        }
    }
}
