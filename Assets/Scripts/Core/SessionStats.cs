using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 當局遊戲數據統計：追蹤本局的敵人擊殺數與倖存者救援數。
    ///
    /// 此腳本掛在遊戲場景內的 Managers 物件上（非 DontDestroyOnLoad）。
    /// 場景每次重新載入時自動重置，天然支援「多局遊戲」無需手動清零。
    ///
    /// 其他腳本透過 SessionStats.Instance 讀取數據：
    ///   int kills    = SessionStats.Instance?.EnemiesKilled ?? 0;
    ///   int survivors = SessionStats.Instance?.SurvivorsRescued ?? 0;
    ///
    /// 效能說明：
    ///   OnEnemyKilled 每隻敵人死亡觸發一次，僅執行整數自增，無 GC 壓力。
    ///   1000+ 敵人同時死亡時，單幀累計 1000 次 int++，仍屬微秒級操作。
    /// </summary>
    public class SessionStats : MonoBehaviour
    {
        private static SessionStats _instance;
        public static SessionStats Instance => _instance;

        // ══════════════════════════════════════════════════════
        //  執行時狀態（唯讀顯示）
        // ══════════════════════════════════════════════════════

        [TitleGroup("當局統計（唯讀）")]
        [Tooltip("本局累計擊殺的敵人總數。\n每次 OnEnemyKilled 事件觸發時自動遞增。")]
        [ShowInInspector, ReadOnly, LabelText("擊殺敵人數")]
        public int EnemiesKilled { get; private set; }

        [TitleGroup("當局統計（唯讀）")]
        [Tooltip("本局累計救援的倖存者總數。\n每次 OnSurvivorRescued 事件觸發時自動遞增。")]
        [ShowInInspector, ReadOnly, LabelText("救援倖存者數")]
        public int SurvivorsRescued { get; private set; }

        // ══════════════════════════════════════════════════════
        //  偵錯
        // ══════════════════════════════════════════════════════

        [TitleGroup("偵錯")]
        [Tooltip("開啟後，每次擊殺敵人或救援倖存者時輸出統計日誌。\n" +
                 "1000 隻敵人同時死亡時會產生大量 log，只在需要時短暫開啟。")]
        [LabelText("啟用統計診斷")]
        [SerializeField] private bool _debugStats = false;

        // ══════════════════════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                DebugLogger.LogOnceError(
                    "SessionStats_Duplicate",
                    "[SessionStats] ★ 場景中存在多個 SessionStats 實例！將摧毀多餘的副本。\n" +
                    "  修復：確認場景內只有一個 SessionStats 組件。",
                    LogCategory.Core);
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnEnable()
        {
            GameEvents.OnEnemyKilled     += HandleEnemyKilled;
            GameEvents.OnSurvivorRescued += HandleSurvivorRescued;
        }

        private void OnDisable()
        {
            GameEvents.OnEnemyKilled     -= HandleEnemyKilled;
            GameEvents.OnSurvivorRescued -= HandleSurvivorRescued;
        }

        // ══════════════════════════════════════════════════════
        //  事件處理
        // ══════════════════════════════════════════════════════

        private void HandleEnemyKilled(int enemyId)
        {
            EnemiesKilled++;

            if (_debugStats)
                DebugLogger.Log($"[SessionStats] 敵人擊殺 +1（id={enemyId}），累計={EnemiesKilled}", LogCategory.Core);
        }

        private void HandleSurvivorRescued(int survivorId)
        {
            SurvivorsRescued++;

            if (_debugStats)
                DebugLogger.Log($"[SessionStats] 倖存者救援 +1（id={survivorId}），累計={SurvivorsRescued}", LogCategory.Core);
        }
    }
}
