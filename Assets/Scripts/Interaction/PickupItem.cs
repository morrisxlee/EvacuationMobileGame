using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 地面掉落物：由 SearchPoint 透過 GenericPool.Spawn 生成，玩家走過觸發撿取。
    /// 實作 IPoolable，完整依賴物件池管理生命週期，無 Destroy/Instantiate。
    ///
    /// 效能設計（針對場景同時大量物件）：
    /// - 無 Coroutine：所有計時使用 float 減法，避免 GC Alloc。
    /// - Update 在未初始化時立即 return，無效物件幾乎零開銷。
    /// - Awake 快取所有 GetComponent，Update/OnTrigger 期間不呼叫。
    /// - OnTriggerEnter2D 為事件驅動，非 per-frame 輪詢。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PickupItem : MonoBehaviour, Pooling.IPoolable
    {
        [TitleGroup("計時設定")]
        [Tooltip("掉落物生成後的保護期（秒）。\n" +
                 "保護期內碰撞器禁用，防止玩家站在搜索點上時立即自動撿起。\n" +
                 "建議設 0.3~0.8 秒，視 PlaySpawn 動畫長度調整。")]
        [LabelText("保護期（秒）")]
        [Min(0f)]
        [SerializeField] private float _gracePeriod = 0.5f;

        [TitleGroup("計時設定")]
        [Tooltip("掉落物在地面存在的最長時間（秒）。\n" +
                 "超時後自動回收至物件池，避免長時間無人撿取佔用池資源。\n" +
                 "建議 20~60 秒，根據場景設計調整。")]
        [LabelText("最長存在時間（秒）")]
        [Min(1f)]
        [SerializeField] private float _lifeTime = 30f;

        // ── 元件快取（Awake 取得，避免 Update / Trigger 期間 GetComponent）──
        private Collider2D _collider;
        private Feedback.PickupFeedback _feedback;

        // ── 執行時狀態（無 [SerializeField]，由 Init() 注入）──
        private Core.SearchRewardType _rewardType;
        private int    _amount;
        private string _poolId;
        private float  _graceTimer;
        private float  _lifeTimer;
        private bool   _isCollectable;
        private bool   _isInitialized;

        // ══════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════

        private void Awake()
        {
            _collider = GetComponent<Collider2D>();
            _feedback = GetComponent<Feedback.PickupFeedback>();

            if (_collider == null)
                Core.DebugLogger.LogError(
                    $"[PickupItem] '{gameObject.name}' 找不到 Collider2D！\n" +
                    "請在此 Prefab 上加入 CircleCollider2D，並勾選 Is Trigger = true。",
                    Core.LogCategory.Interaction);

            if (_feedback == null)
                Core.DebugLogger.LogWarning(
                    $"[PickupItem] '{gameObject.name}' 找不到 PickupFeedback 元件！\n" +
                    "生成與撿取回饋將全部靜音。請在此 Prefab 上加入 PickupFeedback 元件。",
                    Core.LogCategory.Interaction);
        }

        // ══════════════════════════════════════
        //  IPoolable
        // ══════════════════════════════════════

        /// <summary>從池中取出時呼叫。重置狀態，等待 Init() 注入資料。</summary>
        public void OnSpawnFromPool()
        {
            _isInitialized = false;
            _isCollectable = false;
            if (_collider != null) _collider.enabled = false;
        }

        /// <summary>回收至池時呼叫。確保碰撞器關閉、狀態清空。</summary>
        public void OnReturnToPool()
        {
            _isInitialized = false;
            _isCollectable = false;
            if (_collider != null) _collider.enabled = false;
        }

        // ══════════════════════════════════════
        //  初始化
        // ══════════════════════════════════════

        /// <summary>
        /// 設定掉落物的獎勵類型、數量與歸屬池 ID。
        /// 必須在 GenericPool.Spawn() 之後立即呼叫，否則物件不會進入可撿取狀態。
        /// </summary>
        /// <param name="type">獎勵類型（Currency/Heal/Battery/Key/Upgrade）。</param>
        /// <param name="amount">獎勵數量；Upgrade 類型填 0 即可。</param>
        /// <param name="poolId">此物件所屬的 GenericPool 池 ID，回收時使用。</param>
        public void Init(Core.SearchRewardType type, int amount, string poolId)
        {
            if (string.IsNullOrEmpty(poolId))
            {
                Core.DebugLogger.LogError(
                    $"[PickupItem] '{gameObject.name}' Init() 收到空的 poolId！\n" +
                    "請在對應的 SearchLootTable LootEntry 中填寫 Pickup Pool ID。\n" +
                    "此掉落物將無法正確回收至物件池。",
                    Core.LogCategory.Interaction);
            }

            _rewardType    = type;
            _amount        = amount;
            _poolId        = poolId;
            _graceTimer    = _gracePeriod;
            _lifeTimer     = _lifeTime;
            _isInitialized = true;
            _isCollectable = false;

            if (_collider != null) _collider.enabled = false;

            _feedback?.PlaySpawn();

            Core.DebugLogger.Log(
                $"[PickupItem] 初始化完成：類型={type}, 數量={amount}, 池ID={poolId}",
                Core.LogCategory.Interaction);
        }

        // ══════════════════════════════════════
        //  Update：保護期 + 生命期計時
        // ══════════════════════════════════════

        private void Update()
        {
            // 未初始化（池中閒置）時跳過，幾乎零開銷
            if (!_isInitialized) return;

            float dt = Time.deltaTime;

            // 保護期倒計時 → 到期啟用碰撞器進入可撿取狀態
            if (!_isCollectable)
            {
                _graceTimer -= dt;
                if (_graceTimer <= 0f)
                {
                    _isCollectable = true;
                    if (_collider != null) _collider.enabled = true;
                }
            }

            // 生命期倒計時 → 超時自動回收
            _lifeTimer -= dt;
            if (_lifeTimer <= 0f)
            {
                Core.DebugLogger.Log(
                    $"[PickupItem] '{gameObject.name}' 存在超時，自動回收。",
                    Core.LogCategory.Interaction);
                ReturnToPool();
            }
        }

        // ══════════════════════════════════════
        //  撿取觸發
        // ══════════════════════════════════════

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isCollectable) return;

            var playerStats = other.GetComponent<Player.PlayerStats>();
            if (playerStats == null || playerStats.IsDead) return;

            ApplyEffect(playerStats);
            _feedback?.PlayCollected();
            ReturnToPool();
        }

        // ══════════════════════════════════════
        //  效果應用
        // ══════════════════════════════════════

        private void ApplyEffect(Player.PlayerStats stats)
        {
            switch (_rewardType)
            {
                case Core.SearchRewardType.Currency:
                    stats.AddCurrency(_amount);
                    Core.DebugLogger.Log($"[PickupItem] 撿到金幣 ×{_amount}", Core.LogCategory.Interaction);
                    break;

                case Core.SearchRewardType.Heal:
                    stats.Heal(_amount);
                    Core.DebugLogger.Log($"[PickupItem] 撿到補血包 ×{_amount}", Core.LogCategory.Interaction);
                    break;

                case Core.SearchRewardType.Battery:
                    stats.AddBattery(_amount);
                    Core.DebugLogger.Log($"[PickupItem] 撿到電池 ×{_amount}", Core.LogCategory.Interaction);
                    break;

                case Core.SearchRewardType.Key:
                    stats.AddKeys(_amount);
                    Core.DebugLogger.Log($"[PickupItem] 撿到鑰匙 ×{_amount}", Core.LogCategory.Interaction);
                    break;

                case Core.SearchRewardType.Upgrade:
                    Core.GameEvents.FireUpgradeReady();
                    Core.DebugLogger.Log("[PickupItem] 撿到升級道具，觸發升級選擇！", Core.LogCategory.Interaction);
                    break;

                default:
                    Core.DebugLogger.LogError(
                        $"[PickupItem] '{gameObject.name}' 收到未處理的 SearchRewardType: {_rewardType}\n" +
                        "請在 PickupItem.ApplyEffect() 新增對應的 case 處理。",
                        Core.LogCategory.Interaction);
                    break;
            }
        }

        // ══════════════════════════════════════
        //  回收至物件池
        // ══════════════════════════════════════

        private void ReturnToPool()
        {
            _isInitialized = false;

            if (string.IsNullOrEmpty(_poolId))
            {
                Core.DebugLogger.LogError(
                    $"[PickupItem] '{gameObject.name}' 嘗試回收但 _poolId 為空！\n" +
                    "物件已強制 SetActive(false)，但不在池中，下次 Spawn 可能造成問題。\n" +
                    "根本原因：Init() 未被呼叫，或傳入了空的 poolId。",
                    Core.LogCategory.Interaction);
                gameObject.SetActive(false);
                return;
            }

            if (Pooling.GenericPool.Instance == null)
            {
                Core.DebugLogger.LogError(
                    $"[PickupItem] '{gameObject.name}' 回收時 GenericPool.Instance 為 null！\n" +
                    "請確認場景中有 GenericPool 物件且尚未被銷毀。",
                    Core.LogCategory.Interaction);
                gameObject.SetActive(false);
                return;
            }

            Pooling.GenericPool.Instance.Despawn(_poolId, gameObject);
        }
    }
}
