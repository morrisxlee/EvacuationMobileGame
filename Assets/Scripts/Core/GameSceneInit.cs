using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 遊戲場景初始化器：GameScene 載入時自動執行，
    /// 負責將各子系統串接起來（注入參照、初始化管理器）。
    /// 掛在 GameSystems 根物件上，管理器元件也掛在同物件。
    /// </summary>
    public class GameSceneInit : MonoBehaviour
    {
        [TitleGroup("玩家參照")]
        [Tooltip("拖入場景中的 Player 物件。腳本會自動取得 PlayerStats、PlayerMovement、WeaponController。")]
        [LabelText("玩家物件")]
        [Required("必須指定玩家物件！")]
        [SerializeField] private GameObject _player;

        // ── 自動取得的參照（不顯示在 Inspector）──
        private Transform _playerTransform;
        private Player.PlayerStats _playerStats;
        private Player.PlayerMovement _playerMovement;
        private Combat.WeaponController _weaponController;

        // ── 同物件上的管理器（自動取得）──
        private SpawnManager _spawnManager;
        private WaveManager _waveManager;
        private EvacuationManager _evacuationManager;
        private AI.EnemyTickManager _enemyTickManager;
        private Progression.UpgradeManager _upgradeManager;

        private void Awake()
        {
            // ── 從 Player 物件取得子元件 ──
            if (_player != null)
            {
                _playerTransform = _player.transform;
                _playerStats = _player.GetComponent<Player.PlayerStats>();
                _playerMovement = _player.GetComponent<Player.PlayerMovement>();
                _weaponController = _player.GetComponent<Combat.WeaponController>();

                if (_playerStats == null)
                    DebugLogger.LogError("Player 物件上找不到 PlayerStats！", LogCategory.Core);
                if (_playerMovement == null)
                    DebugLogger.LogError("Player 物件上找不到 PlayerMovement！", LogCategory.Core);
                if (_weaponController == null)
                    DebugLogger.LogError("Player 物件上找不到 WeaponController！", LogCategory.Core);
            }
            else
            {
                DebugLogger.LogError("Player 物件未指定！請在 GameSceneInit 上拖入玩家。", LogCategory.Core);
            }

            // ── 從同物件取得管理器 ──
            _spawnManager = GetComponent<SpawnManager>();
            _waveManager = GetComponent<WaveManager>();
            _evacuationManager = GetComponent<EvacuationManager>();
            _enemyTickManager = GetComponent<AI.EnemyTickManager>();
            _upgradeManager = GetComponent<Progression.UpgradeManager>();

            if (_spawnManager == null)
                DebugLogger.LogError("同物件上找不到 SpawnManager！請確保掛在同一個 GameObject。", LogCategory.Core);
            if (_waveManager == null)
                DebugLogger.LogError("同物件上找不到 WaveManager！請確保掛在同一個 GameObject。", LogCategory.Core);
            if (_evacuationManager == null)
                DebugLogger.LogError("同物件上找不到 EvacuationManager！請確保掛在同一個 GameObject。", LogCategory.Core);
            if (_enemyTickManager == null)
                DebugLogger.LogError("同物件上找不到 EnemyTickManager！請確保掛在同一個 GameObject。", LogCategory.Core);
            if (_upgradeManager == null)
                DebugLogger.LogError("同物件上找不到 UpgradeManager！請確保掛在同一個 GameObject。", LogCategory.Core);
        }

        private void Start()
        {
            var loop = GameLoopManager.Instance;
            if (loop == null)
            {
                DebugLogger.LogError("GameLoopManager.Instance 為 null！請確保 GameLoopManager 存在於 DontDestroyOnLoad 場景中。", LogCategory.Core);
                return;
            }

            var config = loop.Config;
            var stageData = loop.SelectedStageData;
            int stageLevel = loop.CurrentStage;

            if (config == null)
            {
                DebugLogger.LogError("GameConfig 為 null！請在 GameLoopManager 上指定。", LogCategory.Core);
                return;
            }

            if (stageData == null)
            {
                DebugLogger.LogError("SelectedStageData 為 null！請確保從 Menu 正確傳入。", LogCategory.Core);
                return;
            }

            // ── 初始化各管理器 ──
            _enemyTickManager?.SetPlayerTransform(_playerTransform);
            _spawnManager?.Init(_playerTransform, config);
            _waveManager?.Init(stageData, stageLevel, config);
            _evacuationManager?.Init(stageData, stageLevel, config);
            _upgradeManager?.Init(_playerStats, _weaponController);

            // ── 廣播遊戲開始 ──
            GameEvents.FireGameStart();
            DebugLogger.Log("GameScene 初始化完成！遊戲開始。", LogCategory.Core);
        }
    }
}
