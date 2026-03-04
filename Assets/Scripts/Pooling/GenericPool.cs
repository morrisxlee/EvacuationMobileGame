using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Pooling
{
    /// <summary>
    /// 通用物件池：敵人、子彈、特效、掉落物、UI 飄字全部走這裡。
    /// 支援預熱（Prewarm）、自動擴容、回收時自動重置。
    /// 低 GC 設計：內部使用 Stack 而非 List，無 LINQ。
    /// </summary>
    public class GenericPool : MonoBehaviour
    {
        [System.Serializable]
        public class PoolEntry
        {
            [Tooltip("此物件池的唯一識別碼。必須與其他腳本中的 PoolId 欄位完全一致。")]
            [LabelText("池 ID")]
            [Required("必須填寫池 ID！")]
            public string id;

            [Tooltip("要池化的 Prefab。")]
            [LabelText("Prefab")]
            [Required("必須指定 Prefab！")]
            [AssetsOnly]
            public GameObject prefab;

            [Tooltip("遊戲開始時預先生成的數量。建議設定為平均使用量。")]
            [LabelText("預熱數量")]
            [Min(1)]
            public int prewarmCount = 10;

            [Tooltip("此物件池的最大容量。超過此數量時將無法再生成新物件。")]
            [LabelText("最大數量")]
            [Min(1)]
            public int maxCount = 200;
        }

        [TitleGroup("物件池設定")]
        [InfoBox("所有敵人、子彈、特效都必須在此註冊。PoolEntry 的 ID 必須與各腳本中的 PoolId 欄位完全一致。")]
        [Tooltip("物件池條目列表。")]
        [LabelText("池條目")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowItemCount = true)]
        [SerializeField] private List<PoolEntry> _entries = new List<PoolEntry>();

        private readonly Dictionary<string, Stack<GameObject>> _pools = new();
        private readonly Dictionary<string, PoolEntry> _entryLookup = new();
        private readonly Dictionary<string, Transform> _parentLookup = new();
        private readonly HashSet<GameObject> _activeObjects = new();

        private static GenericPool _instance;
        public static GenericPool Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitAllPools();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void InitAllPools()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (string.IsNullOrEmpty(entry.id) || entry.prefab == null)
                {
                    Core.DebugLogger.LogError($"PoolEntry index {i} 的 id 或 prefab 為空，請修正！", Core.LogCategory.Pooling);
                    continue;
                }
                RegisterPool(entry);
            }
        }

        /// <summary>
        /// 執行時期動態註冊新的 pool（例如敵人種類在 runtime 才確定時）。
        /// </summary>
        public void RegisterPool(PoolEntry entry)
        {
            if (_entryLookup.ContainsKey(entry.id))
            {
                Core.DebugLogger.LogWarning($"Pool '{entry.id}' 已存在，跳過重複註冊。", Core.LogCategory.Pooling);
                return;
            }

            _entryLookup[entry.id] = entry;
            _pools[entry.id] = new Stack<GameObject>(entry.prewarmCount);

            var parent = new GameObject($"Pool_{entry.id}");
            parent.transform.SetParent(transform);
            _parentLookup[entry.id] = parent.transform;

            for (int i = 0; i < entry.prewarmCount; i++)
            {
                CreateNewInstance(entry.id);
            }

            Core.DebugLogger.Log($"Pool '{entry.id}' 已建立，預熱 {entry.prewarmCount} 個。", Core.LogCategory.Pooling);
        }

        /// <summary>
        /// 從池中取得物件。若池空且未達上限則自動擴容。
        /// </summary>
        public GameObject Spawn(string id, Vector3 position, Quaternion rotation)
        {
            if (!_pools.TryGetValue(id, out var stack))
            {
                Core.DebugLogger.LogError($"Pool '{id}' 不存在！請先透過 Inspector 或 RegisterPool 註冊。", Core.LogCategory.Pooling);
                return null;
            }

            GameObject obj;
            if (stack.Count > 0)
            {
                obj = stack.Pop();
            }
            else
            {
                // 檢查是否達到上限
                if (_activeObjects.Count >= _entryLookup[id].maxCount)
                {
                    Core.DebugLogger.LogWarning($"Pool '{id}' 已達上限 {_entryLookup[id].maxCount}，無法再生成！", Core.LogCategory.Pooling);
                    return null;
                }
                obj = CreateNewInstance(id);
                if (obj == null) return null;
                // CreateNewInstance 會放入 stack，再拿出來
                obj = stack.Pop();
            }

            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
            _activeObjects.Add(obj);

            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnSpawnFromPool();

            return obj;
        }

        /// <summary>
        /// 回收物件到池中。
        /// </summary>
        public void Despawn(string id, GameObject obj)
        {
            if (obj == null) return;

            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnReturnToPool();

            obj.SetActive(false);
            _activeObjects.Remove(obj);

            if (_pools.TryGetValue(id, out var stack))
            {
                stack.Push(obj);
            }
            else
            {
                Core.DebugLogger.LogError($"Pool '{id}' 不存在，無法回收物件 '{obj.name}'！", Core.LogCategory.Pooling);
                Destroy(obj);
            }
        }

        /// <summary>
        /// 回收該 pool 所有活躍物件。
        /// </summary>
        public void DespawnAll(string id)
        {
            if (!_parentLookup.TryGetValue(id, out var parent)) return;

            // 使用 parent 下所有子物件，避免遍歷 _activeObjects 時修改集合
            int childCount = parent.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (child.activeSelf)
                {
                    Despawn(id, child);
                }
            }
        }

        private GameObject CreateNewInstance(string id)
        {
            if (!_entryLookup.TryGetValue(id, out var entry))
            {
                Core.DebugLogger.LogError($"無法建立 Pool '{id}' 的實例：entry 不存在。", Core.LogCategory.Pooling);
                return null;
            }

            var obj = Instantiate(entry.prefab, _parentLookup[id]);
            obj.SetActive(false);
            _pools[id].Push(obj);
            return obj;
        }

        /// <summary>
        /// 回收所有 pool 的所有活躍物件。
        /// 在切換場景（例如返回主選單）前呼叫，確保 DontDestroyOnLoad 池的活躍物件
        /// （敵人、子彈、掉落物、特效）不會殘留並顯示在新場景中。
        /// </summary>
        public void DespawnAllPools()
        {
            foreach (var id in _entryLookup.Keys)
                DespawnAll(id);

            Core.DebugLogger.Log("GenericPool：所有物件池已全部回收。", Core.LogCategory.Pooling);
        }

        /// <summary>
        /// 取得指定 pool 中的閒置數量。
        /// </summary>
        public int GetAvailableCount(string id)
        {
            return _pools.TryGetValue(id, out var stack) ? stack.Count : 0;
        }
    }

    /// <summary>
    /// 可池化物件介面：在取出/回收時執行自訂邏輯（重置狀態等）。
    /// </summary>
    public interface IPoolable
    {
        void OnSpawnFromPool();
        void OnReturnToPool();
    }
}
