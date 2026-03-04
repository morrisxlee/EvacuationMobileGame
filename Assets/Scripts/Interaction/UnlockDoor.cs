using UnityEngine;
using TMPro;
using Pathfinding;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 解鎖區域門：兩種類型。
    /// Currency 類：玩家走入觸發區且金錢足夠時自動扣款解鎖。
    /// Key 類：玩家走入觸發區且鑰匙足夠時自動消耗解鎖。
    /// 解鎖後阻擋碰撞器禁用、世界 UI 隱藏、A* 圖更新。
    /// </summary>
    public class UnlockDoor : MonoBehaviour
    {
        public enum UnlockType { Currency, Key }

        // ══════════════════════════════════════
        //  解鎖設定
        // ══════════════════════════════════════

        [TitleGroup("解鎖設定")]
        [Tooltip("解鎖類型：Currency = 消耗金錢，Key = 消耗鑰匙。切換後 Inspector 會自動顯示對應欄位。")]
        [LabelText("解鎖類型")]
        [SerializeField] private UnlockType _unlockType = UnlockType.Currency;

        [TitleGroup("解鎖設定")]
        [Tooltip("Currency 類型時所需的金錢數量。玩家進入觸發區且金錢不足時會輸出提示日誌。")]
        [LabelText("所需金錢")]
        [Min(1)]
        [ShowIf("@_unlockType == UnlockType.Currency")]
        [SerializeField] private int _currencyCost = 100;

        [TitleGroup("解鎖設定")]
        [Tooltip("Key 類型時所需的鑰匙數量。玩家進入觸發區且鑰匙不足時會輸出提示日誌。")]
        [LabelText("所需鑰匙數")]
        [Min(1)]
        [ShowIf("@_unlockType == UnlockType.Key")]
        [SerializeField] private int _keyCost = 1;

        // ══════════════════════════════════════
        //  碰撞設定
        // ══════════════════════════════════════

        [TitleGroup("碰撞設定")]
        [Tooltip("解鎖後自動禁用的阻擋碰撞器（非 Trigger 的實體碰撞器）。解鎖前阻擋玩家與敵人通行。")]
        [LabelText("阻擋碰撞器")]
        [SerializeField] private Collider2D _blockingCollider;

        // ══════════════════════════════════════
        //  視覺效果
        // ══════════════════════════════════════

        [TitleGroup("視覺效果")]
        [Tooltip("門的 SpriteRenderer，用於顯示鎖定/解鎖狀態顏色變化。")]
        [LabelText("Sprite 渲染器")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [TitleGroup("視覺效果")]
        [Tooltip("鎖定時門的顏色疊加（建議用紅色或暗色表示不可通行）。")]
        [LabelText("鎖定顏色")]
        [SerializeField] private Color _lockedColor = Color.red;

        [TitleGroup("視覺效果")]
        [Tooltip("解鎖後門的顏色疊加（建議用半透明綠色表示已開放）。")]
        [LabelText("解鎖顏色")]
        [SerializeField] private Color _unlockedColor = new Color(0f, 1f, 0f, 0.3f);

        // ══════════════════════════════════════
        //  世界提示 UI
        // ══════════════════════════════════════

        [TitleGroup("世界提示 UI")]
        [Tooltip("顯示所需費用數字的 TextMeshPro（World Space，不要放在 Canvas 內）。僅 Currency 類型顯示，解鎖後自動隱藏。")]
        [LabelText("費用文字 (TMP)")]
        [ShowIf("@_unlockType == UnlockType.Currency")]
        [SerializeField] private TextMeshPro _costText;

        [TitleGroup("世界提示 UI")]
        [Tooltip("貨幣圖示的 SpriteRenderer（World Space）。僅 Currency 類型顯示，解鎖後自動隱藏。")]
        [LabelText("貨幣圖示")]
        [ShowIf("@_unlockType == UnlockType.Currency")]
        [SerializeField] private SpriteRenderer _currencyIcon;

        [TitleGroup("世界提示 UI")]
        [Tooltip("鑰匙圖示的 SpriteRenderer（World Space）。僅 Key 類型顯示，解鎖後自動隱藏。")]
        [LabelText("鑰匙圖示")]
        [ShowIf("@_unlockType == UnlockType.Key")]
        [SerializeField] private SpriteRenderer _keyIcon;

        // ══════════════════════════════════════
        //  運行時私有欄位
        // ══════════════════════════════════════

        private bool _isUnlocked;
        private bool _playerInRange;
        private Player.PlayerStats _playerStats;

        // ══════════════════════════════════════
        //  公開屬性
        // ══════════════════════════════════════

        public bool IsUnlocked => _isUnlocked;
        public UnlockType Type => _unlockType;
        public int CurrencyCost => _currencyCost;
        public int KeyCost => _keyCost;

        // ══════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════

        private void Start()
        {
            RefreshWorldUI();
            UpdateVisual();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isUnlocked) return;
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats == null) return;

            _playerInRange = true;
            _playerStats = stats;
            TryUnlock();
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_isUnlocked || !_playerInRange) return;
            TryUnlock();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null && stats == _playerStats)
            {
                _playerInRange = false;
                _playerStats = null;
            }
        }

        // ══════════════════════════════════════
        //  解鎖邏輯
        // ══════════════════════════════════════

        private void TryUnlock()
        {
            if (_isUnlocked || _playerStats == null) return;

            bool success = false;

            switch (_unlockType)
            {
                case UnlockType.Currency:
                    if (_playerStats.Currency >= _currencyCost)
                        success = _playerStats.SpendCurrency(_currencyCost);
                    else
                        Core.DebugLogger.Log($"金錢不足！需要 {_currencyCost}，目前 {_playerStats.Currency}", Core.LogCategory.Interaction);
                    break;

                case UnlockType.Key:
                    if (_playerStats.Keys >= _keyCost)
                    {
                        success = true;
                        for (int i = 0; i < _keyCost; i++)
                        {
                            if (!_playerStats.SpendKey()) { success = false; break; }
                        }
                    }
                    else
                        Core.DebugLogger.Log($"鑰匙不足！需要 {_keyCost}，目前 {_playerStats.Keys}", Core.LogCategory.Interaction);
                    break;
            }

            if (success)
                Unlock();
        }

        private void Unlock()
        {
            _isUnlocked = true;

            if (_blockingCollider != null)
                _blockingCollider.enabled = false;

            HideWorldUI();
            UpdateVisual();
            UpdateAstarGraph();
            Core.DebugLogger.Log($"區域解鎖成功！類型={_unlockType}", Core.LogCategory.Interaction);
        }

        // ══════════════════════════════════════
        //  世界 UI
        // ══════════════════════════════════════

        private void RefreshWorldUI()
        {
            bool isCurrency = _unlockType == UnlockType.Currency;

            if (_costText != null)
            {
                _costText.gameObject.SetActive(isCurrency);
                _costText.text = _currencyCost.ToString();
            }

            if (_currencyIcon != null)
                _currencyIcon.gameObject.SetActive(isCurrency);

            if (_keyIcon != null)
                _keyIcon.gameObject.SetActive(!isCurrency);
        }

        private void HideWorldUI()
        {
            if (_costText != null)     _costText.gameObject.SetActive(false);
            if (_currencyIcon != null) _currencyIcon.gameObject.SetActive(false);
            if (_keyIcon != null)      _keyIcon.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════
        //  視覺
        // ══════════════════════════════════════

        private void UpdateVisual()
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.color = _isUnlocked ? _unlockedColor : _lockedColor;
        }

        // ══════════════════════════════════════
        //  A* 圖更新
        // ══════════════════════════════════════

        private void UpdateAstarGraph()
        {
            if (AstarPath.active == null) return;
            AstarPath.active.UpdateGraphs(new Bounds(transform.position, Vector3.one * 3f));
            Core.DebugLogger.Log("A* 圖已更新（區域解鎖）。", Core.LogCategory.Interaction);
        }
    }
}
