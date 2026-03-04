using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 玩家數值系統：HP、移速、互動速度、金錢、電池、倖存者貨幣。
    /// 所有數值變動都會透過 GameEvents 廣播。
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [TitleGroup("基礎數值")]
        [Tooltip("玩家的最大生命值。")]
        [LabelText("最大 HP")]
        [Min(1f)]
        [SerializeField] private float _maxHP = 100f;

        [TitleGroup("基礎數值")]
        [Tooltip("玩家的移動速度（世界單位/秒）。")]
        [LabelText("移動速度")]
        [Min(0.1f)]
        [SerializeField] private float _moveSpeed = 5f;

        [TitleGroup("基礎數值")]
        [Tooltip("互動讀條速度倍率。1.0 為標準速度，2.0 為兩倍速。")]
        [LabelText("互動速度倍率")]
        [Min(0.1f)]
        [SerializeField] private float _interactionSpeedMultiplier = 1f;

        // 復活次數由 runtime 管理，不顯示在 Inspector
        private int _reviveCount;
        private int _maxRevives;
        private int _firstReviveBatteryCost;

        // ── 運行時數值 ──
        private float _currentHP;
        private int _currency;
        private int _battery;
        private int _survivorCurrency;
        private int _keys;
        private bool _isDead;

        // ── 武器等級 ──
        private int _weaponLevel;

        // ── 公開屬性 ──
        public float MaxHP => _maxHP;
        public float CurrentHP => _currentHP;
        public float MoveSpeed => _moveSpeed;
        public float InteractionSpeedMultiplier => _interactionSpeedMultiplier;
        public int Currency => _currency;
        public int Battery => _battery;
        public int SurvivorCurrency => _survivorCurrency;
        public int Keys => _keys;
        public bool IsDead => _isDead;
        public int WeaponLevel => _weaponLevel;
        public int ReviveCount => _reviveCount;

        private void Start()
        {
            var config = Core.GameLoopManager.Instance?.Config;
            if (config != null)
            {
                _maxRevives = config.MaxRevives;
                _firstReviveBatteryCost = config.FirstReviveBatteryCost;
            }
            else
            {
                Core.DebugLogger.LogError("GameLoopManager.Instance 或 Config 為 null！PlayerStats 無法初始化復活設定。", Core.LogCategory.Player);
                _maxRevives = 3;
                _firstReviveBatteryCost = 1;
            }

            ResetStats();
        }

        /// <summary>
        /// 重置所有數值到初始狀態（開局 / 復活時呼叫）。
        /// </summary>
        public void ResetStats()
        {
            _currentHP = _maxHP;
            _currency = 0;
            _battery = 0;
            _survivorCurrency = 0;
            _keys = 0;
            _weaponLevel = 0;
            _reviveCount = 0;
            _isDead = false;

            Core.GameEvents.FirePlayerHealthChanged(_currentHP);
            Core.GameEvents.FireCurrencyChanged(_currency);
            Core.GameEvents.FireBatteryChanged(_battery);
            Core.GameEvents.FireSurvivorCurrencyChanged(_survivorCurrency);
        }

        // ══════════════════════════════════════
        //  HP
        // ══════════════════════════════════════

        public void TakeDamage(float damage)
        {
            if (_isDead) return;
            _currentHP = Mathf.Max(0f, _currentHP - damage);
            Core.GameEvents.FirePlayerHealthChanged(_currentHP);
            Core.DebugLogger.Log($"玩家受傷 {damage}，剩餘 HP: {_currentHP}", Core.LogCategory.Player);

            if (_currentHP <= 0f)
            {
                Die();
            }
        }

        public void Heal(float amount)
        {
            if (_isDead) return;
            _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
            Core.GameEvents.FirePlayerHealthChanged(_currentHP);
            Core.DebugLogger.Log($"玩家回血 {amount}，目前 HP: {_currentHP}", Core.LogCategory.Player);
        }

        private void Die()
        {
            _isDead = true;
            Core.DebugLogger.Log("玩家死亡！", Core.LogCategory.Player);
            Core.GameEvents.FirePlayerDied();
        }

        /// <summary>
        /// 嘗試復活。第 1 次消耗電池，第 2~3 次需看廣告（由外部處理）。
        /// 回傳 true 表示復活成功。
        /// </summary>
        public bool TryRevive(bool adWatched = false)
        {
            if (!_isDead) return false;
            if (_reviveCount >= _maxRevives)
            {
                Core.DebugLogger.Log("已達最大復活次數！", Core.LogCategory.Player);
                return false;
            }

            if (adWatched)
            {
                // 廣告復活：任何次數皆可，不扣電池
            }
            else if (_reviveCount == 0)
            {
                // 首次且未看廣告：必須消耗電池
                if (_battery < _firstReviveBatteryCost)
                {
                    Core.DebugLogger.Log($"電池不足（需要 {_firstReviveBatteryCost}，目前 {_battery}），無法復活！", Core.LogCategory.Player);
                    return false;
                }
                AddBattery(-_firstReviveBatteryCost);
            }
            else
            {
                // 第 2~3 次且未看廣告：拒絕
                Core.DebugLogger.Log("需要觀看廣告才能復活！", Core.LogCategory.Player);
                return false;
            }

            _reviveCount++;
            _isDead = false;
            _currentHP = _maxHP; // 復活回滿血
            Core.GameEvents.FirePlayerHealthChanged(_currentHP);
            Core.GameEvents.FirePlayerRevived();
            Core.DebugLogger.Log($"復活成功！第 {_reviveCount} 次，HP: {_currentHP}", Core.LogCategory.Player);
            return true;
        }

        // ══════════════════════════════════════
        //  貨幣
        // ══════════════════════════════════════

        public void AddCurrency(int amount)
        {
            _currency += amount;
            if (_currency < 0) _currency = 0;
            Core.GameEvents.FireCurrencyChanged(_currency);
        }

        public bool SpendCurrency(int amount)
        {
            if (_currency < amount)
            {
                Core.DebugLogger.Log($"金錢不足！需要 {amount}，目前 {_currency}", Core.LogCategory.Player);
                return false;
            }
            _currency -= amount;
            Core.GameEvents.FireCurrencyChanged(_currency);
            return true;
        }

        // ══════════════════════════════════════
        //  電池
        // ══════════════════════════════════════

        public void AddBattery(int amount)
        {
            _battery += amount;
            if (_battery < 0) _battery = 0;
            Core.GameEvents.FireBatteryChanged(_battery);
        }

        // ══════════════════════════════════════
        //  鑰匙
        // ══════════════════════════════════════

        public void AddKeys(int amount)
        {
            _keys += amount;
            if (_keys < 0) _keys = 0;
        }

        public bool SpendKey()
        {
            if (_keys <= 0)
            {
                Core.DebugLogger.Log("鑰匙不足！", Core.LogCategory.Player);
                return false;
            }
            _keys--;
            return true;
        }

        // ══════════════════════════════════════
        //  倖存者貨幣
        // ══════════════════════════════════════

        public void AddSurvivorCurrency(int amount)
        {
            _survivorCurrency += amount;
            Core.GameEvents.FireSurvivorCurrencyChanged(_survivorCurrency);
        }

        // ══════════════════════════════════════
        //  武器等級
        // ══════════════════════════════════════

        public void UpgradeWeapon()
        {
            _weaponLevel++;
            Core.DebugLogger.Log($"武器升級到 Lv.{_weaponLevel}", Core.LogCategory.Player);
        }

        // ══════════════════════════════════════
        //  Buff 修改器（升級卡用）
        // ══════════════════════════════════════

        public void ModifyMaxHP(float amount, bool isPercentage)
        {
            if (isPercentage)
                _maxHP *= (1f + amount);
            else
                _maxHP += amount;

            _currentHP = Mathf.Min(_currentHP, _maxHP);
            Core.GameEvents.FirePlayerHealthChanged(_currentHP);
        }

        public void ModifyMoveSpeed(float amount, bool isPercentage)
        {
            if (isPercentage)
                _moveSpeed *= (1f + amount);
            else
                _moveSpeed += amount;
        }

        public void ModifyInteractionSpeed(float amount, bool isPercentage)
        {
            if (isPercentage)
                _interactionSpeedMultiplier *= (1f + amount);
            else
                _interactionSpeedMultiplier += amount;
        }
    }
}
