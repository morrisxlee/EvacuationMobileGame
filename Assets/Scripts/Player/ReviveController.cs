using System.Collections;
using UnityEngine;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 復活控制器：監聽玩家死亡事件，判斷是否有復活機會。
    ///
    /// 若有機會：呼叫 GameLoopManager.EnterReviving()（凍結時間）並廣播
    /// GameEvents.OnReviveOpportunity，由 RevivePanel UI 處理後續玩家選擇。
    ///
    /// RevivePanel 會在玩家選擇後呼叫此腳本上的公開方法：
    ///   GiveUp()              → 直接 Game Over
    ///   ExecuteBatteryRevive() → 扣電池並復活
    ///   ExecuteAdRevive()      → 播廣告，成功則復活
    /// </summary>
    public class ReviveController : MonoBehaviour
    {
        [SerializeField] private PlayerStats _playerStats;
        private Feedback.FeedbackBridge _feedbackBridge;

        [Tooltip("玩家死亡後幾秒才凍結時間並彈出復活面板（真實時間，不受 timeScale 影響）。")]
        [SerializeField] private float _revivePopupDelay = 1f;

        [Space]
        [Tooltip("開啟後輸出復活流程完整診斷日誌。\n" +
                 "Awake 時一次性印出參照狀態；每次死亡印出決策快照與協程進度。\n" +
                 "正式上線前關閉此開關。")]
        [SerializeField] private bool _debugRevive = true;

        private Coroutine _reviveSequenceCoroutine;

        private void Awake()
        {
            if (_playerStats == null)
                _playerStats = GetComponent<PlayerStats>();
            _feedbackBridge = GetComponent<Feedback.FeedbackBridge>();

            if (_debugRevive)
                DiagnoseSetup();
        }

        private void OnEnable()
        {
            Core.GameEvents.OnPlayerDied += HandlePlayerDied;
        }

        private void OnDisable()
        {
            Core.GameEvents.OnPlayerDied -= HandlePlayerDied;
        }

        // ══════════════════════════════════════
        //  診斷（Debug 模式）
        // ══════════════════════════════════════

        private void DiagnoseSetup()
        {
            Core.DebugLogger.LogOnce(
                "ReviveCtrl_ScriptHost",
                $"[ReviveController 診斷] 腳本掛載在：{gameObject.name}  active={gameObject.activeSelf}  enabled={enabled}",
                Core.LogCategory.Player);

            if (_playerStats == null)
                Core.DebugLogger.LogOnce(
                    "ReviveCtrl_StatsMissing",
                    "[ReviveController 診斷] ★ 根因候選：PlayerStats 為 null！\n" +
                    "  Awake 嘗試 GetComponent 失敗，表示此物件沒有 PlayerStats。\n" +
                    "  修復：確認 ReviveController 掛在有 PlayerStats 的玩家物件上。",
                    Core.LogCategory.Player);
            else
                Core.DebugLogger.LogOnce(
                    "ReviveCtrl_Stats",
                    $"[ReviveController 診斷] PlayerStats = {_playerStats.gameObject.name}  OK",
                    Core.LogCategory.Player);

            var config = Core.GameLoopManager.Instance?.Config;
            Core.DebugLogger.LogOnce(
                "ReviveCtrl_Config",
                $"[ReviveController 診斷] GameLoopManager={(Core.GameLoopManager.Instance != null ? "OK" : "★ null")}  " +
                $"Config={(config != null ? "OK" : "★ null")}  " +
                $"MaxRevives={(config != null ? config.MaxRevives.ToString() : "（無法讀取）")}  " +
                $"BatteryCost={(config != null ? config.FirstReviveBatteryCost.ToString() : "（無法讀取）")}",
                Core.LogCategory.Player);

            Core.DebugLogger.LogOnce(
                "ReviveCtrl_EventSub",
                "[ReviveController 診斷] OnPlayerDied 訂閱完成（OnEnable）。\n" +
                "  若死亡後看不到「OnPlayerDied 收到」的 log，表示此腳本 disabled 或事件沒有 Fire。",
                Core.LogCategory.Player);
        }

        // ══════════════════════════════════════
        //  死亡事件入口
        // ══════════════════════════════════════

        private void HandlePlayerDied()
        {
            if (_playerStats == null)
            {
                Core.DebugLogger.LogError("ReviveController 缺少 PlayerStats！", Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
                return;
            }

            var config = Core.GameLoopManager.Instance?.Config;
            int maxRevives  = config != null ? config.MaxRevives : 3;
            int batteryCost = config != null ? config.FirstReviveBatteryCost : 1;
            int reviveCount = _playerStats.ReviveCount;

            bool canUseBattery = (reviveCount == 0) && (_playerStats.Battery >= batteryCost);
            bool canWatchAd    = Core.AdService.Instance != null && Core.AdService.Instance.IsAdReady();

            if (_debugRevive)
            {
                Core.DebugLogger.Log(
                    $"[ReviveController] OnPlayerDied 收到，決策快照：\n" +
                    $"  ReviveCount={reviveCount}/{maxRevives}  " +
                    $"Battery={_playerStats.Battery}/{batteryCost}  " +
                    $"CanBattery={canUseBattery}  CanAd={canWatchAd}\n" +
                    $"  AdService={(Core.AdService.Instance != null ? Core.AdService.Instance.gameObject.name : "★ null（場景缺少 AdService）")}\n" +
                    $"  GameLoopManager={(Core.GameLoopManager.Instance != null ? "OK" : "★ null")}  " +
                    $"CurrentState={Core.GameLoopManager.Instance?.CurrentState}",
                    Core.LogCategory.Player);
            }

            if (reviveCount >= maxRevives)
            {
                Core.DebugLogger.Log("已達最大復活次數，直接 Game Over。", Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
                return;
            }

            if (!canUseBattery && !canWatchAd)
            {
                Core.DebugLogger.Log(
                    $"[ReviveController] 電池不足且廣告不可用，直接 Game Over。\n" +
                    $"  Battery={_playerStats.Battery} 需要 {batteryCost}，CanAd={canWatchAd}",
                    Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
                return;
            }

            var data = new Core.ReviveOpportunityData
            {
                ReviveCount   = reviveCount,
                MaxRevives    = maxRevives,
                Battery       = _playerStats.Battery,
                BatteryCost   = batteryCost,
                CanUseBattery = canUseBattery,
                CanWatchAd    = canWatchAd
            };

            if (_debugRevive)
                Core.DebugLogger.Log(
                    $"[ReviveController] 啟動 ReviveSequence，將在 {_revivePopupDelay}s 後 EnterReviving + FireReviveOpportunity。",
                    Core.LogCategory.Player);

            // 延遲後再凍結時間並彈出面板（使用真實時間，不受 timeScale 影響）
            if (_reviveSequenceCoroutine != null)
                StopCoroutine(_reviveSequenceCoroutine);
            _reviveSequenceCoroutine = StartCoroutine(ReviveSequence(data));
        }

        private IEnumerator ReviveSequence(Core.ReviveOpportunityData data)
        {
            Core.DebugLogger.Log($"玩家死亡，{_revivePopupDelay}s 後彈出復活面板。", Core.LogCategory.Player);
            yield return new WaitForSecondsRealtime(_revivePopupDelay);

            _reviveSequenceCoroutine = null;

            if (_debugRevive)
                Core.DebugLogger.Log(
                    $"[ReviveController] 延遲結束，執行 EnterReviving()。\n" +
                    $"  currentState={Core.GameLoopManager.Instance?.CurrentState}  timeScale={Time.timeScale}",
                    Core.LogCategory.Player);

            // 凍結時間，進入 Reviving 狀態，交給 RevivePanel 處理玩家選擇
            Core.GameLoopManager.Instance?.EnterReviving();

            if (_debugRevive)
                Core.DebugLogger.Log(
                    $"[ReviveController] FireReviveOpportunity 即將觸發。\n" +
                    $"  timeScale={Time.timeScale}  currentState={Core.GameLoopManager.Instance?.CurrentState}",
                    Core.LogCategory.Player);

            Core.GameEvents.FireReviveOpportunity(data);
        }

        // ══════════════════════════════════════
        //  RevivePanel 呼叫的公開方法
        // ══════════════════════════════════════

        /// <summary>
        /// 玩家選擇放棄，直接結束遊戲。
        /// </summary>
        public void GiveUp()
        {
            Core.DebugLogger.Log("玩家選擇放棄，Game Over。", Core.LogCategory.Player);
            Core.GameLoopManager.Instance?.GameOver();
        }

        /// <summary>
        /// 玩家選擇消耗電池復活（僅第 1 次可用）。
        /// </summary>
        public void ExecuteBatteryRevive()
        {
            if (_playerStats == null)
            {
                Core.DebugLogger.LogError("ExecuteBatteryRevive：PlayerStats 為 null！", Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
                return;
            }

            bool success = _playerStats.TryRevive(false);
            if (success)
            {
                Core.DebugLogger.Log("電池復活成功。", Core.LogCategory.Player);
                _feedbackBridge?.PlayRevive();
                Core.GameLoopManager.Instance?.ExitReviving();
            }
            else
            {
                Core.DebugLogger.LogError("電池復活失敗（TryRevive 回傳 false），Game Over。", Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
            }
        }

        /// <summary>
        /// 玩家選擇觀看廣告復活（第 2~3 次，或首次電池不足時）。
        /// </summary>
        public void ExecuteAdRevive()
        {
            var adService = Core.AdService.Instance;
            if (adService == null || !adService.IsAdReady())
            {
                Core.DebugLogger.Log("廣告不可用，Game Over。", Core.LogCategory.Player);
                Core.GameLoopManager.Instance?.GameOver();
                return;
            }

            adService.ShowRewardedAd(
                onCompleted: () =>
                {
                    if (_playerStats == null)
                    {
                        Core.DebugLogger.LogError("ExecuteAdRevive onCompleted：PlayerStats 為 null！", Core.LogCategory.Player);
                        Core.GameLoopManager.Instance?.GameOver();
                        return;
                    }

                    bool success = _playerStats.TryRevive(true);
                    if (success)
                    {
                        Core.DebugLogger.Log("廣告復活成功。", Core.LogCategory.Player);
                        _feedbackBridge?.PlayRevive();
                        Core.GameLoopManager.Instance?.ExitReviving();
                    }
                    else
                    {
                        Core.DebugLogger.LogError("廣告復活失敗（TryRevive 回傳 false），Game Over。", Core.LogCategory.Player);
                        Core.GameLoopManager.Instance?.GameOver();
                    }
                },
                onFailed: () =>
                {
                    Core.DebugLogger.Log("廣告觀看失敗或取消，Game Over。", Core.LogCategory.Player);
                    Core.GameLoopManager.Instance?.GameOver();
                }
            );
        }
    }
}
