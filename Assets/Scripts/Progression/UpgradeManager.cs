using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Progression
{
    /// <summary>
    /// 升級管理器：搜索到升級時跳出 3 選 1 卡片。
    /// 無經驗值系統，僅由搜索觸發。
    /// 僅升級目前持有武器的相關能力。
    /// </summary>
    public class UpgradeManager : MonoBehaviour
    {
        [TitleGroup("資料參照")]
        [Tooltip("升級池 ScriptableObject，包含所有可用的升級定義。")]
        [LabelText("升級池")]
        [Required("必須指定升級池，否則無法抽取升級！")]
        [SerializeField] private Data.UpgradePool _upgradePool;

        [TitleGroup("資料參照")]
        [Tooltip("稀有度機率表，決定抽到各稀有度的機率。")]
        [LabelText("稀有度表")]
        [Required("必須指定稀有度表，否則無法決定升級稀有度！")]
        [SerializeField] private Data.RarityTable _rarityTable;

        // ── 運行時 ──
        private Data.UpgradeEntry[] _currentChoices = new Data.UpgradeEntry[3];
        private Core.Rarity _currentRarity;
        private bool _isWaitingForChoice;

        private Player.PlayerStats _playerStats;
        private Combat.WeaponController _weaponController;

        public bool IsWaitingForChoice => _isWaitingForChoice;
        public Data.UpgradeEntry[] CurrentChoices => _currentChoices;
        public Core.Rarity CurrentRarity => _currentRarity;

        private void OnEnable()
        {
            Core.GameEvents.OnUpgradeReady += HandleUpgradeReady;
        }

        private void OnDisable()
        {
            Core.GameEvents.OnUpgradeReady -= HandleUpgradeReady;
        }

        /// <summary>
        /// 外部注入玩家參照（場景載入後呼叫）。
        /// </summary>
        public void Init(Player.PlayerStats stats, Combat.WeaponController weapon)
        {
            _playerStats = stats;
            _weaponController = weapon;
        }

        private void HandleUpgradeReady()
        {
            if (_isWaitingForChoice)
            {
                Core.DebugLogger.LogWarning("已經有一組升級選擇在等待中，跳過新的升級觸發。", Core.LogCategory.Progression);
                return;
            }

            if (_upgradePool == null)
            {
                Core.DebugLogger.LogError("UpgradePool 未指定！請在 UpgradeManager 上設定。", Core.LogCategory.Progression);
                return;
            }

            // 決定稀有度
            _currentRarity = _rarityTable != null ? _rarityTable.Roll() : Core.Rarity.Common;

            // 從池中抽取 3 張
            var choices = _upgradePool.GetRandomUpgrades(_currentRarity, 3);
            if (choices == null || choices.Count == 0)
            {
                Core.DebugLogger.LogError($"稀有度 {_currentRarity} 的升級池為空！", Core.LogCategory.Progression);
                return;
            }

            // 填入選項陣列
            for (int i = 0; i < 3; i++)
            {
                _currentChoices[i] = i < choices.Count ? choices[i] : null;
            }

            _isWaitingForChoice = true;
            Time.timeScale = 0f; // 暫停遊戲讓玩家選擇

            Core.DebugLogger.Log(
                $"升級選擇已產生！稀有度={_currentRarity}，選項：{FormatChoiceName(0)}, {FormatChoiceName(1)}, {FormatChoiceName(2)}",
                Core.LogCategory.Progression);

            // UI 監聽此事件來顯示選擇介面
            // （UI 層後續實作）
        }

        /// <summary>
        /// 玩家選擇後呼叫（由 UI 層呼叫）。index = 0, 1, 2。
        /// </summary>
        public void Choose(int index)
        {
            if (!_isWaitingForChoice)
            {
                Core.DebugLogger.LogError("目前沒有等待中的升級選擇！", Core.LogCategory.Progression);
                return;
            }

            if (index < 0 || index >= 3 || _currentChoices[index] == null)
            {
                Core.DebugLogger.LogError($"升級選擇 index={index} 無效！", Core.LogCategory.Progression);
                return;
            }

            var chosen = _currentChoices[index];
            ApplyUpgrade(chosen);

            _isWaitingForChoice = false;
            Time.timeScale = 1f; // 恢復遊戲

            var eventData = new Core.UpgradeChoiceData
            {
                ChosenIndex = index,
                Rarity = _currentRarity,
                UpgradeId = chosen.displayName
            };
            Core.GameEvents.FireUpgradeChosen(eventData);

            Core.DebugLogger.Log($"玩家選擇了升級：{chosen.displayName}（{chosen.rarity}）", Core.LogCategory.Progression);
        }

        private void ApplyUpgrade(Data.UpgradeEntry upgrade)
        {
            if (_weaponController == null)
            {
                Core.DebugLogger.LogError("WeaponController 未設定，無法套用升級！", Core.LogCategory.Progression);
                return;
            }

            float value = upgrade.value;

            switch (upgrade.statType)
            {
                case Data.UpgradeStatType.Damage:
                    _weaponController.AddBonusDamagePercent(value);
                    break;
                case Data.UpgradeStatType.FireRate:
                    _weaponController.AddBonusFireRatePercent(value);
                    break;
                case Data.UpgradeStatType.AttackRange:
                    _weaponController.AddBonusRangePercent(value);
                    break;
                case Data.UpgradeStatType.ProjectileSpeed:
                    _weaponController.AddBonusProjectileSpeedPercent(value);
                    break;
                case Data.UpgradeStatType.PelletCount:
                    _weaponController.AddBonusPelletCount((int)value);
                    break;
                case Data.UpgradeStatType.SpreadReduction:
                    // 未來擴充：減少散射角
                    break;
            }

            _playerStats?.UpgradeWeapon();
        }

        private string FormatChoiceName(int index)
        {
            return _currentChoices[index] != null ? _currentChoices[index].displayName : "(空)";
        }
    }
}
