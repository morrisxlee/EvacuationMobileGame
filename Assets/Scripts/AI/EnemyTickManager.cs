using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Pathfinding;

namespace SurvivalDemo.AI
{
    /// <summary>
    /// 敵人分幀更新管理器：將所有活躍敵人分批 Tick，避免同幀尖峰。
    /// 每幀只更新一部分敵人，分散 CPU 負載。
    /// 遠距敵人使用更低頻率的簡化邏輯。
    /// </summary>
    public class EnemyTickManager : MonoBehaviour
    {
        private static EnemyTickManager _instance;
        public static EnemyTickManager Instance => _instance;

        [TitleGroup("效能設定")]
        [InfoBox("分幀更新可大幅降低同時處理 1000+ 敵人時的 CPU 尖峰。")]
        [Tooltip("每幀最多更新幾隻近距敵人（完整 AI 邏輯）。建議 30~50。")]
        [LabelText("近距 Tick 預算")]
        [Min(1)]
        [SerializeField] private int _nearTickBudget = 30;

        [TitleGroup("效能設定")]
        [Tooltip("每幀最多更新幾隻遠距敵人（簡化 AI 邏輯）。建議 20~40。")]
        [LabelText("遠距 Tick 預算")]
        [Min(1)]
        [SerializeField] private int _farTickBudget = 20;

        [TitleGroup("效能設定")]
        [Tooltip("判定近距/遠距的分界距離（世界單位）。近距敵人優先更新。")]
        [LabelText("近距/遠距分界")]
        [Min(5f)]
        [SerializeField] private float _nearFarThreshold = 15f;

        [TitleGroup("診斷工具")]
        [InfoBox("開啟後輸出細粒度敵人狀態（首次 Tick、路徑成功、LoS 轉換、卡牆）。\n需同時在 DebugLogger 開啟「詳細敵人診斷」才實際輸出。\n1000 隻同時開啟時 log 量大，建議只在定位問題時短暫啟用。")]
        [Tooltip("是否啟用 EnemyDiag 詳細診斷日誌。\n須同時在 DebugLogger Inspector 開啟「詳細敵人診斷（_enableEnemyDiag）」才生效。")]
        [LabelText("詳細敵人診斷")]
        [SerializeField] private bool _enableVerboseDiag = false;

        // ── 運行時 ──
        private readonly List<EnemyController> _enemies = new(256);
        private int _nearTickIndex;
        private int _farTickIndex;
        private Transform _playerTransform;

        // 重用容器避免 GC
        private readonly List<EnemyController> _nearBatch = new(64);
        private readonly List<EnemyController> _farBatch = new(64);

        // ── 診斷狀態 ──
        private bool _firstUpdateDone;

        public int ActiveEnemyCount => _enemies.Count;

        /// <summary>
        /// 供 EnemyController 查詢：是否應輸出 EnemyDiag 詳細日誌。
        /// 條件：EnemyTickManager._enableVerboseDiag AND DebugLogger._enableEnemyDiag 兩者均為 ON。
        /// </summary>
        public static bool VerboseDiagActive =>
            _instance != null && _instance._enableVerboseDiag && Core.DebugLogger.IsEnemyDiagEnabled;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // ── Physics Layer Collision Matrix 讀回驗證 ──
            // 直接讀取 Unity 實際執行中的矩陣值，確認設定正確。
            // 期望：Enemy ↔ Default = false（不忽略 = 有碰撞）；Enemy ↔ Enemy = true（忽略 = 穿透，由分離力處理）
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int defaultLayer = LayerMask.NameToLayer("Default");

