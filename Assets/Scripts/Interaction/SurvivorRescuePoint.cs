using UnityEngine;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 倖存者救援點：玩家站定讀條，完成後給「倖存者貨幣」（僅局外用途）。
    /// </summary>
    public class SurvivorRescuePoint : MonoBehaviour
    {
        [Header("設定")]
        [Tooltip("救援讀條時間（秒）")]
        [SerializeField] private float _rescueDuration = 5f;
        [Tooltip("完成後給予的倖存者貨幣")]
        [SerializeField] private int _rewardAmount = 1;

        [Header("視覺")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Color _pendingColor = Color.cyan;
        [SerializeField] private Color _rescuedColor = Color.green;

        private bool _isRescued;
        private bool _isRescuing;
        private float _rescueTimer;
        private bool _playerInRange;
        private Player.PlayerStats _playerStats;
        private int _survivorId;

        public bool IsRescued => _isRescued;
        public float RescueProgress => _rescueDuration > 0f ? _rescueTimer / _rescueDuration : 0f;

        private void Start()
        {
            _survivorId = gameObject.GetInstanceID();
            UpdateVisual();
        }

        private void Update()
        {
            if (_isRescued || !_playerInRange || _playerStats == null || _playerStats.IsDead)
            {
                if (_isRescuing) CancelRescue();
                return;
            }

            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (state != Core.GameState.Playing && state != Core.GameState.Evacuation)
            {
                if (_isRescuing) CancelRescue();
                return;
            }

            if (!_isRescuing)
            {
                _isRescuing = true;
                _rescueTimer = 0f;
            }

            float speedMul = _playerStats.InteractionSpeedMultiplier;
            _rescueTimer += Time.deltaTime * speedMul;

            if (_rescueTimer >= _rescueDuration)
            {
                CompleteRescue();
            }
        }

        private void CompleteRescue()
        {
            _isRescued = true;
            _isRescuing = false;

            _playerStats?.AddSurvivorCurrency(_rewardAmount);
            Core.GameEvents.FireSurvivorRescued(_survivorId);
            Core.DebugLogger.Log($"倖存者救援完成！獲得 {_rewardAmount} 倖存者貨幣。", Core.LogCategory.Interaction);

            UpdateVisual();
        }

        private void CancelRescue()
        {
            _isRescuing = false;
            _rescueTimer = 0f;
        }

        private void UpdateVisual()
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.color = _isRescued ? _rescuedColor : _pendingColor;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isRescued) return;
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null)
            {
                _playerInRange = true;
                _playerStats = stats;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null && stats == _playerStats)
            {
                _playerInRange = false;
                _playerStats = null;
                CancelRescue();
            }
        }
    }
}
