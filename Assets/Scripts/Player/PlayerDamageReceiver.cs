using UnityEngine;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 玩家受傷接收器：實作 IDamageable，將傷害轉發給 PlayerStats。
    /// 掛在玩家物件上，讓敵人投射物可以透過 IDamageable 對玩家造傷。
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerDamageReceiver : MonoBehaviour, Combat.IDamageable
    {
        [SerializeField] private PlayerStats _playerStats;
        private Feedback.FeedbackBridge _feedbackBridge;

        public bool IsAlive => _playerStats != null && !_playerStats.IsDead;

        private void Awake()
        {
            if (_playerStats == null)
                _playerStats = GetComponent<PlayerStats>();
            _feedbackBridge = GetComponent<Feedback.FeedbackBridge>();
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive) return;

            if (_playerStats == null)
            {
                Core.DebugLogger.LogError("PlayerDamageReceiver 缺少 PlayerStats！", Core.LogCategory.Player);
                return;
            }

            _playerStats.TakeDamage(damage);
            _feedbackBridge?.PlayHit();

            // 若此次傷害導致玩家死亡，額外觸發死亡 MMF 回饋（PlayHit 已先行，兩者均播放）
            // PlayerStats.Die() 只 fire GameEvent，不持有 FeedbackBridge 引用，故在此補觸發
            if (!IsAlive)
                _feedbackBridge?.PlayDeath();
        }
    }
}