            if (enemyLayer < 0)
            {
                Core.DebugLogger.LogError(
                    "找不到 'Enemy' Layer！\n" +
                    "請在 Edit → Project Settings → Tags and Layers 中建立 'Enemy' Layer，並將所有敵人 Prefab 設為此 Layer。",
                    Core.LogCategory.AI);
            }
            else if (defaultLayer < 0)
            {
                Core.DebugLogger.LogError(
                    "找不到 'Default' Layer！Unity 內建 Layer，請確認 Project Settings 未被意外修改。",
                    Core.LogCategory.AI);
            }
            else
            {
                bool enemyIgnoresWall  = Physics2D.GetIgnoreLayerCollision(enemyLayer, defaultLayer);
                bool enemyIgnoresEnemy = Physics2D.GetIgnoreLayerCollision(enemyLayer, enemyLayer);

                // Enemy ↔ Default 應該 = true（停用碰撞）
                // 敵人改為 Kinematic 模式，移動完全由 A* 路徑導向，物理牆壁碰撞已無必要。
                // 停用此碰撞可顯著降低 1000 隻敵人的物理求解器開銷。
                if (!enemyIgnoresWall)
                    Core.DebugLogger.LogOnceWarning("TickMgr_WallMatrix_Warn",
                        "效能提示：Enemy ↔ Default 目前設為「有碰撞」。\n" +
                        "敵人已改為 Kinematic 模式，靠 A* 路徑避牆，物理牆壁碰撞已不必要。\n" +
                        "建議在 Edit → Project Settings → Physics 2D → Layer Collision Matrix\n" +
                        "→ 取消 Enemy ↔ Default 交叉格的勾選（停用碰撞）以大幅提升 1000 隻敵人的效能。",
                        Core.LogCategory.AI);
                else
                    Core.DebugLogger.LogOnce("TickMgr_WallMatrix_OK",
                        $"[Physics Matrix] Enemy ↔ Default = 碰撞停用 ✓（Kinematic 敵人靠 A* 避牆，效能最佳化）",
                        Core.LogCategory.EnemyDiag);

                        // Enemy ↔ Enemy 應該 = true（忽略碰撞，由 RVOSimulator 局部避讓處理）
                // 停用物理碰撞可省去 1000 隻敵人之間的物理求解器計算，效能關鍵。
                if (!enemyIgnoresEnemy)
                    Core.DebugLogger.LogOnceWarning("TickMgr_EnemyMatrix_Warn",
                        "Physics Matrix 提示：Enemy ↔ Enemy 目前設為「有碰撞」。\n" +
                        "1000 隻敵人互相物理碰撞會造成嚴重效能問題。\n" +
                        "建議在 Project Settings → Physics 2D → Layer Collision Matrix\n" +
                        "→ 找到 Enemy ↔ Enemy 交叉格，取消勾選（停用碰撞，由 RVOSimulator 負責局部避讓）。",
                        Core.LogCategory.AI);
                else
                    Core.DebugLogger.LogOnce("TickMgr_EnemyMatrix_OK",
                        $"[Physics Matrix] Enemy ↔ Enemy = 碰撞停用 ✓（RVOSimulator 局部避讓）",
                        Core.LogCategory.EnemyDiag);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 場景載入後由 GameScene 初始化器呼叫。
        /// </summary>
        public void SetPlayerTransform(Transform player)
        {
            _playerTransform = player;
        }

        /// <summary>
        /// 註冊新的敵人（生成後呼叫）。
        /// </summary>
        public void Register(EnemyController enemy)
        {
            if (!_enemies.Contains(enemy))
            {
                _enemies.Add(enemy);
            }
        }

        /// <summary>
        /// 移除敵人（死亡/回收後呼叫）。
        /// </summary>
        public void Unregister(EnemyController enemy)
        {
            _enemies.Remove(enemy);
        }

        private void Update()
        {
            // 遊戲暫停時不更新
            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (state == Core.GameState.Paused || state == Core.GameState.Menu || state == Core.GameState.Result)
                return;

            // 第一次進入 Update（且非暫停狀態）：輸出系統健康快照
            if (!_firstUpdateDone)
            {
                _firstUpdateDone = true;
                LogFirstUpdateSnapshot(state);
            }

            if (_playerTransform == null || _enemies.Count == 0) return;

            float dt = Time.deltaTime;
            float thresholdSqr = _nearFarThreshold * _nearFarThreshold;
            Vector2 playerPos = _playerTransform.position;

            // 清空批次容器（不 new，重用）
            _nearBatch.Clear();
            _farBatch.Clear();

            // 分類敵人
            int count = _enemies.Count;
            for (int i = 0; i < count; i++)
            {
                var e = _enemies[i];
                if (e == null || !e.gameObject.activeInHierarchy) continue;

                float distSqr = ((Vector2)e.transform.position - playerPos).sqrMagnitude;
                if (distSqr <= thresholdSqr)
                    _nearBatch.Add(e);
                else
                    _farBatch.Add(e);
            }

            // 近距批次 Tick（完整 AI 邏輯，RVO 由 RVOSimulator 全局統一處理）
            TickBatch(_nearBatch, ref _nearTickIndex, _nearTickBudget, dt);

            // 遠距批次 Tick（較低頻率：deltaTime * 2 模擬低頻更新；RVO 仍每幀運算，只有 Tick 頻率降低）
            TickBatch(_farBatch, ref _farTickIndex, _farTickBudget, dt * 2f);

            // 清理已失效的敵人（每幀掃一遍開銷極低）
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                if (_enemies[i] == null || !_enemies[i].gameObject.activeInHierarchy)
                {
                    _enemies.RemoveAt(i);
                }
            }

            // ── 每幀：維持 RVO NavMesh Obstacle 查詢持續有效 ──
            // 必須每幀（每次 RVO 模擬步驟前）呼叫 SetObstacleQuery，否則 RVO Burst Job 無法感知
            // Grid Graph 邊界（RVOController.UpdateAgentProperties 讀取後會清除快取的節點）。
            // 搭配 RVOSimulator.useNavmeshAsObstacle = true，RVO 才能從計算層阻止 Agent 被推入牆。
            // 每次呼叫 = 1 次 null 檢查 + 1 次整數賦值，1000 隻敵人合計 < 5μs，零 GC。
            int rvoQueryCount = _enemies.Count;
            for (int i = 0; i < rvoQueryCount; i++)
                _enemies[i].ApplyRVOObstacleQuery();
        }

