using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Combat
{
    /// <summary>
    /// 武器控制器：掛在玩家身上，負責自動攻擊邏輯。
    /// 優先鎖定最近敵人，在射程內自動瞄準/開火。
    /// 低 GC：使用 NonAlloc 物理查詢，重用陣列。
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [TitleGroup("武器資料")]
        [Tooltip("武器的 ScriptableObject 資料。若留空，會在 Start 時從 GameLoopManager 取得選定武器。")]
        [LabelText("武器資料")]
        [SerializeField] private Data.WeaponData _weaponData;

        [TitleGroup("攻擊設定")]
        [Tooltip("敵人所在的 Layer。用於 Physics2D 偵測。")]
        [LabelText("敵人圖層")]
        [SerializeField] private LayerMask _enemyLayer;

        [TitleGroup("攻擊設定")]
        [Tooltip("子彈發射的位置。若留空，使用玩家中心位置。")]
        [LabelText("發射點")]
        [SerializeField] private Transform _firePoint;

        [TitleGroup("效能設定")]
        [Tooltip("Physics2D.OverlapCircleNonAlloc 的最大結果數。建議 50~100。")]
        [LabelText("最大偵測數")]
        [Min(10)]
        [SerializeField] private int _maxDetectionResults = 50;

        [TitleGroup("除錯")]
        [Tooltip("勾選後會在 Scene 視窗顯示攻擊範圍。")]
        [LabelText("顯示 Gizmo")]
        [SerializeField] private bool _drawGizmos = true;

        // ── 運行時 ──
        private float _fireCooldown;
        private Collider2D[] _detectionResults;
        private Transform _currentTarget;
        private Player.PlayerStats _playerStats;
        private Feedback.FeedbackBridge _feedbackBridge;

        // ── 升級加成（由 UpgradeManager 寫入） ──
        private float _bonusDamagePercent;
        private float _bonusFireRatePercent;
        private float _bonusRangePercent;
        private float _bonusProjectileSpeedPercent;
        private int _bonusPelletCount;

        public Data.WeaponData WeaponData => _weaponData;
        public Transform CurrentTarget => _currentTarget;

        private void Awake()
        {
            _detectionResults = new Collider2D[_maxDetectionResults];
            _playerStats = GetComponent<Player.PlayerStats>();
            _feedbackBridge = GetComponent<Feedback.FeedbackBridge>();
        }

        private void Start()
        {
            // 若未手動指定，嘗試從 GameLoop 取得選定武器
            if (_weaponData == null)
            {
                var loop = Core.GameLoopManager.Instance;
                if (loop != null && loop.SelectedWeapon != null)
                {
                    _weaponData = loop.SelectedWeapon;
                }
                else
                {
                    Core.DebugLogger.LogError("WeaponController 沒有指定 WeaponData，也無法從 GameLoopManager 取得！", Core.LogCategory.Combat);
                }
            }
        }

        private void Update()
        {
            if (_weaponData == null) return;
            if (_playerStats != null && _playerStats.IsDead) return;

            // 遊戲暫停時不更新
            var loopState = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (loopState == Core.GameState.Paused || loopState == Core.GameState.Menu || loopState == Core.GameState.Result)
                return;

            _fireCooldown -= Time.deltaTime;

            FindNearestEnemy();

            // 發射點持續朝向當前目標（Z 軸旋轉），讓武器視覺上始終對準敵人
            UpdateFirePointRotation();

            if (_currentTarget != null && _fireCooldown <= 0f)
            {
                Fire();
            }
        }

        /// <summary>
        /// 每幀將發射點（FirePoint）的世界旋轉對準當前目標方向（Z 軸旋轉）。
        /// 沒有目標時保持上一幀的旋轉，不歸零。
        /// 使用世界旋轉（rotation）確保無論 FirePoint 在階層中哪一層都能正確對準。
        /// </summary>
        private void UpdateFirePointRotation()
        {
            if (_firePoint == null || _currentTarget == null) return;

            Vector2 dir   = (Vector2)(_currentTarget.position - _firePoint.position);
            float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _firePoint.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// 用 Physics2D.OverlapCircleNonAlloc 找最近敵人。
        /// </summary>
        private void FindNearestEnemy()
        {
            float range = GetEffectiveRange();
            int count = Physics2D.OverlapCircleNonAlloc(transform.position, range, _detectionResults, _enemyLayer);

            _currentTarget = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = _detectionResults[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;

                float dist = ((Vector2)(col.transform.position - transform.position)).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    _currentTarget = col.transform;
                }
            }
        }

        private void Fire()
        {
            int level = _playerStats != null ? _playerStats.WeaponLevel : 0;
            float interval = 1f / GetEffectiveFireRate(level);
            _fireCooldown = interval;

            int pellets = GetEffectivePelletCount();
            float spread = _weaponData.SpreadAngle;

            Vector2 dir = (_currentTarget.position - transform.position).normalized;
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            for (int i = 0; i < pellets; i++)
            {
                float angle = baseAngle;
                if (pellets > 1)
                {
                    // 均勻分布在散射角度內
                    float t = (float)i / (pellets - 1) - 0.5f; // -0.5 ~ 0.5
                    angle += t * spread;
                }

                SpawnProjectile(angle, level);
            }

            _feedbackBridge?.PlayFire();
        }

        private void SpawnProjectile(float angleDeg, int weaponLevel)
        {
            var pool = Pooling.GenericPool.Instance;
            if (pool == null)
            {
                Core.DebugLogger.LogError("GenericPool.Instance 為 null，無法生成投射物！", Core.LogCategory.Combat);
                return;
            }

            string poolId = _weaponData.ProjectilePoolId;
            if (string.IsNullOrEmpty(poolId))
            {
                Core.DebugLogger.LogError($"武器 '{_weaponData.DisplayName}' 的 ProjectilePoolId 為空！", Core.LogCategory.Combat);
                return;
            }

            Vector3 spawnPos = _firePoint != null ? _firePoint.position : transform.position;
            Quaternion rot = Quaternion.Euler(0f, 0f, angleDeg);

            var obj = pool.Spawn(poolId, spawnPos, rot);
            if (obj == null) return;

            var projectile = obj.GetComponent<Projectile>();
            if (projectile != null)
            {
                float dmg = GetEffectiveDamage(weaponLevel);
                float spd = GetEffectiveProjectileSpeed();
                Vector2 dir = new Vector2(Mathf.Cos(angleDeg * Mathf.Deg2Rad), Mathf.Sin(angleDeg * Mathf.Deg2Rad));
                projectile.Init(dir, spd, dmg, poolId);
            }
        }

        // ══════════════════════════════════════
        //  升級加成 API（UpgradeManager 呼叫）
        // ══════════════════════════════════════

        public void AddBonusDamagePercent(float value) => _bonusDamagePercent += value;
        public void AddBonusFireRatePercent(float value) => _bonusFireRatePercent += value;
        public void AddBonusRangePercent(float value) => _bonusRangePercent += value;
        public void AddBonusProjectileSpeedPercent(float value) => _bonusProjectileSpeedPercent += value;
        public void AddBonusPelletCount(int value) => _bonusPelletCount += value;

        // ══════════════════════════════════════
        //  有效數值計算
        // ══════════════════════════════════════

        private float GetEffectiveDamage(int level)
        {
            return _weaponData.GetDamageAtLevel(level) * (1f + _bonusDamagePercent);
        }

        private float GetEffectiveFireRate(int level)
        {
            return _weaponData.GetFireRateAtLevel(level) * (1f + _bonusFireRatePercent);
        }

        private float GetEffectiveRange()
        {
            return _weaponData.AttackRange * (1f + _bonusRangePercent);
        }

        private float GetEffectiveProjectileSpeed()
        {
            return _weaponData.ProjectileSpeed * (1f + _bonusProjectileSpeedPercent);
        }

        private int GetEffectivePelletCount()
        {
            return _weaponData.PelletCount + _bonusPelletCount;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos || _weaponData == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _weaponData.AttackRange);
        }
    }
}
