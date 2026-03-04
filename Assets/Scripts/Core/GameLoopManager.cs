using UnityEngine;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 遊戲主迴圈管理器：控制 Menu→Playing→Evacuation→Result 狀態轉換。
    /// 全域單例，跨場景存活。
    /// </summary>
    public class GameLoopManager : MonoBehaviour
    {
        private static GameLoopManager _instance;
        public static GameLoopManager Instance => _instance;

        [TitleGroup("場景設定")]
        [Tooltip("主選單場景的名稱。")]
        [LabelText("選單場景")]
        [SerializeField] private string _menuSceneName = "MenuScene";

        [TitleGroup("場景設定")]
        [Tooltip("遊戲主場景的名稱。")]
        [LabelText("遊戲場景")]
        [SerializeField] private string _gameSceneName = "GameScene";

        [TitleGroup("全域設定")]
        [Tooltip("全域遊戲設定 ScriptableObject，包含波次、復活、撤離等參數。")]
        [LabelText("遊戲設定")]
        [Required("必須指定遊戲設定！")]
        [SerializeField] private Data.GameConfig _gameConfig;

        private GameState _currentState = GameState.Menu;
        private GameState _stateBeforePause;
        public GameState CurrentState => _currentState;
        public Data.GameConfig Config => _gameConfig;

        // ── 當局遊戲資料 ──
        private int _currentStage;
        private Data.WeaponData _selectedWeapon;
        private Data.StageData _selectedStageData;

        public int CurrentStage => _currentStage;
        public Data.WeaponData SelectedWeapon => _selectedWeapon;
        public Data.StageData SelectedStageData => _selectedStageData;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 只銷毀這個「多餘的 GameLoopManager 元件」，而非整個 gameObject。
                // 這樣同一 GameObject 上的 MenuController 等其他元件就不會被誤殺，
                // 能正常執行 Awake / Start 並接收 Start 按鈕的 onClick。
                //
                // ⚠ 架構建議：GameLoopManager 最好掛在一個「只有 GameLoopManager」的獨立空物件上，
                //   不要與 MenuController 共用同一個 GameObject，可避免生命週期互相干擾。
                DebugLogger.Log(
                    "[GameLoopManager] 偵測到複製品，僅 Destroy 此元件（保留同 GO 上的其他元件）。",
                    LogCategory.Core);
                Destroy(this);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ══════════════════════════════════════
        //  狀態轉換
        // ══════════════════════════════════════

        /// <summary>
        /// Menu 中選擇 Stage 與武器後呼叫此方法開始遊戲。
        /// </summary>
        public void StartGame(Data.StageData stageData, Data.WeaponData weapon, int stage)
        {
            if (_currentState != GameState.Menu)
            {
                DebugLogger.LogError("StartGame 只能在 Menu 狀態呼叫！", LogCategory.Core);
                return;
            }

            _selectedStageData = stageData;
            _selectedWeapon = weapon;
            _currentStage = stage;

            DebugLogger.Log($"開始遊戲：Stage={stageData.DisplayName}, Weapon={weapon.DisplayName}, StageLevel={stage}", LogCategory.Core);

            ChangeState(GameState.Playing);
            SceneManager.LoadScene(_gameSceneName);
        }

        /// <summary>
        /// 進入撤離階段。
        /// </summary>
        public void EnterEvacuation()
        {
            if (_currentState != GameState.Playing)
            {
                DebugLogger.LogError("EnterEvacuation 只能在 Playing 狀態呼叫！", LogCategory.Core);
                return;
            }

            DebugLogger.Log("進入撤離階段！", LogCategory.Core);
            ChangeState(GameState.Evacuation);
            GameEvents.FireEvacCalled();
        }

        /// <summary>
        /// 撤離成功。
        /// </summary>
        public void EvacuationSuccess()
        {
            if (_currentState != GameState.Evacuation)
            {
                DebugLogger.LogError("EvacuationSuccess 只能在 Evacuation 狀態呼叫！", LogCategory.Core);
                return;
            }

            DebugLogger.Log("撤離成功！", LogCategory.Core);
            ChangeState(GameState.Result);
            GameEvents.FireEvacCompleted();
            GameEvents.FireGameResult(ResultType.EvacSuccess);
        }

        /// <summary>
        /// 緊急撤離（電池滿 6 直接撤）。
        /// </summary>
        public void EmergencyEvacuation()
        {
            if (_currentState != GameState.Playing && _currentState != GameState.Evacuation)
            {
                DebugLogger.LogError("EmergencyEvacuation 只能在 Playing 或 Evacuation 狀態呼叫！", LogCategory.Core);
                return;
            }

            DebugLogger.Log("緊急撤離！", LogCategory.Core);
            ChangeState(GameState.Result);
            GameEvents.FireEmergencyEvac();
            GameEvents.FireGameResult(ResultType.EmergencyEvac);
        }

        /// <summary>
        /// 玩家死亡進入復活等待狀態：凍結時間，等待玩家在 RevivePanel 做選擇。
        /// </summary>
        public void EnterReviving()
        {
            if (_currentState != GameState.Playing && _currentState != GameState.Evacuation)
            {
                DebugLogger.LogError("EnterReviving 只能在 Playing 或 Evacuation 狀態呼叫！", LogCategory.Core);
                return;
            }

            DebugLogger.Log("進入復活等待狀態，遊戲凍結。", LogCategory.Core);
            Time.timeScale = 0f;
            ChangeState(GameState.Reviving);
        }

        /// <summary>
        /// 玩家選擇復活後離開復活等待狀態，恢復時間。
        /// </summary>
        public void ExitReviving()
        {
            if (_currentState != GameState.Reviving)
            {
                DebugLogger.LogError("ExitReviving 只能在 Reviving 狀態呼叫！", LogCategory.Core);
                return;
            }

            DebugLogger.Log("離開復活等待狀態，遊戲恢復。", LogCategory.Core);
            Time.timeScale = 1f;
            ChangeState(GameState.Playing);
        }

        /// <summary>
        /// 玩家死亡（Game Over）。
        /// </summary>
        public void GameOver()
        {
            // 若是從 Reviving 狀態轉入，需先恢復時間
            if (_currentState == GameState.Reviving)
                Time.timeScale = 1f;

            DebugLogger.Log("遊戲結束（死亡）。", LogCategory.Core);
            ChangeState(GameState.Result);
            GameEvents.FireGameOver();
            GameEvents.FireGameResult(ResultType.GameOver);
        }

        /// <summary>
        /// 回到主選單。
        /// 執行順序：恢復時間 → 回收所有物件池 → 靜默設定狀態 → 載入場景。
        ///
        /// 注意：這裡刻意不呼叫 ChangeState()（不觸發 FireGameStateChanged 事件），
        /// 原因是此時 GameScene 的物件仍然存活，若在這裡廣播事件，
        /// 場景中的訂閱者可能會做出非預期的反應。
        /// 事件改由 MenuScene 載入完成後，MenuController 呼叫 OnMenuLoaded() 時再廣播。
        /// </summary>
        public void ReturnToMenu()
        {
            // 確保時間不凍結（從 Reviving / Paused 狀態返回時 timeScale 可能仍為 0）
            Time.timeScale = 1f;

            // 回收所有 DontDestroyOnLoad 物件池中的活躍物件（敵人、子彈、掉落物等），
            // 避免它們在主選單場景中繼續顯示
            Pooling.GenericPool.Instance?.DespawnAllPools();

            // 直接寫入狀態欄位，不透過 ChangeState()，避免在 GameScene 卸載前廣播事件
            var prevState = _currentState;
            _currentState = GameState.Menu;
            DebugLogger.Log($"狀態設為 Menu（靜默）：{prevState} → Menu", LogCategory.Core);

            SceneManager.LoadScene(_menuSceneName);
        }

        /// <summary>
        /// 由 MenuController.Start() 在 MenuScene 完整載入後呼叫。
        /// 確認狀態為 Menu，並廣播 OnGameStateChanged 事件。
        /// 這樣可以保證事件只送達 MenuScene 的訂閱者，而非 GameScene 的舊訂閱者。
        /// </summary>
        public void OnMenuLoaded()
        {
            if (_currentState != GameState.Menu)
            {
                DebugLogger.LogError(
                    $"[GameLoopManager] OnMenuLoaded 時狀態異常（{_currentState}），預期為 Menu。強制重置。\n" +
                    $"  根因：ReturnToMenu() 之後有其他程式碼修改了狀態，請檢查 GameScene 卸載流程。",
                    LogCategory.Core);
                _currentState = GameState.Menu;
            }

            GameEvents.FireGameStateChanged(GameState.Menu);
            DebugLogger.Log("[GameLoopManager] OnMenuLoaded 完成，Menu 狀態事件已廣播。", LogCategory.Core);
        }

        /// <summary>
        /// 暫停遊戲。
        /// </summary>
        public void PauseGame()
        {
            if (_currentState == GameState.Playing || _currentState == GameState.Evacuation)
            {
                _stateBeforePause = _currentState;
                Time.timeScale = 0f;
                ChangeState(GameState.Paused);
                GameEvents.FireGamePause();
            }
        }

        /// <summary>
        /// 繼續遊戲。
        /// </summary>
        public void ResumeGame()
        {
            if (_currentState == GameState.Paused)
            {
                Time.timeScale = 1f;
                ChangeState(_stateBeforePause);
                GameEvents.FireGameResume();
            }
        }

        private void ChangeState(GameState newState)
        {
            var oldState = _currentState;
            _currentState = newState;
            DebugLogger.Log($"狀態轉換：{oldState} → {newState}", LogCategory.Core);
            GameEvents.FireGameStateChanged(newState);
        }
    }
}