        private void TickBatch(List<EnemyController> batch, ref int startIndex, int budget, float dt)
        {
            if (batch.Count == 0) return;

            // 修正越界
            if (startIndex >= batch.Count) startIndex = 0;

            int ticked = 0;
            int idx = startIndex;
            while (ticked < budget && ticked < batch.Count)
            {
                batch[idx].Tick(dt);
                idx = (idx + 1) % batch.Count;
                ticked++;
            }
            startIndex = idx;
        }

        // ══════════════════════════════════════
        //  診斷方法
        // ══════════════════════════════════════

        /// <summary>
        /// 第一次 Update 執行時的系統健康快照，輸出到 EnemyDiag 類別（需開啟診斷）。
        /// 用於確認「TickManager 本身是否正常啟動」。
        /// </summary>
        private void LogFirstUpdateSnapshot(Core.GameState state)
        {
            bool astarReady = AstarPath.active != null;
            bool rvoReady   = Pathfinding.RVO.RVOSimulator.active != null;

            string playerStatus = _playerTransform != null
                ? $"已設定（{_playerTransform.name}，位置 {_playerTransform.position}）"
                : "⚠ 為 null！請確認 GameSceneInit.SetPlayerTransform() 已呼叫";

            string astarStatus = astarReady
                ? "已就緒"
                : "⚠ 為 null！場景中未找到 AstarPath 物件，所有敵人將使用直線移動";

            string rvoStatus = rvoReady
                ? "已就緒"
                : "⚠ 為 null！場景中缺少 RVO Simulator，敵人群體避讓將完全失效";

            Core.DebugLogger.LogOnce("TickMgr_FirstUpdate",
                $"[EnemyTickManager] 系統健康快照\n" +
                $"  GameState        = {state}\n" +
                $"  PlayerTransform  = {playerStatus}\n" +
                $"  AstarPath.active = {astarStatus}\n" +
                $"  RVOSimulator     = {rvoStatus}\n" +
                $"  已註冊敵人數     = {_enemies.Count}\n" +
                $"  近距 Tick 預算   = {_nearTickBudget}  遠距 Tick 預算 = {_farTickBudget}\n" +
                $"  詳細診斷開關     = {(_enableVerboseDiag && Core.DebugLogger.IsEnemyDiagEnabled ? "ON（EnemyDiag 輸出中）" : "OFF")}",
                Core.LogCategory.EnemyDiag);

            // PlayerTransform 為 null 時升級為 Error（always-on）
            if (_playerTransform == null)
                Core.DebugLogger.LogOnceError("TickMgr_PlayerNull",
                    "EnemyTickManager：PlayerTransform 為 null，敵人無法追蹤玩家！\n" +
                    "請確認 GameSceneInit 已呼叫 EnemyTickManager.SetPlayerTransform(playerTransform)。",
                    Core.LogCategory.AI);

            if (!astarReady)
                Core.DebugLogger.LogOnceError("TickMgr_AstarNull",
                    "EnemyTickManager：AstarPath.active 為 null，A* 路徑系統未啟動！\n" +
                    "請在場景中新增空物件並掛上 AstarPath（Pathfinding > Pathfinder），設定 Grid Graph 後重啟。",
                    Core.LogCategory.AI);

            if (!rvoReady)
                Core.DebugLogger.LogOnceError("TickMgr_RVONull",
                    "EnemyTickManager：RVOSimulator 為 null！敵人的群體局部避讓（防重疊）將完全失效。\n" +
                    "修復步驟：\n" +
                    "  1) 在 GameScene 的任意空物件上 Add Component → Pathfinding > Local Avoidance > RVO Simulator\n" +
                    "  2) 將 Movement Plane 設為 XY（2D 上方視角遊戲必填，預設是 XZ 3D 模式）\n" +
                    "  3) 確認所有敵人 Prefab 上掛有 RVOController（EnemyController 已加 RequireComponent）",
                    Core.LogCategory.AI);
        }

