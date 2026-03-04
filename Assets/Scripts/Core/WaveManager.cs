using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 波次管理器：控制波次節奏、血月等級、狂暴波觸發。
    /// 首波等待 40 秒（受難度係數修正），之後每波清完 → 血月 +1 → 下一波。
    /// ⚠ 波次時間設定請至 Project 視窗選取 GameConfig.asset → Inspector 的「波次設定」群組。
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        // ── Inspector：設定說明 ──
        [TitleGroup("設定說明")]
        [InfoBox("波次時間設定（首波延遲 / 波次間隔）在 GameConfig.asset 中。\n" +
                 "路徑：Project 視窗 → GameConfig.asset → Inspector → 波次設定\n" +
                 "敵人逐隻生成間隔：SpawnManager → 生成間隔（秒/隻）")]
        [SerializeField, HideLabel]
        private string _configNote = "";   // 純佔位符，讓 InfoBox 正常顯示

        // ── Inspector：執行時監測（唯讀）──
        [TitleGroup("執行時狀態（唯讀）")]
        [ShowInInspector, ReadOnly, LabelText("目前波次")]
        private int DebugCurrentWave => _currentWaveIndex + 1;

        [TitleGroup("執行時狀態（唯讀）")]
        [ShowInInspector, ReadOnly, LabelText("血月等級")]
        private int DebugBloodMoon => _bloodMoonLevel;

        [TitleGroup("執行時狀態（唯讀）")]
        [ShowInInspector, ReadOnly, LabelText("等待下一波")]
        private bool DebugIsWaiting => _isWaitingForNextWave;

        [TitleGroup("執行時狀態（唯讀）")]
        [ShowInInspector, ReadOnly, LabelText("倒計時（秒）")]
        private float DebugTimer => Mathf.CeilToInt(_waveTimer);

        [TitleGroup("執行時狀態（唯讀）")]
        [ShowInInspector, ReadOnly, LabelText("剩餘敵人")]
        private int DebugEnemiesRemaining => _enemiesRemainingInWave;

        private static WaveManager _instance;
        public static WaveManager Instance => _instance;

        // GameConfig 由 GameSceneInit 透過 Init() 注入，不需在 Inspector 顯示
        private Data.GameConfig _gameConfig;

        // ── 運行時 ──
        private int _currentWaveIndex;
        private int _bloodMoonLevel;
        private bool _isWaveActive;
        private bool _isWaitingForNextWave;
        private float _waveTimer;
        private int _enemiesRemainingInWave;
        private bool _isInitialized;

        private Data.StageData _stageData;
        private int _stageLevel;

        // ── 公開屬性 ──
        public int CurrentWaveIndex => _currentWaveIndex;
        public int BloodMoonLevel => _bloodMoonLevel;
        public bool IsWaveActive => _isWaveActive;
        public bool IsWaitingForNextWave => _isWaitingForNextWave;
        /// <summary>
        /// 距下一波開始的倒計時秒數（等待階段時有效；波次進行中為 0）。
        /// </summary>
        public float WaveTimer => _waveTimer;
        /// <summary>
        /// 本波剩餘待擊殺敵人數量（波次進行中有效；等待階段可能為 0）。
        /// </summary>
        public int EnemiesRemainingInWave => _enemiesRemainingInWave;
        public bool IsRageWave => _bloodMoonLevel > 0 && _bloodMoonLevel % (_gameConfig != null ? _gameConfig.RageWaveThreshold : 5) == 0;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
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
            GameEvents.OnEnemyKilled += HandleEnemyKilled;
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnEnemyKilled -= HandleEnemyKilled;
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        }

        /// <summary>
        /// 場景載入後初始化。
        /// </summary>
        public void Init(Data.StageData stageData, int stageLevel, Data.GameConfig config)
        {
            _stageData = stageData;
            _stageLevel = stageLevel;
            _gameConfig = config;

            _currentWaveIndex = 0;
            _bloodMoonLevel = 0;
            _isWaveActive = false;
            _isWaitingForNextWave = true;
            _isInitialized = true;

            // 首波等待時間
            float delay = _gameConfig.FirstWaveDelay * _stageData.FirstWaveDelayMultiplier;
            _waveTimer = delay;

            DebugLogger.Log($"WaveManager 初始化完成。首波延遲={delay}秒", LogCategory.Spawn);
        }

        private void Update()
        {
            if (!_isInitialized) return;

            var state = GameLoopManager.Instance?.CurrentState ?? GameState.Playing;
            if (state != GameState.Playing && state != GameState.Evacuation) return;

            if (_isWaitingForNextWave)
            {
                _waveTimer -= Time.deltaTime;
                if (_waveTimer <= 0f)
                {
                    StartNextWave();
                }
            }
        }

        private void StartNextWave()
        {
            _isWaitingForNextWave = false;
            _isWaveActive = true;

            bool isRage = IsRageWave;
            int enemyCount = _stageData.GetEnemyCountForWave(_currentWaveIndex);

            // 狂暴波怪物數量翻倍
            if (isRage)
            {
                enemyCount *= 2;
                DebugLogger.Log($"★ 狂暴波！怪物數量翻倍至 {enemyCount}", LogCategory.Spawn);
                GameEvents.FireRageWaveTriggered();
            }

            // 生成普通怪
            var spawner = SpawnManager.Instance;
            if (spawner != null)
            {
                int spawned = spawner.SpawnNormalEnemies(_stageData.NormalEnemies, enemyCount, _stageLevel);
                _enemiesRemainingInWave = spawned;

                // 精英怪
                int eliteCount = _gameConfig.GetEliteCount(_stageLevel, isRage);
                if (eliteCount > 0 && _stageData.EliteEnemies.Count > 0)
                {
                    int eliteSpawned = spawner.SpawnElites(_stageData.EliteEnemies, eliteCount, _stageLevel);
                    _enemiesRemainingInWave += eliteSpawned;
                }
            }
            else
            {
                DebugLogger.LogError("SpawnManager.Instance 為 null！無法生怪。", LogCategory.Spawn);
            }

            GameEvents.FireWaveStarted(_currentWaveIndex);
            DebugLogger.Log($"第 {_currentWaveIndex + 1} 波開始！血月等級={_bloodMoonLevel}，敵人={_enemiesRemainingInWave}", LogCategory.Spawn);
        }

        private void HandleEnemyKilled(int enemyId)
        {
            if (!_isWaveActive) return;

            _enemiesRemainingInWave--;
            if (_enemiesRemainingInWave <= 0)
            {
                WaveCleared();
            }
        }

        private void WaveCleared()
        {
            _isWaveActive = false;
            GameEvents.FireWaveCleared(_currentWaveIndex);
            DebugLogger.Log($"第 {_currentWaveIndex + 1} 波清除完畢！", LogCategory.Spawn);

            // 血月等級 +1
            _bloodMoonLevel++;
            GameEvents.FireBloodMoonLevelUp(_bloodMoonLevel);
            DebugLogger.Log($"血月等級提升至 {_bloodMoonLevel}", LogCategory.Spawn);

            // 準備下一波
            _currentWaveIndex++;
            _isWaitingForNextWave = true;
            _waveTimer = _gameConfig.WaveInterval;
        }

        private void HandleGameStateChanged(GameState newState)
        {
            // 撤離階段時停止正常波次
            if (newState == GameState.Evacuation)
            {
                _isWaitingForNextWave = false;
                DebugLogger.Log("進入撤離階段，波次管理暫停。", LogCategory.Spawn);
            }
        }
    }
}
