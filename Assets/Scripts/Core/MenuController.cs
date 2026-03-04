using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 主選單控制器：選擇 Stage 與起始武器後進場。
    /// Stage 為無限成長模式（非固定關卡數）。
    /// Demo 版本步槍/霰彈都可直接選。
    /// UI 層後續擴充，此腳本提供邏輯骨架。
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        [TitleGroup("可選武器（Demo 版全部解鎖）")]
        [Tooltip("玩家在選單中可以選擇的武器清單。")]
        [SerializeField] private List<Data.WeaponData> _availableWeapons = new();

        [TitleGroup("Stage 設定")]
        [Tooltip("預設的關卡資料。")]
        [SerializeField] private Data.StageData _defaultStageData;

        [TitleGroup("全域設定")]
        [Tooltip("如果場景中還沒有 GameLoopManager，會嘗試載入此設定。")]
        [SerializeField] private Data.GameConfig _gameConfig;

        [TitleGroup("除錯測試 (無 UI 用)")]
        [Tooltip("勾選後，進入 MenuScene 會在 1 秒後自動開始遊戲。方便目前沒有 UI 時進行測試。")]
        [SerializeField] private bool _autoStartForDebug = false;

        [TitleGroup("除錯測試 (無 UI 用)")]
        [Tooltip("開啟後：\n" +
                 "① 場景載入時輸出一次性結構診斷（EventSystem 數量、DontDestroyOnLoad Canvas、按鈕狀態）\n" +
                 "② 每次滑鼠左鍵點擊輸出 RaycastAll 結果，直接告訴你點擊被哪個物件攔截\n" +
                 "確認根因後關閉以避免 Update 開銷。")]
        [SerializeField] private bool _debugMenu = true;

        // ── 單例（用於清理舊的 DontDestroyOnLoad 殘留實例）──
        // GameLoopManager 是 DontDestroyOnLoad，若 MenuController 與它同 GameObject，
        // MenuController 也會隨之跨場景存活。每次 MenuScene 重新載入時，
        // 新建立的這份 MenuController 會在 Awake 中把舊的殘留實例清掉，確保只有一份在運行。
        private static MenuController _menuInstance;

        // ── 運行時 ──
        private int _selectedWeaponIndex;
        private int _selectedStageLevel = 1;

        // ── 公開屬性 ──
        public IReadOnlyList<Data.WeaponData> AvailableWeapons => _availableWeapons;
        public int SelectedWeaponIndex => _selectedWeaponIndex;
        public int SelectedStageLevel => _selectedStageLevel;

        private void Awake()
        {
            // 若已有一個舊的 MenuController（跟著 DontDestroyOnLoad GO 存活下來的），
            // 把它的元件銷毀，讓這個新場景本地的實例成為唯一一份。
            if (_menuInstance != null && _menuInstance != this)
            {
                DebugLogger.Log(
                    "[MenuController] 偵測到舊實例（DontDestroyOnLoad 殘留），Destroy 舊元件，讓新實例接管。",
                    LogCategory.Core);
                Destroy(_menuInstance);
            }
            _menuInstance = this;
        }

        private void OnDestroy()
        {
            if (_menuInstance == this)
                _menuInstance = null;
        }

        private void Start()
        {
            // 每次進入選單都清除 LogOnce 紀錄，確保第二局起的診斷訊息不被靜默抑制
            DebugLogger.ClearLogOnceKeys();

            Time.timeScale = 1f;

            if (GameLoopManager.Instance == null)
            {
                DebugLogger.LogError("GameLoopManager 不存在！請確保場景中有 GameLoopManager prefab。", LogCategory.Core);
                return;
            }

            // OnMenuLoaded()：
            //   1. 驗證狀態確實為 Menu（若不是則強制重置並記錄錯誤，協助排查根因）
            //   2. 在 MenuScene 已完整載入後廣播 OnGameStateChanged(Menu) 事件，
            //      確保事件只送達 MenuScene 的訂閱者，而非先前 GameScene 的殘留訂閱者
            GameLoopManager.Instance.OnMenuLoaded();

            if (_debugMenu)
                DiagnoseMenuOnLoad();

            if (_autoStartForDebug)
            {
                DebugLogger.Log("AutoStartForDebug 已開啟，將於 1 秒後自動開始遊戲...", LogCategory.Core);
                Invoke(nameof(StartGame), 1f);
            }
        }

        private void Update()
        {
            // 每次滑鼠左鍵按下時輸出 RaycastAll 結果，判斷點擊是否被攔截。
            // GetMouseButtonDown 每幀最多觸發一次，不會 spam。
            if (_debugMenu && Input.GetMouseButtonDown(0))
                DiagnoseClickInput();
        }

        // ══════════════════════════════════════════════════════
        //  診斷方法（_debugMenu 開啟時使用）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 場景載入時執行一次的結構性診斷。
        /// 檢查項目：EventSystem 數量與來源、DontDestroyOnLoad Canvas、
        /// GameLoopManager 狀態、場景中所有 Button 的健康狀態。
        /// </summary>
        private void DiagnoseMenuOnLoad()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[MenuDebug] ════ 選單載入診斷 ════");

            // ── 0. MenuController 實例健康檢查 ──
            var allMCs = FindObjectsOfType<MenuController>(true);
            sb.AppendLine($"  MenuController 實例數: {allMCs.Length} {(allMCs.Length == 1 ? "✓" : "★ 超過 1 個！舊 DontDestroyOnLoad 殘留未清理")}");
            foreach (var mc in allMCs)
                sb.AppendLine($"    [{mc.gameObject.scene.name}] {mc.gameObject.name}  active={mc.isActiveAndEnabled}  isThis={mc == this}");

            // ── 1. GameLoopManager 狀態 ──
            var loop = GameLoopManager.Instance;
            sb.AppendLine(loop != null
                ? $"  GameLoopManager 狀態: {loop.CurrentState} {(loop.CurrentState == GameState.Menu ? "✓" : "★ 不是 Menu！StartGame() 會被 guard 擋掉")}"
                : "  GameLoopManager: ★ null（嚴重）");

            // ── 2. Time.timeScale ──
            sb.AppendLine($"  Time.timeScale: {Time.timeScale} {(Mathf.Approximately(Time.timeScale, 1f) ? "✓" : "★ 不是 1！")}");

            // ── 3. EventSystem 數量（超過 1 個 = UI 輸入失效的典型根因）──
            var allES = FindObjectsOfType<EventSystem>(true);
            sb.AppendLine($"  EventSystem 數量: {allES.Length} {(allES.Length == 1 ? "✓" : allES.Length == 0 ? "★ 完全沒有 EventSystem，UI 點擊完全失效！" : "★ 超過 1 個！這是 UI 輸入失效的典型根因")}");
            foreach (var es in allES)
            {
                bool isDDOL = es.gameObject.scene.name == "DontDestroyOnLoad";
                sb.AppendLine($"    [{es.gameObject.scene.name}] {es.gameObject.name}  " +
                              $"active={es.isActiveAndEnabled}  DDOL={isDDOL}  " +
                              $"module={es.currentInputModule?.GetType().Name ?? "null"}");
            }

            // ── 4. DontDestroyOnLoad Canvas（可能用高 SortOrder 蓋住選單）──
            var allCanvases = FindObjectsOfType<Canvas>(true);
            bool hasDDOLCanvas = false;
            foreach (var c in allCanvases)
            {
                if (c.gameObject.scene.name != "DontDestroyOnLoad") continue;
                if (!hasDDOLCanvas)
                {
                    sb.AppendLine("  ★ 發現 DontDestroyOnLoad Canvas（可能遮擋選單 UI 並攔截點擊）：");
                    hasDDOLCanvas = true;
                }
                sb.AppendLine($"    {c.gameObject.name}  active={c.isActiveAndEnabled}  " +
                              $"sortOrder={c.sortingOrder}  renderMode={c.renderMode}");
            }
            if (!hasDDOLCanvas)
                sb.AppendLine("  DontDestroyOnLoad Canvas: 無 ✓");

            // ── 5. 場景中所有 Button 狀態 ──
            var allButtons = FindObjectsOfType<Button>(true);
            sb.AppendLine($"  場景中 Button 數量: {allButtons.Length}");
            foreach (var btn in allButtons)
            {
                int listenerCount = btn.onClick.GetPersistentEventCount();
                bool isInteractable = btn.interactable;
                bool isActive = btn.isActiveAndEnabled;
                var cg = btn.GetComponentInParent<CanvasGroup>();
                bool cgBlocks = cg != null && (!cg.interactable || cg.alpha < 0.01f);
                string status = (!isActive || !isInteractable || cgBlocks)
                    ? "★ 無法點擊"
                    : listenerCount == 0 ? "★ onClick 無監聽器" : "✓";
                sb.AppendLine($"    {btn.gameObject.name}  active={isActive}  interactable={isInteractable}  " +
                              $"listeners={listenerCount}  CanvasGroupBlock={cgBlocks}  → {status}");
            }

            sb.AppendLine("[MenuDebug] ════════════════════════════");
            sb.AppendLine("  ↑ 看完以上再點擊開始，Console 會輸出 RaycastAll 結果");
            DebugLogger.Log(sb.ToString(), LogCategory.Core);
        }

        /// <summary>
        /// 每次滑鼠左鍵按下時執行。
        /// 透過 EventSystem.RaycastAll 列出點擊位置的完整 UI 堆疊，
        /// 第一筆（最頂層）就是實際攔截點擊的物件。
        /// 不 spam：GetMouseButtonDown 每次按下只觸發一次。
        /// </summary>
        private void DiagnoseClickInput()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[MenuDebug] ── 滑鼠點擊 @ {Input.mousePosition} ──");

            var es = EventSystem.current;
            if (es == null)
            {
                sb.AppendLine("  ★ 根因確認：EventSystem.current == null。沒有任何 EventSystem 在運行，UI 點擊完全無效。");
                sb.AppendLine("  修復：MenuScene 需要有一個 EventSystem GameObject（Add Component → Event System）。");
                DebugLogger.Log(sb.ToString(), LogCategory.Core);
                return;
            }

            sb.AppendLine($"  EventSystem: {es.gameObject.name}  active={es.isActiveAndEnabled}  scene={es.gameObject.scene.name}");

            // RaycastAll：列出游標下方所有 UI 物件（由前到後）
            var pointer = new PointerEventData(es) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            es.RaycastAll(pointer, results);

            if (results.Count == 0)
            {
                sb.AppendLine("  RaycastAll: 無命中（游標下方沒有任何 UI 可接收點擊）");
                sb.AppendLine("  可能原因：游標不在按鈕上、Canvas 的 GraphicRaycaster 被停用、或 Canvas 不在 Screen Space。");
            }
            else
            {
                sb.AppendLine($"  RaycastAll 命中 {results.Count} 個物件（索引 0 = 最頂層，會攔截點擊）：");
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    var btn = r.gameObject.GetComponentInParent<Button>();
                    string btnInfo = btn != null
                        ? $"  [Button interactable={btn.interactable} listeners={btn.onClick.GetPersistentEventCount()}]"
                        : "";
                    sb.AppendLine($"    [{i}] {r.gameObject.name}  (scene={r.gameObject.scene.name}){btnInfo}");
                }

                var top = results[0];
                bool topIsButton = top.gameObject.GetComponentInParent<Button>() != null;
                if (!topIsButton)
                    sb.AppendLine($"  ★ 根因候選：最頂層物件「{top.gameObject.name}」不是 Button，它攔截了所有點擊。");
                else
                    sb.AppendLine($"  ✓ 最頂層物件是 Button（{top.gameObject.name}）。若 StartGame 還是沒觸發，查看 Button.interactable 與 onClick 監聽器數量。");
            }

            DebugLogger.Log(sb.ToString(), LogCategory.Core);
        }

        /// <summary>
        /// 選擇武器（由 UI 按鈕呼叫）。
        /// </summary>
        public void SelectWeapon(int index)
        {
            if (index < 0 || index >= _availableWeapons.Count)
            {
                DebugLogger.LogError($"武器 index={index} 超出範圍！", LogCategory.Core);
                return;
            }
            _selectedWeaponIndex = index;
            DebugLogger.Log($"選擇武器：{_availableWeapons[index].DisplayName}", LogCategory.Core);
        }

        /// <summary>
        /// 設定 Stage 等級（由 UI 呼叫，無限模式下玩家可選起始等級）。
        /// </summary>
        public void SetStageLevel(int level)
        {
            _selectedStageLevel = Mathf.Max(1, level);
            DebugLogger.Log($"設定 Stage 等級：{_selectedStageLevel}", LogCategory.Core);
        }

        /// <summary>
        /// 開始遊戲（由 UI「開始」按鈕呼叫）。
        /// 若玩家未手動選擇武器，自動使用第一個（index 0）。
        /// </summary>
        [Button("開始遊戲 (Debug)", ButtonSizes.Large), GUIColor(0.4f, 0.8f, 1f)]
        public void StartGame()
        {
            // 這行是排查「按下開始沒反應」的第一個確認點：
            //   若按下後 Console 看不到這行 log → 點擊沒到達程式碼（EventSystem / 遮擋問題）
            //   若看到這行但遊戲沒開始   → 繼續往下看 GameLoopManager 狀態
            var currentState = GameLoopManager.Instance?.CurrentState;
            DebugLogger.Log(
                $"[MenuController] ★ StartGame 呼叫（點擊已到達程式碼）。GameLoopManager 狀態={currentState?.ToString() ?? "（Instance 為 null）"}",
                LogCategory.Core);

            if (_availableWeapons.Count == 0)
            {
                DebugLogger.LogError("沒有可選的武器！請在 MenuController Inspector → 可選武器 清單中加入至少一個 WeaponData。", LogCategory.Core);
                return;
            }

            if (_defaultStageData == null)
            {
                DebugLogger.LogError("StageData 未設定！請在 MenuController Inspector → Stage 設定 → 預設關卡資料 拖入 StageData 資產。", LogCategory.Core);
                return;
            }

            // 安全夾緊 index（確保 UI 沒選或 index 越界時仍回到 0）
            _selectedWeaponIndex = Mathf.Clamp(_selectedWeaponIndex, 0, _availableWeapons.Count - 1);
            var weapon = _availableWeapons[_selectedWeaponIndex];

            // 驗證 DisplayName 是否填寫（只是警告，不阻止遊戲）
            if (string.IsNullOrEmpty(weapon.DisplayName))
                DebugLogger.LogWarning(
                    $"WeaponData（index={_selectedWeaponIndex}）的 DisplayName 為空。\n" +
                    "請在 Project 視窗選取該 WeaponData 資產，並在 Inspector 填入 DisplayName。",
                    LogCategory.Core);

            if (string.IsNullOrEmpty(_defaultStageData.DisplayName))
                DebugLogger.LogWarning(
                    "StageData 的 DisplayName 為空。\n" +
                    "請在 Project 視窗選取 StageData 資產，並在 Inspector 填入 DisplayName。",
                    LogCategory.Core);

            DebugLogger.Log(
                $"開始遊戲（自動使用預設值）：武器={weapon.DisplayName}（index={_selectedWeaponIndex}）  Stage={_defaultStageData.DisplayName}  StageLevel={_selectedStageLevel}",
                LogCategory.Core);

            var loop = GameLoopManager.Instance;
            if (loop == null)
            {
                DebugLogger.LogError("GameLoopManager.Instance 為 null！", LogCategory.Core);
                return;
            }

            loop.StartGame(_defaultStageData, weapon, _selectedStageLevel);
        }
    }
}