        /// <summary>
        /// 執行時手動觸發診斷快照，列出當前所有敵人的關鍵狀態。
        /// 在 Odin Inspector 中點擊此按鈕即可輸出到 Console。
        /// </summary>
        [Button("診斷快照（執行時點擊）"), TitleGroup("診斷工具")]
        [GUIColor(0.4f, 0.9f, 0.6f)]
        private void DumpDiagnostics()
        {
            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            bool astarReady = AstarPath.active != null;
            bool rvoReady   = Pathfinding.RVO.RVOSimulator.active != null;

            int nearCount = _nearBatch.Count;
            int farCount  = _farBatch.Count;
            float nearUtil = _nearTickBudget > 0 ? (float)Mathf.Min(nearCount, _nearTickBudget) / _nearTickBudget * 100f : 0f;
            float farUtil  = _farTickBudget  > 0 ? (float)Mathf.Min(farCount,  _farTickBudget)  / _farTickBudget  * 100f : 0f;

            System.Text.StringBuilder sb = new();
            sb.AppendLine("══════════════ EnemyTickManager 診斷快照 ══════════════");
            sb.AppendLine($"  時間             : {Time.time:F2}s");
            sb.AppendLine($"  GameState        : {state}");
            sb.AppendLine($"  PlayerTransform  : {(_playerTransform != null ? _playerTransform.name + " @ " + (Vector2)_playerTransform.position : "NULL ⚠")}");
            sb.AppendLine($"  AstarPath.active : {(astarReady ? "OK" : "NULL ⚠")}");
            sb.AppendLine($"  RVOSimulator     : {(rvoReady ? "OK" : "NULL ⚠（敵人局部避讓失效！）")}");
            sb.AppendLine($"  活躍敵人總數     : {_enemies.Count}");
            sb.AppendLine($"  近距批次         : {nearCount} 隻  預算使用率 {nearUtil:F0}%  ({_nearTickBudget}/幀)");
            sb.AppendLine($"  遠距批次         : {farCount} 隻  預算使用率 {farUtil:F0}%  ({_farTickBudget}/幀)");
            sb.AppendLine($"  詳細診斷         : {(_enableVerboseDiag && Core.DebugLogger.IsEnemyDiagEnabled ? "ON" : "OFF")}");
            sb.AppendLine("────────────────────────────────────────────────────────");

            // 列出前 10 隻活躍敵人的狀態
            int printCount = Mathf.Min(_enemies.Count, 10);
            if (printCount == 0)
            {
                sb.AppendLine("  （目前無已註冊敵人）");
            }
            else
            {
                sb.AppendLine($"  前 {printCount} 隻敵人詳細狀態：");
                for (int i = 0; i < printCount; i++)
                {
                    var e = _enemies[i];
                    if (e == null) { sb.AppendLine($"    [{i}] null"); continue; }
                    bool active = e.gameObject.activeInHierarchy;
                    sb.AppendLine($"    [{i}] {e.name}  active={active}  alive={e.IsAlive}  pos={(Vector2)e.transform.position}");
                }
                if (_enemies.Count > 10)
                    sb.AppendLine($"  ...（另有 {_enemies.Count - 10} 隻未列出）");
            }
            sb.AppendLine("════════════════════════════════════════════════════════");

            Debug.Log(sb.ToString());
        }
    }
}
