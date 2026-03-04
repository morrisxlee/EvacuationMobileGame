using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace SurvivalDemo.UI
{
    /// <summary>
    /// 結算面板：遊戲以任何方式結束時自動顯示，呈現本局成績。
    ///
    /// 支援三種結算類型：
    ///   GameOver      — 任務失敗（玩家耗盡所有復活機會後死亡）
    ///   EvacSuccess   — 撤離成功（完成防守倒計時後成功離開）
    ///   EmergencyEvac — 緊急撤離（電池集滿直接撤離）
    ///
    /// 建議場景結構：
    ///   Canvas
    ///   └── ResultPanelManager     （此腳本，保持 active）
    ///       └── ResultPanel        （_panelRoot，初始 SetActive false）
    ///           ├── TitleText      （TextMeshProUGUI — 「任務失敗」等）
    ///           ├── WaveText       （TextMeshProUGUI — 「到達波次：5」）
    ///           ├── KillsText      （TextMeshProUGUI — 「擊殺敵人：120」）
    ///           ├── CurrencyText   （TextMeshProUGUI — 「獲得金幣：350」）
    ///           ├── SurvivorsText  （TextMeshProUGUI — 「救援倖存者：3」）
    ///           └── ReturnButton   （Button — 「返回主選單」）
    ///
    /// 注意：
    ///   此腳本（ResultPanelManager）本身永遠保持 active，
    ///   只有 _panelRoot 才會被顯示/隱藏，以確保事件訂閱不中斷。
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  Inspector 槽位
        // ══════════════════════════════════════════════════════

        [TitleGroup("必要參照")]
        [Tooltip("真正要顯示/隱藏的 UI 根物件（例如 ResultPanel(DefaultDisable)）。\n" +
                 "此腳本（ResultPanelManager）本身必須保持 active；\n" +
                 "只有這個物件才會在結算時顯示。")]
        [LabelText("面板根物件")]
        [Required("必須指定面板根物件，否則 UI 永遠不會顯示！")]
        [SerializeField] private GameObject _panelRoot;

        [TitleGroup("必要參照")]
        [Tooltip("場景中玩家身上的 PlayerStats 組件。\n" +
                 "用於讀取本局最終持有的金幣數量。")]
        [LabelText("玩家數值（PlayerStats）")]
        [Required("必須指定 PlayerStats，否則金幣數無法顯示！")]
        [SerializeField] private Player.PlayerStats _playerStats;

        [TitleGroup("必要參照")]
        [Tooltip("返回主選單的按鈕。\n點擊後呼叫 GameLoopManager.ReturnToMenu()。")]
        [LabelText("返回主選單按鈕")]
        [Required("必須指定返回按鈕，否則玩家無法離開結算頁面！")]
        [SerializeField] private Button _returnButton;

        // ──────────────────────────────────────────────────────

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示結算類型的大標題，例如「任務失敗」、「撤離成功」。\n" +
                 "若留空則不更新。")]
        [LabelText("標題文字")]
        [SerializeField] private TextMeshProUGUI _titleText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示本局到達的波次，例如「到達波次：5」。\n" +
                 "若留空則不更新。")]
        [LabelText("波次文字")]
        [SerializeField] private TextMeshProUGUI _waveText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示本局擊殺的敵人總數，例如「擊殺敵人：120」。\n" +
                 "若留空則不更新。")]
        [LabelText("擊殺數文字")]
        [SerializeField] private TextMeshProUGUI _killsText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示本局最終持有的金幣，例如「獲得金幣：350」。\n" +
                 "若留空則不更新。")]
        [LabelText("金幣文字")]
        [SerializeField] private TextMeshProUGUI _currencyText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示本局救援的倖存者總數，例如「救援倖存者：3」。\n" +
                 "若留空則不更新。")]
        [LabelText("倖存者文字")]
        [SerializeField] private TextMeshProUGUI _survivorsText;

        // ──────────────────────────────────────────────────────

        [TitleGroup("偵錯")]
        [Tooltip("開啟後在 Awake 輸出完整的組件參照診斷，並在收到結算事件時輸出數據快照。\n" +
                 "正式上線前關閉此開關。")]
        [LabelText("啟用結算面板診斷")]
        [SerializeField] private bool _debugResult = true;

        // ══════════════════════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 事件訂閱必須在任何 SetActive 操作前完成
            Core.GameEvents.OnGameResult += HandleGameResult;

            _returnButton?.onClick.AddListener(OnReturnButtonClicked);

            SetPanelActive(false);

            if (_debugResult)
                DiagnoseSetup();
        }

        private void OnDestroy()
        {
            Core.GameEvents.OnGameResult -= HandleGameResult;
        }

        // ══════════════════════════════════════════════════════
        //  事件處理
        // ══════════════════════════════════════════════════════

        private void HandleGameResult(Core.ResultType resultType)
        {
            int wave      = (Core.WaveManager.Instance != null) ? Core.WaveManager.Instance.CurrentWaveIndex + 1 : 0;
            int kills     = Core.SessionStats.Instance?.EnemiesKilled    ?? 0;
            int currency  = _playerStats != null                         ? _playerStats.Currency : 0;
            int survivors = Core.SessionStats.Instance?.SurvivorsRescued ?? 0;

            if (_debugResult)
            {
                Core.DebugLogger.Log(
                    $"[ResultPanel] OnGameResult 收到。\n" +
                    $"  類型={resultType}  波次={wave}  擊殺={kills}  金幣={currency}  倖存者={survivors}\n" +
                    $"  WaveManager={(Core.WaveManager.Instance != null ? "OK" : "★ null")}\n" +
                    $"  SessionStats={(Core.SessionStats.Instance != null ? "OK" : "★ null")}\n" +
                    $"  PlayerStats={(_playerStats != null ? "OK" : "★ null")}",
                    Core.LogCategory.UI);
            }

            PopulateUI(resultType, wave, kills, currency, survivors);

            SetPanelActive(true);
            (_panelRoot != null ? _panelRoot.transform : transform).SetAsLastSibling();

            Core.DebugLogger.Log($"ResultPanel 顯示（{resultType}）。", Core.LogCategory.UI);
        }

        // ══════════════════════════════════════════════════════
        //  UI 填充
        // ══════════════════════════════════════════════════════

        private void PopulateUI(Core.ResultType resultType, int wave, int kills, int currency, int survivors)
        {
            if (_titleText != null)
            {
                _titleText.text = resultType switch
                {
                    Core.ResultType.GameOver      => "任務失敗",
                    Core.ResultType.EvacSuccess   => "撤離成功",
                    Core.ResultType.EmergencyEvac => "緊急撤離",
                    _                             => "結算"
                };
            }

            if (_waveText      != null) _waveText.text      = $"到達波次：{wave}";
            if (_killsText     != null) _killsText.text     = $"擊殺敵人：{kills}";
            if (_currencyText  != null) _currencyText.text  = $"獲得金幣：{currency}";
            if (_survivorsText != null) _survivorsText.text = $"救援倖存者：{survivors}";
        }

        // ══════════════════════════════════════════════════════
        //  按鈕回調
        // ══════════════════════════════════════════════════════

        private void OnReturnButtonClicked()
        {
            if (Core.GameLoopManager.Instance == null)
            {
                Core.DebugLogger.LogError(
                    "[ResultPanel] ★ GameLoopManager.Instance 為 null！無法返回主選單。\n" +
                    "  確認場景中存在 GameLoopManager 且它是 DontDestroyOnLoad 單例。",
                    Core.LogCategory.UI);
                return;
            }

            Core.DebugLogger.Log("ResultPanel：返回主選單。", Core.LogCategory.UI);
            Core.GameLoopManager.Instance.ReturnToMenu();
        }

        // ══════════════════════════════════════════════════════
        //  面板顯示/隱藏
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 只控制 _panelRoot；腳本自身的 GameObject（ResultPanelManager）永遠不會被關閉，
        /// 否則事件訂閱會隨 OnDisable 一同消失。
        /// </summary>
        private void SetPanelActive(bool active)
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(active);
                return;
            }

            Core.DebugLogger.LogOnceError(
                "ResultPanel_NoPanelRoot",
                "[ResultPanel] ★ 根因確認：_panelRoot（面板根物件）未設定！\n" +
                "  UI 永遠不會顯示。腳本本身不會被關閉（保護事件訂閱）。\n" +
                "  修復：在 Inspector 將 ResultPanel(DefaultDisable) 拖入「面板根物件」欄位。",
                Core.LogCategory.UI);
        }

        // ══════════════════════════════════════════════════════
        //  診斷（Debug 模式）
        // ══════════════════════════════════════════════════════

        private void DiagnoseSetup()
        {
            Core.DebugLogger.LogOnce(
                "ResultPanel_ScriptHost",
                $"[ResultPanel 診斷] 腳本掛載在：{gameObject.name}  active={gameObject.activeSelf}\n" +
                $"  → 此物件必須保持 active，否則結算事件永遠收不到。",
                Core.LogCategory.UI);

            if (_panelRoot == null)
                Core.DebugLogger.LogOnceError(
                    "ResultPanel_PanelRootMissing",
                    "[ResultPanel 診斷] ★ 根因候選：_panelRoot（面板根物件）未設定！\n" +
                    "  修復：在 Inspector 將 ResultPanel(DefaultDisable) 拖入「面板根物件」欄位，\n" +
                    "  並確認該物件在場景中初始為 SetActive(false)。",
                    Core.LogCategory.UI);
            else
                Core.DebugLogger.LogOnce(
                    "ResultPanel_PanelRoot",
                    $"[ResultPanel 診斷] _panelRoot = {_panelRoot.name}  active={_panelRoot.activeSelf}  " +
                    $"父物件：{(_panelRoot.transform.parent != null ? _panelRoot.transform.parent.name : "（無父物件）")}",
                    Core.LogCategory.UI);

            if (_playerStats == null)
                Core.DebugLogger.LogOnceError(
                    "ResultPanel_PlayerStatsMissing",
                    "[ResultPanel 診斷] ★ 根因候選：_playerStats（PlayerStats）未設定！\n" +
                    "  金幣數據將顯示為 0。\n" +
                    "  修復：在 Inspector 將玩家物件（含 PlayerStats）拖入「玩家數值」欄位。",
                    Core.LogCategory.UI);
            else
                Core.DebugLogger.LogOnce(
                    "ResultPanel_PlayerStats",
                    $"[ResultPanel 診斷] _playerStats = {_playerStats.gameObject.name}  OK",
                    Core.LogCategory.UI);

            if (_returnButton == null)
                Core.DebugLogger.LogOnceError(
                    "ResultPanel_ReturnButtonMissing",
                    "[ResultPanel 診斷] ★ 根因候選：_returnButton（返回按鈕）未設定！\n" +
                    "  玩家將無法離開結算頁面。\n" +
                    "  修復：在 Inspector 將返回主選單按鈕拖入「返回主選單按鈕」欄位。",
                    Core.LogCategory.UI);
            else
                Core.DebugLogger.LogOnce(
                    "ResultPanel_ReturnButton",
                    $"[ResultPanel 診斷] _returnButton = {_returnButton.gameObject.name}  OK",
                    Core.LogCategory.UI);

            Core.DebugLogger.LogOnce(
                "ResultPanel_EventSub",
                "[ResultPanel 診斷] OnGameResult 訂閱完成。\n" +
                "  若遊戲結束後看不到「ResultPanel 顯示」的 log，\n" +
                "  請確認 GameLoopManager 有正確呼叫 FireGameResult()。",
                Core.LogCategory.UI);
        }
    }
}
