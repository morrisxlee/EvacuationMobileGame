using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 撤離區域觸發器：玩家進入後可啟動一般撤離。
    /// 撤離區域需先解鎖（例如透過 UnlockDoor）才能使用。
    /// </summary>
    public class EvacZone : MonoBehaviour
    {
        [TitleGroup("撤離設定")]
        [Tooltip("撤離區是否已解鎖。可在運行時由 UnlockDoor 或其他系統呼叫 Unlock() 解鎖。")]
        [LabelText("已解鎖")]
        [SerializeField] private bool _isUnlocked;

        [TitleGroup("視覺效果")]
        [Tooltip("撤離區的 SpriteRenderer，用於顯示狀態變化。")]
        [LabelText("Sprite 渲染器")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [TitleGroup("視覺效果")]
        [Tooltip("撤離區鎖定時的顏色。")]
        [LabelText("鎖定顏色")]
        [SerializeField] private Color _lockedColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        [TitleGroup("視覺效果")]
        [Tooltip("撤離區解鎖後的顏色。")]
        [LabelText("解鎖顏色")]
        [SerializeField] private Color _unlockedColor = new Color(0f, 1f, 0f, 0.3f);

        [TitleGroup("視覺效果")]
        [Tooltip("玩家站在撤離區內時的顏色。")]
        [LabelText("啟動顏色")]
        [SerializeField] private Color _activeColor = new Color(1f, 1f, 0f, 0.5f);

        private bool _playerInZone;

        public bool IsUnlocked => _isUnlocked;
        public bool PlayerInZone => _playerInZone;

        private void Start()
        {
            UpdateVisual();
        }

        /// <summary>
        /// 外部呼叫解鎖此撤離區。
        /// </summary>
        public void Unlock()
        {
            _isUnlocked = true;
            UpdateVisual();
            Core.DebugLogger.Log("撤離區域已解鎖！", Core.LogCategory.Interaction);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isUnlocked) return;
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null)
            {
                _playerInZone = true;
                UpdateVisual();

                // 如果遊戲處於 Playing 狀態，啟動撤離
                var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
                if (state == Core.GameState.Playing)
                {
                    Core.GameLoopManager.Instance?.EnterEvacuation();
                }
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null)
            {
                _playerInZone = false;
                UpdateVisual();
            }
        }

        private void UpdateVisual()
        {
            if (_spriteRenderer == null) return;

            if (!_isUnlocked)
                _spriteRenderer.color = _lockedColor;
            else if (_playerInZone)
                _spriteRenderer.color = _activeColor;
            else
                _spriteRenderer.color = _unlockedColor;
        }
    }
}
