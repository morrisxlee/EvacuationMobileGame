using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace SurvivalDemo.UI
{
    /// <summary>
    /// 復活選擇面板。
    ///
    /// 掛在 Canvas 下的 RevivePanel GameObject 上，預設 SetActive(false)。
    /// 玩家死亡且有復活機會時自動顯示，提供三個選項。
    ///
    /// 建議場景結構：
    ///   Canvas
    ///   └── RevivePanel        (此腳本)
    ///       ├── DimBackground  (Image — 全螢幕半透明遮罩)
    ///       ├── TitleText      (TextMeshProUGUI — "你陣亡了")
    ///       ├── ReviveCountText(TextMeshProUGUI — "復活次數：1 / 3")
    ///       ├── CountdownText  (TextMeshProUGUI — "10s 後自動放棄")
    ///       ├── BatteryButton  (Button — "消耗電池復活 (需 1 顆)")
    ///       │   └── BatteryButtonText (TextMeshProUGUI)
    ///       ├── AdButton       (Button — "觀看廣告復活")
    ///       └── GiveUpButton   (Button — "放棄")
    ///
    /// 注意：因遊戲凍結（timeScale=0），所有延遲必須使用 WaitForSecondsRealtime。
    /// </summary>
    public class RevivePanel : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  Inspector 槽位
        // ══════════════════════════════════════════════════════

        [TitleGroup("必要參照")]
        [Tooltip("真正要顯示/隱藏的 UI 根物件（通常是 RevivePanel(DefaultDisable)）。\n" +
                 "若留空，腳本會直接控制自己所在的 GameObject。\n" +
                 "當腳本掛在 Manager 空物件上時，務必拖入實際 UI 根物件。")]
        [LabelText("面板根物件")]
        [SerializeField] private GameObject _panelRoot;

        [TitleGroup("必要參照")]
        [Tooltip("場景中掛有 ReviveController 的玩家物件（或直接拖入 ReviveController）。")]
        [LabelText("復活控制器")]
        [Required("必須指定 ReviveController，否則按鈕無法觸發復活！")]
        [SerializeField] private Player.ReviveController _reviveController;

        // ──────────────────────────────────────────────────────

        [TitleGroup("按鈕（必要）")]
        [Tooltip("消耗電池復活的按鈕。\n僅在首次復活且電池足夠時啟用。")]
        [LabelText("電池復活按鈕")]
        [Required]
        [SerializeField] private Button _batteryButton;

        [TitleGroup("按鈕（必要）")]
        [Tooltip("觀看廣告復活的按鈕。\n在第 2~3 次復活或電池不足時啟用。")]
        [LabelText("廣告復活按鈕")]
        [Required]
        [SerializeField] private Button _adButton;

        [TitleGroup("按鈕（必要）")]
        [Tooltip("放棄並結束遊戲的按鈕。\n永遠啟用。")]
        [LabelText("放棄按鈕")]
        [Required]
        [SerializeField] private Button _giveUpButton;

        // ──────────────────────────────────────────────────────

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示電池按鈕說明的文字，例如「消耗電池復活（需 1 顆）」。")]
        [LabelText("電池按鈕文字")]
        [SerializeField] private TextMeshProUGUI _batteryButtonText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示目前復活次數的文字，例如「復活次數：1 / 3」。")]
        [LabelText("復活次數文字")]
        [SerializeField] private TextMeshProUGUI _reviveCountText;

        [TitleGroup("UI 文字（選填）")]
        [Tooltip("顯示倒數計時的文字，例如「10s 後自動放棄」。\n倒數結束時自動觸發放棄。")]
        [LabelText("倒數計時文字")]
        [SerializeField] private TextMeshProUGUI _countdownText;

        // ──────────────────────────────────────────────────────

        [TitleGroup("倒數設定")]
        [Tooltip("玩家無操作時自動放棄的倒數秒數。\n設為 0 則停用倒數。")]
        [LabelText("自動放棄倒數（秒）")]
        [Min(0f)]
        [SerializeField] private float _autoGiveUpSeconds = 10f;

        // ──────────────────────────────────────────────────────

        [TitleGroup("偵錯")]
        [Tooltip("開啟後輸出復活面板完整診斷日誌。\n" +
                 "Awake 時一次性印出所有參照狀態；每次死亡印出事件是否收到、panelRoot 是否正確切換。\n" +
                 "正式上線前關閉此開關。")]
        [LabelText("啟用復活面板診斷")]
        [SerializeField] private bool _debugRevive = true;

        // ══════════════════════════════════════════════════════
        //  私有狀態
        // ══════════════════════════════════════════════════════

        private Coroutine _countdownCoroutine;
        private bool _choiceMade; // 防止玩家重複觸發

        // ══════════════════════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 事件訂閱必須在 SetActive(false) 前完成，否則 OnEnable 不會觸發導致訂閱遺失。
            // 注意：此腳本的 gameObject（RevivePanelManager）本身永遠保持 active，
            // 只有 _panelRoot（真正的 UI 物件）才會被顯示/隱藏。
            Core.GameEvents.OnReviveOpportunity += HandleReviveOpportunity;
            Core.GameEvents.OnPlayerRevived     += HandlePlayerRevived;
            Core.GameEvents.OnGameOver          += HandleGameOver;

            ValidateButtons();
            SetPanelActive(false);

            // ── 一次性設定診斷（每 Play Session 只打一次，不 spam）──
            if (_debugRevive)
                DiagnoseSetup();
        }

        private void OnDestroy()
        {
            Core.GameEvents.OnReviveOpportunity -= HandleReviveOpportunity;
            Core.GameEvents.OnPlayerRevived     -= HandlePlayerRevived;
            Core.GameEvents.OnGameOver          -= HandleGameOver;
        }

        // ══════════════════════════════════════════════════════
        //  事件處理
        // ══════════════════════════════════════════════════════

        private void HandleReviveOpportunity(Core.ReviveOpportunityData data)
        {
            _choiceMade = false;

            if (_debugRevive)
            {
                var target = _panelRoot != null ? _panelRoot : gameObject;
                Core.DebugLogger.Log(
                    $"[RevivePanel] OnReviveOpportunity 收到。\n" +
                    $"  控制目標：{target.name}（目前 active={target.activeSelf}）\n" +
                    $"  ReviveCount={data.ReviveCount}/{data.MaxRevives}  " +
                    $"Battery={data.Battery}/{data.BatteryCost}  " +
                    $"CanBattery={data.CanUseBattery}  CanAd={data.CanWatchAd}",
                    Core.LogCategory.UI);
            }

            ConfigureButtons(data);
            UpdateInfoTexts(data);

            SetPanelActive(true);
            // 強制置頂，避免被其他全螢幕 UI（例如 HUD/其他面板）蓋住
            var panelTransform = _panelRoot != null ? _panelRoot.transform : transform;
            panelTransform.SetAsLastSibling();

            if (_debugRevive)
            {
                var target = _panelRoot != null ? _panelRoot : gameObject;
                Core.DebugLogger.Log(
                    $"[RevivePanel] SetActive(true) 執行完畢。\n" +
                    $"  目標物件：{target.name}  active={target.activeSelf}\n" +
                    $"  父物件：{(target.transform.parent != null ? target.transform.parent.name : "（無父物件）")}\n" +
                    $"  Hierarchy 順序（sibling index）：{target.transform.GetSiblingIndex()}",
                    Core.LogCategory.UI);
            }

            Core.DebugLogger.Log("RevivePanel 顯示。", Core.LogCategory.UI);

            if (_autoGiveUpSeconds > 0f)
            {
                if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = StartCoroutine(CountdownCoroutine());
            }
            else if (_countdownText != null)
            {
                _countdownText.gameObject.SetActive(false);
            }
        }

        private void HandlePlayerRevived()
        {
            HidePanel();
        }

        private void HandleGameOver()
        {
            HidePanel();
        }

        // ══════════════════════════════════════════════════════
        //  按鈕設定
        // ══════════════════════════════════════════════════════

        private void ConfigureButtons(Core.ReviveOpportunityData data)
        {
            // 電池按鈕：僅首次且電池足夠時可用
            if (_batteryButton != null)
            {
                _batteryButton.interactable = data.CanUseBattery;
                _batteryButton.onClick.RemoveAllListeners();
                _batteryButton.onClick.AddListener(OnBatteryButtonClicked);
            }

            // 廣告按鈕：廣告可用時啟用
            if (_adButton != null)
            {
                _adButton.interactable = data.CanWatchAd;
                _adButton.onClick.RemoveAllListeners();
                _adButton.onClick.AddListener(OnAdButtonClicked);
            }

            // 放棄按鈕：永遠啟用
            if (_giveUpButton != null)
            {
                _giveUpButton.interactable = true;
                _giveUpButton.onClick.RemoveAllListeners();
                _giveUpButton.onClick.AddListener(OnGiveUpButtonClicked);
            }

            // 電池按鈕說明文字
            if (_batteryButtonText != null)
                _batteryButtonText.text = $"消耗電池復活（需 {data.BatteryCost} 顆，現有 {data.Battery} 顆）";
        }

        private void UpdateInfoTexts(Core.ReviveOpportunityData data)
        {
            if (_reviveCountText != null)
                _reviveCountText.text = $"復活次數：{data.ReviveCount} / {data.MaxRevives}";
        }

        // ══════════════════════════════════════════════════════
        //  按鈕回調
        // ══════════════════════════════════════════════════════

        private void OnBatteryButtonClicked()
        {
            if (_choiceMade) return;
            _choiceMade = true;

            StopCountdown();
            SetAllButtonsInteractable(false);

            if (_reviveController == null)
            {
                Core.DebugLogger.LogError("RevivePanel：未指定 ReviveController！", Core.LogCategory.Player);
                return;
            }

            _reviveController.ExecuteBatteryRevive();
        }

        private void OnAdButtonClicked()
        {
            if (_choiceMade) return;
            _choiceMade = true;

            StopCountdown();
            SetAllButtonsInteractable(false);

            if (_reviveController == null)
            {
                Core.DebugLogger.LogError("RevivePanel：未指定 ReviveController！", Core.LogCategory.Player);
                return;
            }

            _reviveController.ExecuteAdRevive();
        }

        private void OnGiveUpButtonClicked()
        {
            if (_choiceMade) return;
            _choiceMade = true;

            StopCountdown();
            SetAllButtonsInteractable(false);

            if (_reviveController == null)
            {
                Core.DebugLogger.LogError("RevivePanel：未指定 ReviveController！", Core.LogCategory.Player);
                return;
            }

            _reviveController.GiveUp();
        }

        // ══════════════════════════════════════════════════════
        //  倒數計時
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 倒數計時協程：使用 WaitForSecondsRealtime，不受 timeScale=0 影響。
        /// 倒數結束後自動觸發放棄。
        /// </summary>
        private IEnumerator CountdownCoroutine()
        {
            float remaining = _autoGiveUpSeconds;

            while (remaining > 0f)
            {
                if (_countdownText != null)
                {
                    _countdownText.gameObject.SetActive(true);
                    _countdownText.text = $"{Mathf.CeilToInt(remaining)}s 後自動放棄";
                }

                yield return new WaitForSecondsRealtime(0.1f);
                remaining -= 0.1f;
            }

            _countdownCoroutine = null;

            if (!_choiceMade)
            {
                Core.DebugLogger.Log("倒數結束，自動放棄。", Core.LogCategory.Player);
                OnGiveUpButtonClicked();
            }
        }

        private void StopCountdown()
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
            }

            if (_countdownText != null)
                _countdownText.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  面板控制
        // ══════════════════════════════════════════════════════

        private void HidePanel()
        {
            StopCountdown();
            Core.DebugLogger.Log("RevivePanel 隱藏。", Core.LogCategory.UI);
            SetPanelActive(false);
        }

        private void SetAllButtonsInteractable(bool interactable)
        {
            if (_batteryButton != null) _batteryButton.interactable = interactable;
            if (_adButton != null)      _adButton.interactable      = interactable;
            if (_giveUpButton != null)  _giveUpButton.interactable  = interactable;
        }

        // ══════════════════════════════════════════════════════
        //  面板顯示/隱藏輔助
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 顯示或隱藏面板。
        /// 只控制 _panelRoot；腳本自身的 GameObject（RevivePanelManager）永遠不會被這裡關閉，
        /// 否則 OnDisable 會取消事件訂閱，導致之後的死亡事件永遠收不到。
        /// 若 _panelRoot 未設定，只輸出錯誤，不操作任何物件。
        /// </summary>
        private void SetPanelActive(bool active)
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(active);
                return;
            }

            // _panelRoot 未設定：絕對不能對 gameObject 呼叫 SetActive(false)，
            // 否則腳本會被 Unity 呼叫 OnDisable，事件訂閱丟失，整個復活系統失效。
            Core.DebugLogger.LogOnceError(
                "RevivePanel_SetActive_NoPanelRoot",
                "[RevivePanel] ★ 根因確認：_panelRoot（面板根物件）未設定！\n" +
                "  無法控制 UI 顯示/隱藏。腳本本身不會被關閉（保護事件訂閱）。\n" +
                "  修復：在 Inspector 將 RevivePanel(DefaultDisable) 拖入「面板根物件」欄位，\n" +
                "  然後確認 RevivePanel(DefaultDisable) 在場景中初始為 SetActive(false)。",
                Core.LogCategory.UI);
        }

        // ══════════════════════════════════════════════════════
        //  診斷（Debug 模式）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 一次性設定診斷：在 Awake 後以 LogOnce 印出所有關鍵參照狀態。
        /// 每個 key 整個 Play Session 只打一次，不會 spam。
        /// </summary>
        private void DiagnoseSetup()
        {
            // 診斷 1：腳本本身掛在哪？
            Core.DebugLogger.LogOnce(
                "RevivePanel_ScriptHost",
                $"[RevivePanel 診斷] 腳本掛載在：{gameObject.name}  active={gameObject.activeSelf}\n" +
                $"  → 此物件必須保持 active，否則事件永遠收不到。",
                Core.LogCategory.UI);

            // 診斷 2：panelRoot 是否設定？
            if (_panelRoot == null)
                Core.DebugLogger.LogOnce(
                    "RevivePanel_PanelRootMissing",
                    "[RevivePanel 診斷] ★ 根因候選：_panelRoot（面板根物件）未設定！\n" +
                    "  腳本會 fallback 控制自身 gameObject，但若腳本掛在 Manager 空物件上，UI 永遠不會出現。\n" +
                    "  修復：在 Inspector 將 RevivePanel(DefaultDisable) 拖入「面板根物件」欄位。",
                    Core.LogCategory.UI);
            else
                Core.DebugLogger.LogOnce(
                    "RevivePanel_PanelRoot",
                    $"[RevivePanel 診斷] _panelRoot = {_panelRoot.name}  active={_panelRoot.activeSelf}\n" +
                    $"  父物件：{(_panelRoot.transform.parent != null ? _panelRoot.transform.parent.name : "（無父物件，應在 Canvas 下）")}",
                    Core.LogCategory.UI);

            // 診斷 3：ReviveController 是否設定？
            if (_reviveController == null)
                Core.DebugLogger.LogOnce(
                    "RevivePanel_ControllerMissing",
                    "[RevivePanel 診斷] ★ 根因候選：_reviveController（復活控制器）未設定！\n" +
                    "  按鈕按下後無法呼叫復活邏輯。\n" +
                    "  修復：在 Inspector 將玩家身上的 ReviveController 拖入「復活控制器」欄位。",
                    Core.LogCategory.UI);
            else
                Core.DebugLogger.LogOnce(
                    "RevivePanel_Controller",
                    $"[RevivePanel 診斷] _reviveController = {_reviveController.gameObject.name}  OK",
                    Core.LogCategory.UI);

            // 診斷 4：三個按鈕
            string btnStatus =
                $"BatteryBtn={(_batteryButton != null ? _batteryButton.gameObject.name : "★ 未設定")}  " +
                $"AdBtn={(_adButton != null ? _adButton.gameObject.name : "★ 未設定")}  " +
                $"GiveUpBtn={(_giveUpButton != null ? _giveUpButton.gameObject.name : "★ 未設定")}";
            Core.DebugLogger.LogOnce("RevivePanel_Buttons", $"[RevivePanel 診斷] 按鈕參照：{btnStatus}", Core.LogCategory.UI);

            // 診斷 5：事件訂閱確認
            Core.DebugLogger.LogOnce(
                "RevivePanel_EventSub",
                "[RevivePanel 診斷] OnReviveOpportunity / OnPlayerRevived / OnGameOver 訂閱完成。\n" +
                "  若玩家死亡後沒有看到「OnReviveOpportunity 收到」的 log，表示事件根本沒有 Fire，\n" +
                "  請檢查 ReviveController 是否掛在玩家身上且 enabled。",
                Core.LogCategory.UI);
        }

        // ══════════════════════════════════════════════════════
        //  驗證
        // ══════════════════════════════════════════════════════

        private void ValidateButtons()
        {
            if (_batteryButton == null)
                Core.DebugLogger.LogError("RevivePanel：未指定電池復活按鈕（BatteryButton）！", Core.LogCategory.Player);
            if (_adButton == null)
                Core.DebugLogger.LogError("RevivePanel：未指定廣告復活按鈕（AdButton）！", Core.LogCategory.Player);
            if (_giveUpButton == null)
                Core.DebugLogger.LogError("RevivePanel：未指定放棄按鈕（GiveUpButton）！", Core.LogCategory.Player);
            if (_reviveController == null)
                Core.DebugLogger.LogError(
                    "RevivePanel：未指定 ReviveController！\n" +
                    "請在 Inspector 將玩家物件上的 ReviveController 拖入「復活控制器」欄位。",
                    Core.LogCategory.Player);
        }
    }
}
