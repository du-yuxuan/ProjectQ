// ============================================================
// SpeciesAvatarController.cs
// Q (Cue) — 单个物种化身控制器
//
// 管理单个说话人的 3D 化身：
//   - SpeakerId 标识所属说话人
//   - 物种类型 → 对应 Prefab 实例化
//   - 说话时嘴巴骨骼缩放动画
//   - 情绪→物种切换（带冷却时间，防止频繁闪烁）
//
// 由 FaceOcclusionManager 查找使用。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Q.Pico
{
    /// <summary>
    /// 单个物种化身控制器 — 管理一个说话人的 3D 化身。
    /// 多说话人场景下，每个说话人一个 SpeciesAvatarController 实例。
    /// </summary>
    public class SpeciesAvatarController : MonoBehaviour
    {
        [Header("说话人")]
        [Tooltip("说话人 ID（与后端 speaker 字段对应）")]
        [SerializeField] private int speakerId = 0;

        [Header("物种 Prefab")]
        [Tooltip("老虎 Prefab")]
        public GameObject tigerPrefab;
        [Tooltip("兔子 Prefab")]
        public GameObject rabbitPrefab;
        [Tooltip("猫头鹰 Prefab")]
        public GameObject owlPrefab;
        [Tooltip("狐狸 Prefab")]
        public GameObject foxPrefab;
        [Tooltip("狮子 Prefab")]
        public GameObject lionPrefab;
        [Tooltip("狼 Prefab")]
        public GameObject wolfPrefab;
        [Tooltip("鹿 Prefab（默认）")]
        public GameObject deerPrefab;

        [Header("说话动画")]
        [Tooltip("嘴巴骨骼名称（BlenderBiped Mouth / Jaw 等）")]
        public string mouthBoneName = "Mouth";
        [Tooltip("说话时嘴巴缩放")]
        public float speakingScale = 1.3f;
        [Tooltip("闭嘴时嘴巴缩放")]
        public float idleScale = 1.0f;
        [Tooltip("嘴巴动画平滑速度")]
        public float mouthLerpSpeed = 10f;
        [Tooltip("说话能量阈值（低于此值视为停止说话）")]
        public float speakingThreshold = 0.05f;

        [Header("物种切换冷却")]
        [Tooltip("物种切换最小间隔（秒，防止频繁闪烁）")]
        public float switchCooldown = 3.0f;

        [Header("调试")]
        public bool debugLog = false;

        // ============================================================
        // 公开属性
        // ============================================================

        /// <summary>说话人 ID</summary>
        public int SpeakerId
        {
            get => speakerId;
            set => speakerId = value;
        }

        /// <summary>当前物种类型</summary>
        public SpeciesType CurrentSpecies { get; private set; } = SpeciesType.deer;

        /// <summary>当前情绪标签</summary>
        public string CurrentEmotion { get; private set; } = "neutral";

        /// <summary>是否正在说话</summary>
        public bool IsSpeaking { get; private set; } = false;

        // ============================================================
        // 内部状态
        // ============================================================

        /// <summary>当前实例化的化身 GameObject</summary>
        private GameObject currentAvatar;

        /// <summary>嘴巴骨骼 Transform</summary>
        private Transform mouthBone;

        /// <summary>目标嘴巴缩放</summary>
        private float targetMouthScale = 1f;

        /// <summary>说话能量（0-1，由外部能量报告更新）</summary>
        private float speakingEnergy = 0f;

        /// <summary>上次物种切换时间</summary>
        private float lastSwitchTime = -999f;

        /// <summary>物种 Prefab 缓存</summary>
        private Dictionary<SpeciesType, GameObject> speciesPrefabMap;

        // ============================================================
        // Unity 生命周期
        // ============================================================

        void Awake()
        {
            // 构建 Prefab 映射
            speciesPrefabMap = new Dictionary<SpeciesType, GameObject>
            {
                { SpeciesType.tiger, tigerPrefab },
                { SpeciesType.rabbit, rabbitPrefab },
                { SpeciesType.owl, owlPrefab },
                { SpeciesType.fox, foxPrefab },
                { SpeciesType.lion, lionPrefab },
                { SpeciesType.wolf, wolfPrefab },
                { SpeciesType.deer, deerPrefab },
            };
        }

        void Start()
        {
            // 初始化默认物种
            if (currentAvatar == null)
            {
                SetSpecies(SpeciesType.deer, "neutral");
            }
        }

        void Update()
        {
            // 嘴巴动画插值
            if (mouthBone != null)
            {
                Vector3 scale = mouthBone.localScale;
                float currentScale = scale.x; // 统一缩放，取 x 分量
                float newScale = Mathf.Lerp(currentScale, targetMouthScale, mouthLerpSpeed * Time.deltaTime);
                mouthBone.localScale = new Vector3(newScale, newScale, newScale);
            }

            // 说话状态更新
            IsSpeaking = speakingEnergy > speakingThreshold;
            targetMouthScale = IsSpeaking ? speakingScale : idleScale;
        }

        // ============================================================
        // 物种切换
        // ============================================================

        /// <summary>
        /// 更新物种（从后端 species_update 消息触发）。
        /// 带冷却时间保护，防止频繁切换导致闪烁。
        /// </summary>
        /// <param name="species">物种字符串（tiger/rabbit/owl/...）</param>
        public void UpdateSpecies(string species)
        {
            SpeciesType newType = EnumConverter.ParseSpeciesType(species);
            SetSpecies(newType, CurrentEmotion);
        }

        /// <summary>
        /// 更新物种和情绪。
        /// </summary>
        public void UpdateSpeciesAndEmotion(string species, string emotion)
        {
            SpeciesType newType = EnumConverter.ParseSpeciesType(species);
            SetSpecies(newType, emotion);
        }

        /// <summary>
        /// 设置物种（内部方法，含冷却检查）。
        /// </summary>
        private void SetSpecies(SpeciesType newType, string emotion)
        {
            // 冷却检查（非首次切换）
            if (CurrentSpecies != newType && sampleCountForSwitch > 0)
            {
                float elapsed = Time.time - lastSwitchTime;
                if (elapsed < switchCooldown)
                {
                    if (debugLog)
                        Debug.Log($"[Avatar:{speakerId}] 物种切换冷却中（{elapsed:F1}/{switchCooldown}s）");
                    return;
                }
            }

            if (newType == CurrentSpecies && currentAvatar != null)
            {
                // 物种未变，仅更新情绪
                CurrentEmotion = emotion;
                return;
            }

            // 销毁旧化身
            if (currentAvatar != null)
            {
                Destroy(currentAvatar);
                currentAvatar = null;
                mouthBone = null;
            }

            CurrentSpecies = newType;
            CurrentEmotion = emotion;
            lastSwitchTime = Time.time;
            sampleCountForSwitch++;

            // 实例化新化身
            if (speciesPrefabMap != null && speciesPrefabMap.TryGetValue(newType, out GameObject prefab) && prefab != null)
            {
                currentAvatar = Instantiate(prefab, transform);
                currentAvatar.transform.localPosition = Vector3.zero;
                currentAvatar.transform.localRotation = Quaternion.identity;

                // 查找嘴巴骨骼
                mouthBone = FindDeepChild(currentAvatar.transform, mouthBoneName);

                if (debugLog)
                    Debug.Log($"[Avatar:{speakerId}] 物种切换 → {newType}, 情绪={emotion}, mouthBone={(mouthBone != null ? "found" : "not found")}");
            }
            else
            {
                if (debugLog)
                    Debug.LogWarning($"[Avatar:{speakerId}] 物种 {newType} 的 Prefab 未配置");
            }
        }

        private int sampleCountForSwitch = 0;

        // ============================================================
        // 说话能量更新
        // ============================================================

        /// <summary>
        /// 更新说话能量值（0-1）。
        /// 由外部 EnergyReportMessage 触发，驱动嘴巴动画。
        /// </summary>
        public void SetSpeakingEnergy(float energy)
        {
            speakingEnergy = Mathf.Clamp01(energy);
        }

        // ============================================================
        // 位置控制
        // ============================================================

        /// <summary>设置化身世界位置。</summary>
        public void SetPosition(Vector3 worldPos)
        {
            transform.position = worldPos;
        }

        /// <summary>设置化身旋转。</summary>
        public void SetRotation(Quaternion worldRot)
        {
            transform.rotation = worldRot;
        }

        /// <summary>获取化身位置。</summary>
        public Vector3 GetAvatarPosition()
        {
            return transform.position;
        }

        // ============================================================
        // 工具方法
        // ============================================================

        /// <summary>递归查找指定名称的子 Transform。</summary>
        private Transform FindDeepChild(Transform parent, string name)
        {
            // 广度优先
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
            }
            // 递归
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindDeepChild(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
