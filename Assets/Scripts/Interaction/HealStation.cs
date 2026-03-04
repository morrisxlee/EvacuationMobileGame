using UnityEngine;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 補血站：場景補給點，可配置使用次數與冷卻。
    /// 玩家靠近後自動補血。
    /// </summary>
    public class HealStation : MonoBehaviour
    {
        [Header("設定")]
        [Tooltip("每次補血量")]
        [SerializeField] private float _healAmount = 30f;
        [Tooltip("可使用次數（-1 = 無限）")]
        [SerializeField] private int _maxUses = 3;
        [Tooltip("使用後冷卻時間（秒）")]
        [SerializeField] private float _cooldown = 30f;

        [Header("視覺")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Color _readyColor = Color.green;
        [SerializeField] private Color _cooldownColor = Color.yellow;
        [SerializeField] private Color _depletedColor = Color.gray;

        private int _usesRemaining;
        private float _cooldownTimer;
        private bool _isReady = true;

        public bool IsReady => _isReady && !IsDepeleted;
        public bool IsDepeleted => _maxUses >= 0 && _usesRemaining <= 0;
        public float CooldownProgress => _cooldown > 0f ? _cooldownTimer / _cooldown : 0f;

        private void Start()
        {
            _usesRemaining = _maxUses;
            UpdateVisual();
        }

        private void Update()
        {
            if (_isReady || IsDepeleted) return;

            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer <= 0f)
            {
                _isReady = true;
                UpdateVisual();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsReady) return;

            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null && !stats.IsDead && stats.CurrentHP < stats.MaxHP)
            {
                UseStation(stats);
            }
        }

        private void UseStation(Player.PlayerStats stats)
        {
            stats.Heal(_healAmount);

            if (_maxUses >= 0)
            {
                _usesRemaining--;
            }

            _isReady = false;
            _cooldownTimer = _cooldown;

            Core.DebugLogger.Log($"補血站使用！回復 {_healAmount} HP，剩餘次數={(_maxUses < 0 ? "∞" : _usesRemaining.ToString())}", Core.LogCategory.Interaction);
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (_spriteRenderer == null) return;

            if (IsDepeleted)
                _spriteRenderer.color = _depletedColor;
            else if (!_isReady)
                _spriteRenderer.color = _cooldownColor;
            else
                _spriteRenderer.color = _readyColor;
        }
    }
}
