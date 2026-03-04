using System.Collections.Generic;
using UnityEngine;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 全域 Debug 日誌系統，可在 Inspector 中開關各類別日誌。
    /// 不做靜默 fallback——錯誤一律打日誌讓開發者立即知道問題。
    /// 使用方式：DebugLogger.Log("訊息", LogCategory.Combat);
    /// LogOnce("唯一key", "訊息", LogCategory.AI) 確保相同 key 全局只打一次，防止 log 爆炸。
    /// </summary>
    public class DebugLogger : MonoBehaviour
    {
        private static DebugLogger _instance;

        [Header("日誌類別開關")]
        [Tooltip("Core 系統訊息（GameLoopManager、GameEvents 等）。")]
        [SerializeField] private bool _enableCore = true;

        [Tooltip("玩家相關訊息（移動、輸入、復活等）。")]
        [SerializeField] private bool _enablePlayer = true;

        [Tooltip("戰鬥系統訊息（武器、投射物、傷害等）。")]
        [SerializeField] private bool _enableCombat = true;

        [Tooltip("AI 系統訊息（A* 路徑、攻擊決策）。關閉後仍會輸出 LogError。")]
        [SerializeField] private bool _enableAI = true;

        [Tooltip("互動物件訊息（門、搜索點、撤離區等）。")]
        [SerializeField] private bool _enableInteraction = true;

        [Tooltip("成長系統訊息（升級、解鎖等）。")]
        [SerializeField] private bool _enableProgression = true;

        [Tooltip("生怪系統訊息（波次、Coroutine 排程等）。")]
        [SerializeField] private bool _enableSpawn = true;

        [Tooltip("物件池訊息（Spawn / Despawn 次數等）。")]
        [SerializeField] private bool _enablePooling = true;

        [Tooltip("回饋系統訊息（MMF、特效等）。")]
        [SerializeField] private bool _enableFeedback = true;

        [Tooltip("UI 系統訊息。")]
        [SerializeField] private bool _enableUI = true;

        [Space]
        [Header("── 敵人詳細診斷（預設 OFF，開啟前先確認 EnemyTickManager 的同名開關也已開啟）──")]
        [Tooltip("開啟後輸出每隻敵人的細粒度狀態：首次 Tick、首次路徑成功、LoS 狀態轉換、卡牆事件。\n" +
                 "此類別與 EnemyTickManager 的「詳細敵人診斷」開關同時為 ON 時才實際輸出。\n" +
                 "建議只在定位問題時短暫開啟，避免 1000 隻敵人造成 log 爆炸。")]
        [SerializeField] private bool _enableEnemyDiag = false;

        // ── LogOnce 去重表 ──
        // 靜態，跨場景持久；相同 key 只打一次，適合系統級一次性診斷訊息。
        private static readonly HashSet<string> s_loggedKeys = new();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void Log(string message, LogCategory category = LogCategory.Core)
        {
            if (_instance == null)
            {
                Debug.Log($"[{category}] {message}");
                return;
            }
            if (!_instance.IsCategoryEnabled(category)) return;
            Debug.Log($"[{category}] {message}");
        }

        public static void LogWarning(string message, LogCategory category = LogCategory.Core)
        {
            if (_instance == null)
            {
                Debug.LogWarning($"[{category}] {message}");
                return;
            }
            if (!_instance.IsCategoryEnabled(category)) return;
            Debug.LogWarning($"[{category}] {message}");
        }

        public static void LogError(string message, LogCategory category = LogCategory.Core)
        {
            // 錯誤一律輸出，不受開關影響
            Debug.LogError($"[{category}] {message}");
        }

        /// <summary>
        /// 以唯一 key 去重——相同 key 在整個遊戲執行期間只打一次 Log。
        /// 適用於「系統級一次性診斷」（如 Physics Matrix 驗證結果）。
        /// key 建議格式：「系統名_事件名」，例如 "TickMgr_PhysicsMatrix"。
        /// </summary>
        public static void LogOnce(string key, string message, LogCategory category = LogCategory.Core)
        {
            if (s_loggedKeys.Contains(key)) return;
            s_loggedKeys.Add(key);
            Log(message, category);
        }

        /// <summary>
        /// LogOnce 的 Warning 版本。相同 key 只打一次 Warning。
        /// </summary>
        public static void LogOnceWarning(string key, string message, LogCategory category = LogCategory.Core)
        {
            if (s_loggedKeys.Contains(key)) return;
            s_loggedKeys.Add(key);
            LogWarning(message, category);
        }

        /// <summary>
        /// LogOnce 的 Error 版本。相同 key 只打一次 Error（Error 不受類別開關影響）。
        /// </summary>
        public static void LogOnceError(string key, string message, LogCategory category = LogCategory.Core)
        {
            if (s_loggedKeys.Contains(key)) return;
            s_loggedKeys.Add(key);
            LogError(message, category);
        }

        /// <summary>
        /// 清除 LogOnce 的去重紀錄，讓所有 key 可以再次輸出。
        /// 適合在場景重新載入後呼叫，避免跨場景遺漏診斷訊息。
        /// </summary>
        public static void ClearLogOnceKeys()
        {
            s_loggedKeys.Clear();
        }

        /// <summary>
        /// 供 EnemyTickManager 查詢：EnemyDiag 類別是否啟用。
        /// EnemyController 不直接呼叫此方法，統一由 EnemyTickManager 傳入狀態。
        /// </summary>
        public static bool IsEnemyDiagEnabled => _instance != null && _instance._enableEnemyDiag;

        private bool IsCategoryEnabled(LogCategory category)
        {
            return category switch
            {
                LogCategory.Core => _enableCore,
                LogCategory.Player => _enablePlayer,
                LogCategory.Combat => _enableCombat,
                LogCategory.AI => _enableAI,
                LogCategory.Interaction => _enableInteraction,
                LogCategory.Progression => _enableProgression,
                LogCategory.Spawn => _enableSpawn,
                LogCategory.Pooling => _enablePooling,
                LogCategory.Feedback => _enableFeedback,
                LogCategory.UI => _enableUI,
                LogCategory.EnemyDiag => _enableEnemyDiag,
                _ => true
            };
        }
    }

    public enum LogCategory
    {
        Core,
        Player,
        Combat,
        AI,
        Interaction,
        Progression,
        Spawn,
        Pooling,
        Feedback,
        UI,
        /// <summary>
        /// 細粒度敵人診斷：每隻敵人的首次 Tick、路徑成功、LoS 轉換、卡牆事件。
        /// 需要同時在 DebugLogger 與 EnemyTickManager 兩處開啟才生效。
        /// 建議只在定位問題時短暫開啟。
        /// </summary>
        EnemyDiag
    }
}
