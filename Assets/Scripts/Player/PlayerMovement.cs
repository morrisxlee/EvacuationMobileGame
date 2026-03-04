using UnityEngine;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 玩家移動控制器：2D Top-down 移動。
    /// 支援虛擬搖桿輸入（由 VirtualJoystick 提供方向向量）。
    /// 移速受 PlayerStats.MoveSpeed 控制。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("參照")]
        [SerializeField] private PlayerStats _playerStats;

        private Rigidbody2D _rb;
        private Vector2 _inputDirection;

        /// <summary>
        /// 外部（VirtualJoystick 或 InputSystem）設定移動方向。
        /// </summary>
        public Vector2 InputDirection
        {
            get => _inputDirection;
            set => _inputDirection = value;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();

            // ── 根因 1：isKinematic = true ──
            // Kinematic 模式下物理引擎不產生碰撞阻擋，Collision Matrix 完全無效 → 玩家穿牆
            if (_rb.isKinematic)
            {
                Core.DebugLogger.LogError(
                    "[PlayerMovement] 根因：Rigidbody2D.isKinematic = true！\n" +
                    "Kinematic 模式下物理碰撞阻擋無效，Player 會穿過所有牆壁。\n" +
                    "已自動關閉 isKinematic，但請同步修改 Prefab Inspector 避免下次重現：\n" +
                    "  Player Prefab → Rigidbody2D → Is Kinematic = 不勾選",
                    Core.LogCategory.Player);
                _rb.isKinematic = false;
            }

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // ── 根因 2：Collider2D 是 Trigger ──
            // isTrigger = true 的 Collider 只觸發 OnTriggerEnter，不產生物理碰撞阻擋 → 穿牆
            var col = GetComponent<Collider2D>();
            if (col != null && col.isTrigger)
            {
                Core.DebugLogger.LogError(
                    $"[PlayerMovement] 根因：Player 的 {col.GetType().Name} isTrigger = true！\n" +
                    "Trigger Collider 不阻擋任何物體，Player 會穿透所有牆壁。\n" +
                    "請在 Inspector → Player → Collider2D → Is Trigger = 不勾選。",
                    Core.LogCategory.Player);
            }

            // ── 根因 3：Player 與 Default (牆壁) Layer 碰撞被關閉 ──
            int playerLayer  = gameObject.layer;
            int defaultLayer = LayerMask.NameToLayer("Default");
            if (defaultLayer >= 0 && Physics2D.GetIgnoreLayerCollision(playerLayer, defaultLayer))
            {
                Core.DebugLogger.LogError(
                    $"[PlayerMovement] 根因：Player Layer（{LayerMask.LayerToName(playerLayer)}）" +
                    $"與 Default Layer 的碰撞在 Physics 2D Layer Collision Matrix 中被停用！\n" +
                    "路徑：Edit → Project Settings → Physics 2D → Layer Collision Matrix\n" +
                    $"  找到 {LayerMask.LayerToName(playerLayer)} 那一列 → Default 欄 = 勾選（啟用）",
                    Core.LogCategory.Player);
            }
            else if (defaultLayer < 0)
            {
                Core.DebugLogger.LogError(
                    "[PlayerMovement] 場景中找不到名為 'Default' 的 Layer！\n" +
                    "牆壁若不在 Default Layer 上，請確認 Collision Matrix 中 Player 與牆壁 Layer 的碰撞已啟用。",
                    Core.LogCategory.Player);
            }

            if (_playerStats == null)
                _playerStats = GetComponent<PlayerStats>();
        }

        private void FixedUpdate()
        {
            if (_playerStats != null && _playerStats.IsDead)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (state == Core.GameState.Paused || state == Core.GameState.Menu || state == Core.GameState.Result)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            float speed = _playerStats != null ? _playerStats.MoveSpeed : 5f;
            Vector2 dir = _inputDirection.normalized;
            _rb.velocity = dir * speed;
        }
    }
}
