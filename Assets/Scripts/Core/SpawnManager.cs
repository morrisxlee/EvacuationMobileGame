using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 生怪管理器：配合 WaveManager 在指定位置生成敵人。
    /// 遵守同屏怪物上限，所有敵人走物件池。
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        private static SpawnManager _instance;
        public static SpawnManager Instance => _instance;

        [TitleGroup("生成設定")]
        [Tooltip("場景中用來生成敵人的位置點列表。建議在場景邊緣放置 8~16 個空物件。")]
        [LabelText("生成點列表")]
        [Required("必須至少設定一個生成點！")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        [SerializeField] private List<Transform> _spawnPoints = new();

        [TitleGroup("生成設定")]
        [Tooltip("生成點必須距離玩家至少這麼遠，避免敵人在玩家腳下冒出來。")]
        [LabelText("最小生成距離")]
        [Min(1f)]
        [SerializeField] private float _minSpawnDistanceFromPlayer = 8f;

        [TitleGroup("生成設定")]
        [Tooltip("生成點距離玩家的最大距離。太遠的生成點會被跳過（優先選較近的）。")]
        [LabelText("最大生成距離")]
        [Min(5f)]
        [SerializeField] private float _maxSpawnDistanceFromPlayer = 30f;

        [TitleGroup("生成設定")]
        [InfoBox("設為 0 表示同幀一次性生成全部敵人（原始行為）。\n建議設 0.05~0.15：每隻間隔極短，視覺上像一波湧出，同時分散 CPU 峰值。\n1000 隻 × 0.1s = 100 秒全部就位；請根據波次怪物數量調整。")]
        [Tooltip("每生成一隻敵人後的等待時間（秒）。\n0 = 同幀一次性全部生成（等同原本行為）。\n> 0 = 逐隻陸續生成，視覺更流暢且 CPU 尖峰更分散。\n建議值：0.05（高密度）～ 0.15（標準）。")]
        [LabelText("生成間隔（秒/隻）")]
        [Min(0f)]
        [SerializeField] private float _spawnInterval = 0.1f;

        private Transform _playerTransform;
        private Data.GameConfig _gameConfig;

        // 已排進 Coroutine 尚未真正生成的數量，供 CanSpawnMore() 計入上限計算
        private int _pendingSpawnCount;

        // 重用容器（兩個 list 分開，避免 BuildAndShuffleBatchPoints 與 GetValidSpawnPoint 互相覆寫）
        private readonly List<Transform> _validSpawnPoints  = new(16);
        private readonly List<Transform> _batchSpawnPoints  = new(16);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 場景載入後初始化。
        /// </summary>
        public void Init(Transform player, Data.GameConfig config)
        {
            _playerTransform = player;
            _gameConfig = config;
        }

        /// <summary>
        /// 生成一批普通敵人。
        /// 回傳「本次預計生成數量」（同步計算），實際生成透過 Coroutine 逐隻進行。
        /// WaveManager 可立即以回傳值初始化 _enemiesRemainingInWave。
        /// </summary>
        public int SpawnNormalEnemies(IReadOnlyList<Data.EnemyData> enemyPool, int count, int stageLevel)
        {
            if (enemyPool == null || enemyPool.Count == 0)
            {
                DebugLogger.LogError("普通敵人池為空，無法生成！", LogCategory.Spawn);
                return 0;
            }

            int toSpawn = CalculateCanSpawnCount(count);
            if (toSpawn <= 0)
            {
                DebugLogger.Log($"普通敵人生成被上限攔截（已達 MaxActiveEnemies），跳過此批次。", LogCategory.Spawn);
                return 0;
            }

            _pendingSpawnCount += toSpawn;
            StartCoroutine(SpawnBatchCoroutine(enemyPool, toSpawn, stageLevel, isElite: false));
            DebugLogger.Log($"已排程生成 {toSpawn}/{count} 隻普通敵人（間隔 {_spawnInterval}s）。", LogCategory.Spawn);
            return toSpawn;
        }

        /// <summary>
        /// 生成精英怪。
        /// 回傳「本次預計生成數量」（同步計算），實際生成透過 Coroutine 逐隻進行。
        /// </summary>
        public int SpawnElites(IReadOnlyList<Data.EnemyData> elitePool, int count, int stageLevel)
        {
            if (elitePool == null || elitePool.Count == 0)
            {
                DebugLogger.LogError("精英敵人池為空，無法生成！", LogCategory.Spawn);
                return 0;
            }

            int toSpawn = CalculateCanSpawnCount(count);
            if (toSpawn <= 0)
            {
                DebugLogger.Log($"精英敵人生成被上限攔截（已達 MaxActiveEnemies），跳過此批次。", LogCategory.Spawn);
                return 0;
            }

            _pendingSpawnCount += toSpawn;
            StartCoroutine(SpawnBatchCoroutine(elitePool, toSpawn, stageLevel, isElite: true));
            DebugLogger.Log($"已排程生成 {toSpawn}/{count} 隻精英敵人（間隔 {_spawnInterval}s）。", LogCategory.Spawn);
            return toSpawn;
        }

        /// <summary>
        /// 分幀/分秒逐隻生成敵人的 Coroutine。
        /// 每次批次開始前對有效生成點做 Fisher-Yates 洗牌，再以 (i % 點數) 循環分配，
        /// 確保每個生成點被儘量均等使用，杜絕多隻敵人落在同一位置。
        /// _pendingSpawnCount 在每次嘗試後遞減，確保 CanSpawnMore() 始終準確。
        /// </summary>
        private IEnumerator SpawnBatchCoroutine(IReadOnlyList<Data.EnemyData> pool, int count, int stageLevel, bool isElite)
        {
            // _spawnInterval == 0 時使用 null，WaitForSeconds(0) 仍會等一幀，避免不必要的開銷
            var wait = _spawnInterval > 0f ? new WaitForSeconds(_spawnInterval) : null;

            // 批次開始前收集並洗牌有效生成點
            BuildAndShuffleBatchPoints();
            int batchPointCount = _batchSpawnPoints.Count;

            int actualSpawned = 0;
            int eliteIndex    = 0;

            for (int i = 0; i < count; i++)
            {
                // 若中途因其他批次導致超過上限，取消剩餘排程
                if (!CanSpawnMoreDirect())
                {
                    int remaining = count - i;
                    _pendingSpawnCount -= remaining;
                    DebugLogger.Log(
                        $"生成 Coroutine 中途達到 MaxActiveEnemies，取消剩餘 {remaining} 隻{(isElite ? "精英" : "普通")}敵人排程。",
                        LogCategory.Spawn);
                    break;
                }

                // 循環使用洗牌後的生成點（均勻分配，不連續重複）
                Transform batchPoint = batchPointCount > 0 ? _batchSpawnPoints[i % batchPointCount] : null;

                var data    = pool[Random.Range(0, pool.Count)];
                bool success = TrySpawnEnemy(data, stageLevel, batchPoint);
                _pendingSpawnCount--;

                if (success)
                {
                    actualSpawned++;
                    if (isElite)
                        GameEvents.FireEliteSpawned(eliteIndex++);
                }

                if (wait != null)
                    yield return wait;
            }

            DebugLogger.Log(
                $"Coroutine 完成：實際生成 {actualSpawned}/{count} 隻{(isElite ? "精英" : "普通")}敵人。",
                LogCategory.Spawn);
        }

        /// <summary>
        /// 收集當前有效生成點，並以 Fisher-Yates 原地洗牌，存入 _batchSpawnPoints。
        /// </summary>
        private void BuildAndShuffleBatchPoints()
        {
            _batchSpawnPoints.Clear();

            if (_playerTransform == null)
            {
                // 無玩家參照：使用全部生成點
                for (int i = 0; i < _spawnPoints.Count; i++)
                    if (_spawnPoints[i] != null) _batchSpawnPoints.Add(_spawnPoints[i]);
            }
            else
            {
                Vector2 playerPos = _playerTransform.position;
                float minSqr = _minSpawnDistanceFromPlayer * _minSpawnDistanceFromPlayer;
                float maxSqr = _maxSpawnDistanceFromPlayer * _maxSpawnDistanceFromPlayer;

                for (int i = 0; i < _spawnPoints.Count; i++)
                {
                    if (_spawnPoints[i] == null) continue;
                    float distSqr = ((Vector2)_spawnPoints[i].position - playerPos).sqrMagnitude;
                    if (distSqr >= minSqr && distSqr <= maxSqr)
                        _batchSpawnPoints.Add(_spawnPoints[i]);
                }

                // 放寬：只檢查最小距離
                if (_batchSpawnPoints.Count == 0)
                {
                    for (int i = 0; i < _spawnPoints.Count; i++)
                    {
                        if (_spawnPoints[i] == null) continue;
                        float distSqr = ((Vector2)_spawnPoints[i].position - playerPos).sqrMagnitude;
                        if (distSqr >= minSqr) _batchSpawnPoints.Add(_spawnPoints[i]);
                    }
                }

                // 最終放寬：用全部點
                if (_batchSpawnPoints.Count == 0)
                {
                    for (int i = 0; i < _spawnPoints.Count; i++)
                        if (_spawnPoints[i] != null) _batchSpawnPoints.Add(_spawnPoints[i]);
                }
            }

            // Fisher-Yates 原地洗牌
            for (int i = _batchSpawnPoints.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_batchSpawnPoints[i], _batchSpawnPoints[j]) = (_batchSpawnPoints[j], _batchSpawnPoints[i]);
            }
        }

        /// <summary>
        /// 根據當前活躍敵人數 + 排隊中尚未生成的數量，計算最多能再排進多少隻。
        /// </summary>
        private int CalculateCanSpawnCount(int requested)
        {
            var tickMgr = AI.EnemyTickManager.Instance;
            int maxActive = _gameConfig != null ? _gameConfig.MaxActiveEnemies : 100;
            int current = (tickMgr?.ActiveEnemyCount ?? 0) + _pendingSpawnCount;
            int available = Mathf.Max(0, maxActive - current);
            return Mathf.Min(requested, available);
        }

        /// <param name="preSelectedPoint">由 SpawnBatchCoroutine 預先選好的生成點；null 時退回 GetValidSpawnPoint()。</param>
        private bool TrySpawnEnemy(Data.EnemyData data, int stageLevel, Transform preSelectedPoint = null)
        {
            if (string.IsNullOrEmpty(data.PoolId))
            {
                DebugLogger.LogError($"敵人 '{data.DisplayName}' 的 PoolId 為空！", LogCategory.Spawn);
                return false;
            }

            var pool = Pooling.GenericPool.Instance;
            if (pool == null)
            {
                DebugLogger.LogError("GenericPool.Instance 為 null！", LogCategory.Spawn);
                return false;
            }

            Transform spawnPoint = preSelectedPoint != null ? preSelectedPoint : GetValidSpawnPoint();
            if (spawnPoint == null)
            {
                DebugLogger.LogWarning("找不到有效的生成點！", LogCategory.Spawn);
                return false;
            }

            // 隨機小偏移：即使兩隻分配到同一生成點，也能保持 dist > 0，讓分離力正常運作
            Vector3 spawnPos = spawnPoint.position + (Vector3)(Random.insideUnitCircle * 0.6f);
            var obj = pool.Spawn(data.PoolId, spawnPos, Quaternion.identity);
            if (obj == null) return false;

            var enemy = obj.GetComponent<AI.EnemyController>();
            if (enemy != null)
            {
                enemy.Init(data, stageLevel, _playerTransform, data.PoolId);
                AI.EnemyTickManager.Instance?.Register(enemy);
            }
            else
            {
                DebugLogger.LogError($"物件池 '{data.PoolId}' 的 prefab 缺少 EnemyController 組件！", LogCategory.Spawn);
                pool.Despawn(data.PoolId, obj);
                return false;
            }

            return true;
        }

        // 供外部呼叫：計入 pending 排隊中的數量，用於判斷是否能繼續排程新批次
        private bool CanSpawnMore()
        {
            var tickMgr = AI.EnemyTickManager.Instance;
            int maxActive = _gameConfig != null ? _gameConfig.MaxActiveEnemies : 100;
            int current = (tickMgr?.ActiveEnemyCount ?? 0) + _pendingSpawnCount;
            return current < maxActive;
        }

        // Coroutine 內部使用：只看當前活躍數（pending 已在 CalculateCanSpawnCount 預扣），避免重複計算
        private bool CanSpawnMoreDirect()
        {
            var tickMgr = AI.EnemyTickManager.Instance;
            int maxActive = _gameConfig != null ? _gameConfig.MaxActiveEnemies : 100;
            return tickMgr == null || tickMgr.ActiveEnemyCount < maxActive;
        }

        /// <summary>
        /// 從有效生成點中隨機選一個（滿足距離限制）。
        /// 僅供 TrySpawnEnemy 的 preSelectedPoint == null 時作為退路；
        /// 正常情況下 SpawnBatchCoroutine 已透過 BuildAndShuffleBatchPoints 預先分配好生成點。
        /// </summary>
        private Transform GetValidSpawnPoint()
        {
            _validSpawnPoints.Clear();

            if (_playerTransform == null)
            {
                return _spawnPoints.Count > 0 ? _spawnPoints[Random.Range(0, _spawnPoints.Count)] : null;
            }

            Vector2 playerPos = _playerTransform.position;
            float minSqr = _minSpawnDistanceFromPlayer * _minSpawnDistanceFromPlayer;
            float maxSqr = _maxSpawnDistanceFromPlayer * _maxSpawnDistanceFromPlayer;

            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                if (_spawnPoints[i] == null) continue;
                float distSqr = ((Vector2)_spawnPoints[i].position - playerPos).sqrMagnitude;
                if (distSqr >= minSqr && distSqr <= maxSqr)
                    _validSpawnPoints.Add(_spawnPoints[i]);
            }

            if (_validSpawnPoints.Count == 0)
            {
                for (int i = 0; i < _spawnPoints.Count; i++)
                {
                    if (_spawnPoints[i] == null) continue;
                    float distSqr = ((Vector2)_spawnPoints[i].position - playerPos).sqrMagnitude;
                    if (distSqr >= minSqr)
                        _validSpawnPoints.Add(_spawnPoints[i]);
                }
            }

            if (_validSpawnPoints.Count == 0)
                return _spawnPoints.Count > 0 ? _spawnPoints[Random.Range(0, _spawnPoints.Count)] : null;

            return _validSpawnPoints[Random.Range(0, _validSpawnPoints.Count)];
        }
    }
}
