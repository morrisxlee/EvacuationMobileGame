using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Combat
{
    /// <summary>
    /// 投射物：由物件池管理，碰到敵人造成傷害後自動回收。
    /// 支援超時自動回收避免物件洩漏。
    ///
    /// 效能備注：
    ///   s_doorSensorLayer 在 Awake() 快取（所有實例共用一次 LayerMask.NameToLayer 查詢），
    ///   OnTriggerEnter2D 中只做整數比對，1000 發子彈同幀零 GC。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Projectile : MonoBehaviour, Pooling.IPoolable
    {
        [TitleGroup("投射物設定")]
        [Tooltip("投射物最長飛行時間（秒）。超時自動回收到物件池，避免飛出地圖後長期佔用記憶體。\n" +
                 "建議依關卡尺寸設定，確保子彈在邊界前被地形吸收而非超時回收。")]
        [LabelText("最大飛行時間（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _maxLifetime = 5f;

        // ── 執行時私有狀態 ──
        private Rigidbody2D _rb;
        private float _damage;
        private float _speed;
        private string _poolId;
        private float _lifeTimer;
        private bool _initialized;

        // 靜態快取 DoorSensor Layer ID，所有實例共用，只在第一個實例 Awake() 時查詢一次字串。
        // 投射物不傷門、不被門觸發器消耗（門用 A* Tag 架構，投射物穿過觸發區無效果）。
        private static int s_doorSensorLayer = -1;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (s_doorSensorLayer < 0)
            {
                s_doorSensorLayer = LayerMask.NameToLayer("DoorSensor");
                if (s_doorSensorLayer < 0)
                    Core.DebugLogger.LogError(
                        $"投射物 Prefab '{gameObject.name}'：找不到 'DoorSensor' Layer！\n" +
                        "請在 Edit → Project Settings → Tags and Layers 新增 'DoorSensor' Layer。\n" +
                        "若不設定，玩家投射物可能意外觸發門的觸發器並被提前回收。",
                        Core.LogCategory.Combat);
            }
        }

        /// <summary>
        /// 由 WeaponController 或 EnemyController.FireProjectile() 呼叫初始化。
        /// </summary>
        public void Init(Vector2 direction, float speed, float damage, string poolId)
        {
            _speed    = speed;
            _damage   = damage;
            _poolId   = poolId;
            _lifeTimer    = _maxLifetime;
            _initialized  = true;

            _rb.velocity = direction.normalized * speed;
        }

        private void Update()
        {
            if (!_initialized) return;

            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
                ReturnToPool();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_initialized) return;

            // 跳過門感應層：投射物穿過門觸發區無效（門阻擋改用 A* Tag，不依賴碰撞）。
            // 門實作了 IDamageable，若不跳過，玩家子彈會對門造成傷害並被提前回收。
            if (s_doorSensorLayer >= 0 && other.gameObject.layer == s_doorSensorLayer) return;

            var damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(_damage);
                ReturnToPool();
            }
        }

        private void ReturnToPool()
        {
            _initialized  = false;
            _rb.velocity  = Vector2.zero;

            var pool = Pooling.GenericPool.Instance;
            if (pool != null && !string.IsNullOrEmpty(_poolId))
                pool.Despawn(_poolId, gameObject);
            else
                gameObject.SetActive(false);
        }

        // ── IPoolable ──
        public void OnSpawnFromPool()
        {
            _lifeTimer   = _maxLifetime;
            _initialized = false;
        }

        public void OnReturnToPool()
        {
            _initialized = false;
            _rb.velocity = Vector2.zero;
        }
    }
}
