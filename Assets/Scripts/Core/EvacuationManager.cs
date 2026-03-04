using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 撤離管理器：處理一般撤離（防守 60 秒）與緊急撤離（電池 6 直接撤）。
    /// 撤離期間怪物分波爆發，間隔隨時間加速。
    /// </summary>
    public class EvacuationManager : MonoBehaviour
    {
        private static EvacuationManager _instance;
        public static EvacuationManager Instance => _instance;

        // GameConfig 由 GameSceneInit 透過 Init() 注入，不需在 Inspector 顯示
        private Data.GameConfig _gameConfig;

        // ── 運行時 ──
        private bool _isEvacuating;
        private float _evacTimer;
        private float _reinforcementTimer;
        private float _currentReinforcementInterval;
        private float _reinforcementAcceleration = 0.9f; // 每波間隔乘此值加速
        private int _reinforcementWaveCount;

        private Data.StageData _stageData;
        private int _stageLevel;

        // ── 公開屬性 ──
        public bool IsEvacuating => _isEvacuating;
        public float EvacTimeRemaining => _evacTimer;
        public float EvacProgress => _gameConfig != null && _gameConfig.EvacDefendDuration > 0f
            ? 1f - (_evacTimer / _gameConfig.EvacDefendDuration)
            : 0f;

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
            GameEvents.OnEvacCalled += HandleEvacCalled;
        }

        private void OnDisable()
        {
            GameEvents.OnEvacCalled -= HandleEvacCalled;
        }

        /// <summary>
        /// 場景載入後初始化。
        /// </summary>
        public void Init(Data.StageData stageData, int stageLevel, Data.GameConfig config)
        {
            _stageData = stageData;
            _stageLevel = stageLevel;
            _gameConfig = config;
        }

        private void HandleEvacCalled()
        {
            if (_isEvacuating) return;

            _isEvacuating = true;
            _evacTimer = _gameConfig.EvacDefendDuration;
            _currentReinforcementInterval = _gameConfig.EvacReinforcementInterval;
            _reinforcementTimer = _currentReinforcementInterval;
            _reinforcementWaveCount = 0;

            // 生成撤離精英（固定數量，依 stage 調整）
            SpawnEvacElites();

            DebugLogger.Log($"撤離開始！防守 {_evacTimer} 秒。", LogCategory.Core);
        }

        private void Update()
        {
            if (!_isEvacuating) return;

            var state = GameLoopManager.Instance?.CurrentState ?? GameState.Evacuation;
            if (state == GameState.Paused) return;

            _evacTimer -= Time.deltaTime;

            // 增援計時
            _reinforcementTimer -= Time.deltaTime;
            if (_reinforcementTimer <= 0f)
            {
                SpawnReinforcements();
                _reinforcementWaveCount++;

                // 間隔加速
                _currentReinforcementInterval *= _reinforcementAcceleration;
                _currentReinforcementInterval = Mathf.Max(_currentReinforcementInterval, 1f); // 最短 1 秒
                _reinforcementTimer = _currentReinforcementInterval;
            }

            // 撤離成功
            if (_evacTimer <= 0f)
            {
                _isEvacuating = false;
                DebugLogger.Log("撤離防守完成！", LogCategory.Core);
                GameLoopManager.Instance?.EvacuationSuccess();
            }
        }

        /// <summary>
        /// 嘗試緊急撤離（檢查電池數量）。
        /// </summary>
        public bool TryEmergencyEvac(Player.PlayerStats playerStats)
        {
            if (playerStats == null)
            {
                DebugLogger.LogError("PlayerStats 為 null，無法執行緊急撤離！", LogCategory.Core);
                return false;
            }

            int required = _gameConfig != null ? _gameConfig.EmergencyEvacBatteryCost : 6;
            if (playerStats.Battery < required)
            {
                DebugLogger.Log($"電池不足！需要 {required}，目前 {playerStats.Battery}", LogCategory.Core);
                return false;
            }

            DebugLogger.Log("緊急撤離條件滿足！", LogCategory.Core);
            GameLoopManager.Instance?.EmergencyEvacuation();
            return true;
        }

        private void SpawnEvacElites()
        {
            var spawner = SpawnManager.Instance;
            if (spawner == null || _stageData == null) return;

            bool isRage = WaveManager.Instance != null && WaveManager.Instance.IsRageWave;
            int eliteCount = _gameConfig.GetEliteCount(_stageLevel, isRage);

            if (_stageData.EliteEnemies.Count > 0)
            {
                spawner.SpawnElites(_stageData.EliteEnemies, eliteCount, _stageLevel);
                DebugLogger.Log($"撤離精英已生成：{eliteCount} 隻", LogCategory.Core);
            }
        }

        private void SpawnReinforcements()
        {
            var spawner = SpawnManager.Instance;
            if (spawner == null || _stageData == null) return;

            // 增援數量隨波次增加
            int baseCount = _stageData.BaseEnemiesPerWave;
            int count = baseCount + _reinforcementWaveCount * 2;

            spawner.SpawnNormalEnemies(_stageData.NormalEnemies, count, _stageLevel);
            DebugLogger.Log($"撤離增援第 {_reinforcementWaveCount + 1} 波：{count} 隻", LogCategory.Core);
        }
    }
}
