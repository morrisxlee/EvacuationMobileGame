using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Pathfinding;
using Pathfinding.RVO;

namespace SurvivalDemo.AI
{
    /// <summary>
    /// 敵人控制器：統一管理近戰/遠程敵人行為。
    /// 由 EnemyTickManager 分幀驅動 Tick()，避免同幀尖峰。
    /// 實作 IDamageable 與 IPoolable。
    ///
    /// 群體避讓架構：
    ///   A* Seeker           → 計算繞牆路徑（每 _repathInterval 秒非同步更新）
    ///   RVOController       → 全局最優局部避讓（Burst Job，每幀由 RVOSimulator 統一計算）
    ///                         搭配 RVOSimulator.useNavmeshAsObstacle + SetObstacleQuery（每幀）：
    ///                         讓 RVO Burst Job 感知 Grid Graph 邊界，不再將 Agent 推入牆壁
    ///   Rigidbody2D         → Kinematic velocity 驅動，Tick 間速度自動持續
    ///   指數平滑             → 防止 Tick 間 RVO 輸出方向瞬間反轉造成視覺抖動
    ///   constrainInsideGraph → 每 Tick 用 GetNearest(Walkable) 偵測是否進入不可行走區域，
    ///                         若有則直接位移回最近可行走點（對應官方 AIPath.constrainInsideGraph）
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Seeker))]
    [RequireComponent(typeof(RVOController))]
    public class EnemyController : MonoBehaviour, Combat.IDamageable, Pooling.IPoolable
    {
        [TitleGroup("敵人資料")]
        [Tooltip("敵人的 ScriptableObject 資料。由 SpawnManager 在運行時注入。")]
        [LabelText("敵人資料")]
        [ReadOnly]
        [SerializeField] private Data.EnemyData _enemyData;

        [TitleGroup("遠程敵人設定")]
        [Tooltip("遠程敵人發射投射物的位置。近戰敵人可留空。")]
        [LabelText("發射點")]
        [SerializeField] private Transform _firePoint;

        [TitleGroup("A* 尋路設定")]
        [Tooltip("多久重新計算一次路徑（秒）。數值越小路徑越精確但 CPU 消耗越高，建議 0.5~1.0。")]
        [LabelText("重新尋路間隔（秒）")]
        [Range(0.3f, 3f)]
        [SerializeField] private float _repathInterval = 0.75f;

        [TitleGroup("A* 尋路設定")]
        [Tooltip("抵達路徑點的判定距離（世界單位）。低於此距離時視為到達並前往下一個路徑點，建議 0.3~0.5。")]
        [LabelText("路徑點到達距離")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _waypointReachDistance = 0.4f;

        [TitleGroup("視線偵測設定")]
        [InfoBox("請在此勾選牆壁所在的 Layer（通常是 Default）。\n若未正確設定，遠程敵人將可穿牆射擊。\n⚠ 不要勾選 Enemy / Player / Projectile 等非障礙物 Layer，否則敵人會把隊友視為牆壁。")]
        [Tooltip("視線偵測（HasLineOfSight）所使用的障礙物 Layer Mask。\n勾選你的牆壁 Layer，通常是 Default Layer。\n此欄位是必要設定，未設定時執行時會輸出 LogError。\n注意：門阻擋邏輯已改由 DoorController 觸發區通知，此 Mask 不再用於門偵測。")]
        [LabelText("障礙物 Layer Mask")]
        [SerializeField] private LayerMask _losObstacleMask = 1; // 預設 Default Layer（第 0 層，bit 0 = 1）

        [TitleGroup("A* DoorTag 診斷")]
        [Tooltip("開啟後，定期輸出 DoorTag 讀取診斷（目前節點 Tag、與最近完整門距離、是否已綁定 _doorGate）。\n" +
                 "僅供定位「門有設 Tag 但敵人不停」問題。關閉可減少 log。")]
        [LabelText("啟用 DoorTag 診斷")]
        [SerializeField] private bool _enableDoorTagDiagnostics = false;

        [TitleGroup("A* DoorTag 診斷")]
        [Tooltip("DoorTag 診斷日誌輸出間隔（秒）。\n" +
                 "只在啟用 DoorTag 診斷時生效。建議 0.5~2。")]
        [LabelText("診斷輸出間隔（秒）")]
        [Min(0.1f)]
        [SerializeField] private float _doorTagDiagInterval = 1f;

        [TitleGroup("A* DoorTag 診斷")]
        [Tooltip("判定為「靠近門」的距離（世界單位）。\n" +
                 "若敵人在此距離內持續一段時間，卻從未讀到 DoorNodeTag，將輸出一次 LogError。")]
        [LabelText("靠近門判定距離")]
        [Min(0.5f)]
        [SerializeField] private float _doorTagNearDoorDistance = 2.5f;

        [TitleGroup("A* DoorTag 診斷")]
        [Tooltip("敵人靠近完整門且仍未讀到 DoorNodeTag 時，累積多久後輸出錯誤（秒）。\n" +
                 "此錯誤每次生成週期只打一次，避免 1000 敵人時 log 爆量。")]
        [LabelText("缺失警告延遲（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _doorTagMissingWarnDelay = 2f;

        // ── 運行時 ──
        private float _currentHP;
        private float _effectiveDamage;
        private float _effectiveMoveSpeed;
        private float _attackCooldown;
        private int _stageLevel;
        private string _poolId;
        private bool _isAlive;

        private Rigidbody2D _rb;
        private Transform _target; // 玩家
        private Feedback.FeedbackBridge _feedbackBridge;

        // ── RVO 局部避讓（替代原自製分離力）──
        // RVOController 沒有自己的 Update 迴圈，完全由 RVOSimulator（Burst Job）統一驅動。
        // 此處只負責每 Tick 呼叫 SetTarget()（設期望速度）並讀取 velocity（RVO 計算結果）。
        private RVOController _rvoController;

        // ── A* 尋路 ──
        private Seeker _seeker;
        private readonly List<Vector3> _waypoints = new List<Vector3>(32);
        private int _waypointIndex;
        private float _repathTimer;
        private bool _hasPath;

        // ── 靜態共用緩衝區（Tick 序列執行，不會衝突，避免 GC）──
        // ── LoS 用緩衝區：RaycastNonAlloc 跳過 trigger，零 GC ──
        private static readonly RaycastHit2D[] s_losBuffer = new RaycastHit2D[8];
        // ── 零摩擦材質：靜態，所有敵人共用一個實例，只建立一次，無 GC ──
        private static PhysicsMaterial2D s_zeroFriction;

        // ── 門交互（A* Tag 架構：由 FollowPath() 主動偵測，不依賴 Physics callback）──
        // FollowPath() 的 Step 0 中，踩到 DoorNodeTag 節點時呼叫 FindNearestIntact() 自設；
        // Tick() 的 else 分支中，每 Tick 自動偵測 IsIntact/PlayerInRange → 自清。
        private Interaction.DoorController _doorGate;

        // ── 診斷欄位（runtime only，不輸出到 Inspector）──
        // 每次從物件池生成時在 OnSpawnFromPool() 重置，確保每個生命週期只打一次
        private bool _diagFirstTickLogged;
        private bool _diagFirstPathLogged;
        private bool _diagPrevLosResult;
        private bool _diagLosInitialized;
        private float _diagStuckTimer;
        private float _diagStuckLogThrottle;
        private const float StuckThreshold   = 1.5f;
        private const float StuckLogCooldown = 5.0f;
        private float _stuckRecoveryTimer;
        private Vector2 _prevTickPosition;
        private const float StuckRecoveryInterval = 2.5f;
        private const float StuckRecoveryJump     = 1.5f;
        private const float StuckMoveThreshold    = 0.02f;

        // ── Dynamic RVO Priority（排隊行為）──
        // 超過此距離（世界單位）後，RVO priorityMultiplier 降至最低值 0.4f（後排自動讓路）
        private const float RvoPriorityFalloffDist = 10f;

        // ── RVO NavMesh Obstacle（constrainInsideGraph）──
        // 每 Tick 快取最近可行走節點，供 ApplyRVOObstacleQuery() 每幀使用（零 GetNearest 開銷的每幀呼叫）
        private GraphNode _cachedGraphNode;

        // ── 診斷：constrainInsideGraph 修正事件節流（EnemyDiag 模式，每次生成獨立計時）──
        private float _diagConstrainLogThrottle;
        private float _diagDoorTagLogThrottle;
        private float _diagDoorTagMissingTimer;
        private bool _diagDoorTagMissingLogged;

        // ── IDamageable ──
        public bool IsAlive => _isAlive;

        // ── 公開 ──
        public Data.EnemyData EnemyData => _enemyData;
        public string PoolId => _poolId;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.isKinematic    = true;  // Kinematic：移動由 rb.velocity 驅動，velocity 在 Tick 間自動持續
            _rb.gravityScale   = 0f;
            _rb.drag           = 0f;
            _rb.freezeRotation = true;
            _feedbackBridge = GetComponent<Feedback.FeedbackBridge>();
            _seeker = GetComponent<Seeker>();
            _rvoController = GetComponent<RVOController>();

            // 停止移動（攻擊、待機）時自動鎖定 RVO Agent：
            // 其他 Agent 仍會感知並繞開此 Agent，但不會推擠它。
            _rvoController.lockWhenNotMoving = true;

            // 零摩擦材質：解決 Tick 間隔期間被牆壁摩擦力磨停的卡牆問題。
            // 靜態建立，所有敵人共用同一個 PhysicsMaterial2D 實例，零 GC。
            if (s_zeroFriction == null)
                s_zeroFriction = new PhysicsMaterial2D("EnemyZeroFriction") { friction = 0f, bounciness = 0f };
            GetComponent<Collider2D>().sharedMaterial = s_zeroFriction;

            // 驗證障礙物遮罩是否有設定
            if (_losObstacleMask.value == 0)
                Core.DebugLogger.LogError(
                    $"敵人 Prefab '{gameObject.name}' 的『障礙物 Layer Mask』（_losObstacleMask）為空！\n" +
                    "請在 Inspector → 視線偵測設定 → 障礙物 Layer Mask 勾選牆壁所在的 Layer（通常是 Default）。\n" +
                    "若不設定，遠程敵人將可穿牆射擊。",
                    Core.LogCategory.AI);
        }

        /// <summary>
        /// 由 SpawnManager 在生成後呼叫。
        /// </summary>
        public void Init(Data.EnemyData data, int stageLevel, Transform target, string poolId)
        {
            _enemyData = data;
            _stageLevel = stageLevel;
            _target = target;
            _poolId = poolId;

            _currentHP = data.GetHPAtStage(stageLevel);
            _effectiveDamage = data.GetDamageAtStage(stageLevel);
            _effectiveMoveSpeed = data.MoveSpeed;
            _attackCooldown = 0f;
            _isAlive = true;

            // 驗證生成位置是否在 A* 可行走節點上
            if (AstarPath.active != null)
            {
                var info = AstarPath.active.GetNearest(transform.position);
                if (info.node == null)
                    Core.DebugLogger.LogError(
                        $"敵人 '{data.DisplayName}' 生成點 {transform.position} 找不到任何 A* 節點！請確認 Grid Graph 範圍涵蓋此位置。",
                        Core.LogCategory.AI);
                else if (!info.node.Walkable)
                    Core.DebugLogger.LogError(
                        $"敵人 '{data.DisplayName}' 生成點 {transform.position} 在 A* 不可行走節點上（牆壁/障礙物內）！請把 SpawnPoint 移離牆壁。",
                        Core.LogCategory.AI);
            }
        }

        /// <summary>
        /// 由 EnemyTickManager 每 tick 呼叫（非每幀）。
        /// deltaTime 為距上次 tick 的時間。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isAlive || _target == null) return;

            _attackCooldown -= deltaTime;

            // Kinematic 卡住偵測：量測本 Tick 相較「上一 Tick 結束時的位置」的實際位移量
            Vector2 curPos = transform.position;
            float movedSqrSinceLastTick = (curPos - _prevTickPosition).sqrMagnitude;
            _prevTickPosition = curPos;

            // ── 診斷：首次 Tick 快照（每次生成後只打一次）──
            if (!_diagFirstTickLogged && EnemyTickManager.VerboseDiagActive)
            {
                _diagFirstTickLogged = true;
                Core.DebugLogger.Log(
                    $"[EnemyDiag] 首次 Tick  {gameObject.name}\n" +
                    $"  位置={(Vector2)transform.position}  目標={(_target != null ? (Vector2)_target.position : Vector2.zero)}\n" +
                    $"  hasPath={_hasPath}  waypoints={_waypoints.Count}  速度設定={_effectiveMoveSpeed}\n" +
                    $"  AstarPath.active={AstarPath.active != null}  Seeker.IsDone={_seeker.IsDone()}\n" +
                    $"  RVOAgent={(_rvoController != null && _rvoController.rvoAgent != null ? "就緒 ✓" : "未就緒 ⚠（場景缺少 RVOSimulator？）")}",
                    Core.LogCategory.EnemyDiag);
            }

        // ── 門阻擋（A* Tag 架構：_doorGate 由 FollowPath() 踩上 DoorNodeTag 節點時自設）──
        // 此處的 if/else 邏輯不動：門完整且玩家不在 → 攻門；否則自清並重新尋路。
        if (_doorGate != null)
            {
                if (_doorGate.IsIntact && !_doorGate.PlayerInRange)
                {
                    // 門仍完整且玩家不在場 → 原地停止並持續攻擊門
                    if (_rvoController.rvoAgent != null) _rvoController.Move(Vector3.zero);
                    _rb.velocity        = Vector2.zero;
                    // 主動停止不算卡住，重置計時器，防止 return 前累積的殘值觸發傳送
                    _diagStuckTimer     = 0f;
                    _stuckRecoveryTimer = 0f;
                    if (_attackCooldown <= 0f)
                    {
                        _doorGate.TakeDamage(_effectiveDamage);
                        _attackCooldown = _enemyData.AttackInterval;
                    }
                    return;
                }
                else
                {
                    // 門已破壞（IsIntact=false）或玩家進入（PlayerInRange=true）→ 清除門引用，立即重新尋路
                    _doorGate    = null;
                    _repathTimer = 0f;
                }
            }

            float distSqr = ((Vector2)(_target.position - transform.position)).sqrMagnitude;
            float atkRangeSqr = _enemyData.AttackRange * _enemyData.AttackRange;

            // ── 第二層：Dynamic RVO Priority（創造排隊行為）──
            // 靠近玩家 → priorityMultiplier 高（2.0）：RVO 前排優先通行
            // 遠離玩家 → priorityMultiplier 低（0.4）：後排自動 yield，不強推前排
            // UpdateAgentProperties() 中：rvoAgent.Priority = priority × priorityMultiplier
            // priorityMultiplier 是 [NonSerialized] 運行時欄位，不影響 Inspector 設定值
            float dist = Mathf.Sqrt(distSqr);
            _rvoController.priorityMultiplier = Mathf.Lerp(2.0f, 0.4f, Mathf.Clamp01(dist / RvoPriorityFalloffDist));

            // ── LoS 評估（含狀態轉換診斷）──
            bool inAttackRange = distSqr <= atkRangeSqr;
            bool los = inAttackRange && HasLineOfSight();

            // 診斷：LoS 狀態轉換（blocked → clear 或 clear → blocked，只在 EnemyDiag 開啟時追蹤）
            if (inAttackRange && EnemyTickManager.VerboseDiagActive)
            {
                if (!_diagLosInitialized)
                {
                    _diagLosInitialized = true;
                    _diagPrevLosResult = los;
                }
                else if (los != _diagPrevLosResult)
                {
                    _diagPrevLosResult = los;
                    string blocker = "暢通";
                    if (!los)
                    {
                        Vector2 dOrigin = transform.position;
                        Vector2 dDest   = _target.position;
                        Vector2 dDir    = dDest - dOrigin;
                        float   dDist   = dDir.magnitude;
                        int     dHits   = dDist > 0.001f
                            ? Physics2D.RaycastNonAlloc(dOrigin, dDir / dDist, s_losBuffer, dDist, _losObstacleMask)
                            : 0;
                        blocker = "偵測結果不一致（可能是 Mask 為空）";
                        for (int d = 0; d < dHits; d++)
                        {
                            var dc = s_losBuffer[d].collider;
                            if (dc != null && !dc.isTrigger)
                            {
                                blocker = $"被 '{dc.gameObject.name}'（Layer={LayerMask.LayerToName(dc.gameObject.layer)}）阻擋";
                                break;
                            }
                        }
                    }
                    Core.DebugLogger.Log(
                        $"[EnemyDiag] LoS 狀態轉換  {gameObject.name}\n" +
                        $"  {(_diagPrevLosResult ? "✗ 遮擋 → ✓ 暢通" : "✓ 暢通 → ✗ 遮擋")}  狀況：{blocker}\n" +
                        $"  losObstacleMask={_losObstacleMask.value}  距目標={(Vector2)(_target.position - transform.position)}",
                        Core.LogCategory.EnemyDiag);
                }
            }

            if (los)
            {
                // 在攻擊範圍內且視線暢通 → 停止移動並攻擊
                // 告知 RVO 此 Agent 已停止，其他 Agent 將自然繞行（lockWhenNotMoving 已在 Awake 開啟）
                if (_rvoController.rvoAgent != null) _rvoController.Move(Vector3.zero);
                _rb.velocity        = Vector2.zero;
                _diagStuckTimer     = 0f;
                _stuckRecoveryTimer = 0f;
                if (_attackCooldown <= 0f)
                {
                    Attack();
                    _attackCooldown = _enemyData.AttackInterval;
                }
            }
            else
            {
                // 更新尋路計時器
                _repathTimer -= deltaTime;
                if (_repathTimer <= 0f)
                {
                    RequestPath();
                    _repathTimer = _repathInterval;
                }

                FollowPath(deltaTime);
                EvaluateDoorTagDiagnostics(deltaTime);

                // ── 卡住偵測（Kinematic 模式：以實際位移量取代速度量測）──
                _diagStuckLogThrottle -= deltaTime;

                float stuckThreshSqr = StuckMoveThreshold * StuckMoveThreshold;

                // RVO 主動給零速 = 敵人被人群（門口停止的敵人）堵住，屬於「刻意等待」而非卡牆。
                // 舊：固定閾值 StuckMoveThreshold（0.02 m/s）— 太緊，RVO 自然剎車速度
                //     0.05~0.15 m/s 都會誤觸，導致隊伍後排被誤判傳送。
                // 新：相對於敵人移速的 20%（預設 3 m/s → 0.6 m/s 以下才視為刻意等待），
                //     與敵人速度設定自動匹配，不會因 EnemyData.MoveSpeed 變動而失效。
                float rvoStalledThresh = _effectiveMoveSpeed * 0.20f;
                bool rvoIntentionallyStalled =
                    _rvoController.rvoAgent != null &&
                    ((Vector2)_rvoController.velocity).sqrMagnitude < (rvoStalledThresh * rvoStalledThresh);

                if (!rvoIntentionallyStalled && movedSqrSinceLastTick < stuckThreshSqr && distSqr > atkRangeSqr)
                {
                    _diagStuckTimer     += deltaTime;
                    _stuckRecoveryTimer += deltaTime;

                    // 自動恢復：每 StuckRecoveryInterval 秒直接推移位置，脫離死角並重新尋路
                    if (_stuckRecoveryTimer >= StuckRecoveryInterval)
                    {
                        _stuckRecoveryTimer = 0f;
                        Vector2 toTarget  = ((Vector2)_target.position - curPos).normalized;
                        Vector2 randDir   = Random.insideUnitCircle.normalized;
                        Vector2 jumpDir   = (toTarget * 0.6f + randDir * 0.4f).normalized;
                        _rb.MovePosition(curPos + jumpDir * StuckRecoveryJump);
                        _repathTimer = 0f; // 立即觸發重新尋路，確保脫困後路徑正確
                    }

                    if (_diagStuckTimer >= StuckThreshold && _diagStuckLogThrottle <= 0f)
                    {
                        _diagStuckLogThrottle = StuckLogCooldown;
                        _diagStuckTimer = 0f;

                        Core.DebugLogger.LogError(
                            $"敵人卡住：{gameObject.name} 已停滯 {StuckThreshold}s（已觸發位置推移脫困）。\n" +
                            $"  位置={curPos}  目標距離={Mathf.Sqrt(distSqr):F2}\n" +
                            $"  hasPath={_hasPath}  waypoints={_waypoints.Count}  waypointIndex={_waypointIndex}\n" +
                            $"  可能根因：\n" +
                            $"    1) A* Graph 解析度不足，路徑終點在不可行走節點旁\n" +
                            $"    2) SpawnPoint 太靠近牆壁（生成在不可行走節點附近）\n" +
                            $"    3) _repathInterval 過大（當前={_repathInterval}s），路徑更新太慢\n" +
                            $"    4) 目標在完全封閉區域（A* 找不到有效路徑）",
                            Core.LogCategory.AI);
                    }
                }
                else
                {
                    _diagStuckTimer     = 0f;
                    _stuckRecoveryTimer = 0f;
                }

                // A* Tag 架構：門阻擋偵測已整合至 FollowPath() Step 0，此處無需額外處理。
            }
        }

        /// <summary>
        /// DoorTag 讀取診斷（可切換）。只在除錯時啟用，避免大量敵人時 log 負擔。
        /// 目的：快速確認敵人是否真的踩在 DoorNodeTag 節點，並與最近完整門的距離關聯。
        /// </summary>
        private void EvaluateDoorTagDiagnostics(float deltaTime)
        {
            if (!_enableDoorTagDiagnostics) return;

            _diagDoorTagLogThrottle -= deltaTime;

            var nearestDoor = Interaction.DoorController.FindNearestIntact((Vector2)transform.position, float.PositiveInfinity);
            float nearestDoorDist = nearestDoor != null
                ? Vector2.Distance((Vector2)transform.position, (Vector2)nearestDoor.transform.position)
                : float.PositiveInfinity;

            if (_diagDoorTagLogThrottle <= 0f)
            {
                _diagDoorTagLogThrottle = _doorTagDiagInterval;
                string nodeTag = _cachedGraphNode != null ? _cachedGraphNode.Tag.ToString() : "null";
                string doorName = nearestDoor != null ? nearestDoor.name : "none";
                string doorDist = nearestDoor != null ? nearestDoorDist.ToString("F2") : "INF";
                Core.DebugLogger.Log(
                    $"[DoorTagDiag] {gameObject.name}\n" +
                    $"  nodeTag={nodeTag}  expected={Interaction.DoorController.DoorNodeTag}\n" +
                    $"  doorGate={(_doorGate != null ? _doorGate.name : "null")}  nearestDoor={doorName}  nearestDoorDist={doorDist}",
                    Core.LogCategory.AI);
            }

            bool nearIntactDoor = nearestDoor != null && nearestDoorDist <= _doorTagNearDoorDistance;
            bool seesDoorTag = _cachedGraphNode != null && _cachedGraphNode.Tag == Interaction.DoorController.DoorNodeTag;
            if (nearIntactDoor && _doorGate == null && !seesDoorTag)
            {
                _diagDoorTagMissingTimer += deltaTime;
                if (!_diagDoorTagMissingLogged && _diagDoorTagMissingTimer >= _doorTagMissingWarnDelay)
                {
                    _diagDoorTagMissingLogged = true;
                    Core.DebugLogger.LogError(
                        $"敵人 '{gameObject.name}' 靠近完整門 '{nearestDoor.name}' {nearestDoorDist:F2} 單位，" +
                        $"持續 {_doorTagMissingWarnDelay:F1}s 仍未讀到 DoorNodeTag。\n" +
                        $"  目前 nodeTag={(_cachedGraphNode != null ? _cachedGraphNode.Tag.ToString() : "null")}\n" +
                        $"  請檢查：\n" +
                        $"    1) DoorController 的 Tag 更新 bounds 是否覆蓋敵人實際路徑\n" +
                        $"    2) Grid Graph node size / rotation 是否造成門區節點偏移\n" +
                        $"    3) Tag 1 是否被其他系統覆寫",
                        Core.LogCategory.AI);
                }
            }
            else
            {
                _diagDoorTagMissingTimer = 0f;
            }
        }

        private void RequestPath()
        {
            if (AstarPath.active == null)
            {
                Core.DebugLogger.LogError(
                    "場景中沒有 AstarPath 元件！A* 尋路無法運作。請在場景中新增 AstarPath 物件並設定 Grid Graph。",
                    Core.LogCategory.AI);
                return;
            }

            if (!_seeker.IsDone()) return;

            _seeker.StartPath(transform.position, _target.position, OnPathFound);
        }

        private void OnPathFound(Path p)
        {
            if (!_isAlive) return;

            if (p.error)
            {
                Core.DebugLogger.LogError(
                    $"敵人 '{_enemyData?.DisplayName}' 路徑計算失敗：{p.errorLog}",
                    Core.LogCategory.AI);
                _hasPath = false;
                return;
            }

            _waypoints.Clear();
            _waypoints.AddRange(p.vectorPath);
            _waypointIndex = 0;
            _hasPath = true;

            if (!_diagFirstPathLogged && EnemyTickManager.VerboseDiagActive)
            {
                _diagFirstPathLogged = true;
                Core.DebugLogger.Log(
                    $"[EnemyDiag] 首次路徑成功  {gameObject.name}\n" +
                    $"  路徑節點數={p.vectorPath.Count}  起點={(Vector2)p.vectorPath[0]}  終點={(Vector2)p.vectorPath[p.vectorPath.Count - 1]}\n" +
                    $"  路徑長度≈{p.GetTotalLength():F2}  目標物件={(_target != null ? _target.name : "null")}",
                    Core.LogCategory.EnemyDiag);
            }
        }

        /// <summary>
        /// 沿 A* 路徑移動，並透過 RVOController 實現群體局部避讓。
        ///
        /// 流程：
        ///   0. constrainInsideGraph：GetNearest(Walkable) 快取當前節點 + SetObstacleQuery（供每幀呼叫）
        ///      若位置已偏出可行走區域（Tick 間隔中被推入牆），直接位移回最近可行走點並修正速度分量
        ///   1. 從 A* 路徑取得下一個目標點（waypointTarget）
        ///   2. 呼叫 SetTarget()：告知 RVOSimulator 期望去哪、期望速度、最終目的地
        ///   3. RVOSimulator（Burst Job）在每幀全局計算所有 Agent 的最優避讓速度
        ///      搭配 useNavmeshAsObstacle = true，RVO 計算時自動感知 Grid Graph 邊界，不往牆方向推
        ///   4. 讀取 rvoController.velocity（RVO 輸出），加上指數平滑後套用到 rb.velocity
        ///
        /// 指數平滑係數 = 1 - exp(-15 * deltaTime)：
        ///   - 與 Tick 頻率無關（自動適應近/遠距批次不同的 dt）
        ///   - 防止 Tick 間因路徑更新或 RVO 結果變化造成的速度方向瞬間反轉（視覺抖動根因）
        /// </summary>
        private void FollowPath(float deltaTime)
        {
            // rvoAgent 在場景缺少 RVOSimulator 時為 null（RVOController.OnEnable 已輸出詳細錯誤）
            if (_rvoController.rvoAgent == null) return;

            // ── Step 0：constrainInsideGraph + RVO Obstacle Query（對應官方 AIPath.constrainInsideGraph）──
            // 取得最近可行走節點：
            //   • 快取至 _cachedGraphNode，供 EnemyTickManager 每幀呼叫 ApplyRVOObstacleQuery() 使用
            //     （SetObstacleQuery 本身只是一個整數賦值，可安全每幀呼叫，零 GetNearest 開銷）
            //   • 若位置已偏出可行走區域（Tick 間隔期間被 RVO 推力或 StuckRecovery 推入牆中），
            //     diff > 0 → 直接位移回最近可行走點，並移除指向牆方向的速度分量
            // 需要 RVOSimulator.useNavmeshAsObstacle = true（Inspector 設定）才能讓 RVO Burst Job
            // 利用此節點資訊感知 Grid Graph 邊界，從根本上阻止 RVO 將 Agent 推向牆壁。
            if (AstarPath.active != null)
            {
                var nearestInfo = AstarPath.active.GetNearest(transform.position, NNConstraint.Walkable);
                if (nearestInfo.node != null)
                {
                    _cachedGraphNode = nearestInfo.node;
                    _rvoController.SetObstacleQuery(_cachedGraphNode);

                    // ── A* Tag 門偵測（效能關鍵設計）──
                    // 條件：尚未設定 _doorGate 且當前最近節點被標記為 DoorNodeTag。
                    // 這意味著只有在「首次踩上門節點時」才執行 FindNearestIntact()，
                    // 不會每 Tick 都查詢，1000+ 敵人場景下總開銷 = 門數 × 踩上頻率，極低。
                    // searchRadiusSqr = doorRadius²（預設 2 單位，由 DoorController._doorDetectRadius 決定）
                    if (_doorGate == null &&
                        _cachedGraphNode.Tag == Interaction.DoorController.DoorNodeTag)
                    {
                        var door = Interaction.DoorController.FindNearestIntact((Vector2)transform.position);
                        if (door != null)
                        {
                            _doorGate = door;
                            Core.DebugLogger.Log(
                                $"[DoorTag] {gameObject.name} 踩上 DoorNodeTag 節點，自設 _doorGate='{door.name}'。",
                                Core.LogCategory.AI);
                        }
                        else
                        {
                            // 節點有 Tag 但找不到完整的門（門剛被破壞 or 搜尋半徑不足）
                            Core.DebugLogger.LogError(
                                $"敵人 '{gameObject.name}' 踩上 DoorNodeTag 節點，但找不到任何在偵測半徑內的完整門！\n" +
                                $"  敵人位置={(Vector2)transform.position}\n" +
                                $"  可能原因：\n" +
                                $"    1) DoorController._doorDetectRadius 設定過小，增大至門寬 + 0.5 單位\n" +
                                $"    2) 門節點 Tag 未即時清除（UpdateNodeTags(false) 尚未生效，A* 更新有延遲）\n" +
                                $"    3) 門的 transform.position 與觸發區中心偏差過大，調整門 Pivot 位置",
                                Core.LogCategory.AI);
                        }
                    }

                    Vector2 diff = (Vector2)nearestInfo.position - (Vector2)transform.position;
                    if (diff.sqrMagnitude > 0.0001f)
                    {
                        _rb.position += diff;
                        float dot = Vector2.Dot(_rb.velocity, diff.normalized);
                        if (dot < 0f) _rb.velocity -= dot * diff.normalized;

                        _diagConstrainLogThrottle -= deltaTime;
                        if (EnemyTickManager.VerboseDiagActive && _diagConstrainLogThrottle <= 0f)
                        {
                            _diagConstrainLogThrottle = 2.0f;
                            Core.DebugLogger.Log(
                                $"[EnemyDiag] ConstrainGraph 修正  {gameObject.name}\n" +
                                $"  偏移={diff}（{diff.magnitude:F3} 單位）  修正後位置={(Vector2)_rb.position}",
                                Core.LogCategory.EnemyDiag);
                        }
                    }
                }
            }

            // ── Step 1：路徑點追蹤 ──
            Vector2 waypointTarget;

            if (_hasPath && _waypoints.Count > 0)
            {
                float reachSqr = _waypointReachDistance * _waypointReachDistance;

                // 跳過已到達的路徑點
                while (_waypointIndex < _waypoints.Count - 1)
                {
                    if (((Vector2)(_waypoints[_waypointIndex] - transform.position)).sqrMagnitude < reachSqr)
                        _waypointIndex++;
                    else
                        break;
                }

                int idx = Mathf.Min(_waypointIndex, _waypoints.Count - 1);
                waypointTarget = (Vector2)_waypoints[idx];
            }
            else
            {
                // 路徑尚未計算完成（初始短暫狀態）：直線移向目標
                waypointTarget = (Vector2)_target.position;
            }

            // ── Step 2：通知 RVO 期望目標 ──
            // endOfPath = 最終目的地（玩家位置）：讓 RVO 偵測大量 Agent 擁擠同目標的情況，
            // 自動提升 FlowFollowingStrength，使後排敵人自然繞流而非死頂前排。
            // maxSpeed = speed * 1.2f：允許 RVO 在避讓機動時短暫加速（例如繞開旁邊的敵人）。
            var waypointTarget3D = new Vector3(waypointTarget.x, waypointTarget.y, 0f);
            var endOfPath3D      = new Vector3(_target.position.x, _target.position.y, 0f);
            _rvoController.SetTarget(waypointTarget3D, _effectiveMoveSpeed, _effectiveMoveSpeed * 1.2f, endOfPath3D);

            // ── Step 3-4：讀取 RVO 輸出並套用指數平滑 ──
            // velocity getter 內部使用 Time.deltaTime（RVO 每幀運算），與我們的 Tick dt 無關，永遠正確。
            Vector2 rvoVelocity  = (Vector2)_rvoController.velocity;
            float   smoothFactor = 1f - Mathf.Exp(-15f * deltaTime);
            _rb.velocity = Vector2.Lerp(_rb.velocity, rvoVelocity, smoothFactor);
        }

        /// <summary>
        /// 每幀由 EnemyTickManager 呼叫，維持 RVO NavMesh Obstacle 偵測持續有效。
        ///
        /// 背景：RVOController.SetObstacleQuery 必須每幀（每次 RVO 模擬步驟前）呼叫才有效，
        /// 因為 UpdateAgentProperties 讀取後會清除 obstacleQuery（設為 null）。
        /// 此方法只執行一個整數賦值（_rvoController 內部的 obstacleQuery = node），幾乎零開銷。
        /// 節點由 FollowPath() 每 Tick 透過 GetNearest(Walkable) 更新快取。
        ///
        /// 需要 RVOSimulator.useNavmeshAsObstacle = true（Inspector 設定）才能讓 RVO Burst Job
        /// 將此節點轉化為牆壁邊界感知。
        /// </summary>
        public void ApplyRVOObstacleQuery()
        {
            if (!_isAlive || _rvoController.rvoAgent == null || _cachedGraphNode == null) return;
            _rvoController.SetObstacleQuery(_cachedGraphNode);
        }

        private void Attack()
        {
            if (_enemyData.Type == Data.EnemyType.Melee)
            {
                var playerStats = _target.GetComponent<Player.PlayerStats>();
                if (playerStats != null)
                    playerStats.TakeDamage(_effectiveDamage);
            }
            else
            {
                FireProjectile();
            }
        }

        private void FireProjectile()
        {
            var pool = Pooling.GenericPool.Instance;
            if (pool == null || string.IsNullOrEmpty(_enemyData.ProjectilePoolId))
            {
                Core.DebugLogger.LogError($"敵人 '{_enemyData.DisplayName}' 無法發射投射物：pool 或 projectilePoolId 無效。", Core.LogCategory.AI);
                return;
            }

            Vector3 spawnPos = _firePoint != null ? _firePoint.position : transform.position;
            Vector2 dir = ((Vector2)(_target.position - spawnPos)).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var obj = pool.Spawn(_enemyData.ProjectilePoolId, spawnPos, Quaternion.Euler(0, 0, angle));
            if (obj == null) return;

            var projectile = obj.GetComponent<Combat.Projectile>();
            if (projectile != null)
                projectile.Init(dir, _enemyData.ProjectileSpeed, _effectiveDamage, _enemyData.ProjectilePoolId);
        }

        // ── 視線偵測 ──
        /// <summary>
        /// 從敵人到目標之間是否有非 Trigger 牆壁阻擋。
        /// 使用 RaycastNonAlloc + 手動跳過 isTrigger=true 的碰撞體。零 GC，只在攻擊距離內呼叫。
        /// </summary>
        private bool HasLineOfSight()
        {
            Vector2 origin = transform.position;
            Vector2 dest   = _target.position;
            Vector2 dir    = dest - origin;
            float   dist   = dir.magnitude;
            if (dist < 0.001f) return true;

            int hitCount = Physics2D.RaycastNonAlloc(origin, dir / dist, s_losBuffer, dist, _losObstacleMask);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_losBuffer[i].collider;
                if (col != null && !col.isTrigger)
                    return false;
            }
            return true;
        }

        // ══════════════════════════════════════
        //  IDamageable
        // ══════════════════════════════════════

        public void TakeDamage(float damage)
        {
            if (!_isAlive) return;
            _currentHP -= damage;
            _feedbackBridge?.PlayHit();

            if (_currentHP <= 0f)
                Die();
        }

        private void Die()
        {
            _isAlive = false;
            _rb.velocity = Vector2.zero;
            _feedbackBridge?.PlayDeath();
            Core.GameEvents.FireEnemyKilled(gameObject.GetInstanceID());

            var pool = Pooling.GenericPool.Instance;
            if (pool != null && !string.IsNullOrEmpty(_poolId))
                pool.Despawn(_poolId, gameObject);
            else
                gameObject.SetActive(false);
        }

        // ══════════════════════════════════════
        //  IPoolable
        // ══════════════════════════════════════

        public void OnSpawnFromPool()
        {
            _isAlive        = true;
            _attackCooldown = 0f;
            _doorGate       = null;
            _repathTimer    = 0f; // 生成後立即觸發第一次尋路
            _waypoints.Clear();
            _waypointIndex = 0;
            _hasPath = false;

            // 重置所有 per-spawn 診斷狀態
            _diagFirstTickLogged      = false;
            _diagFirstPathLogged      = false;
            _diagPrevLosResult        = false;
            _diagLosInitialized       = false;
            _diagStuckTimer           = 0f;
            _diagStuckLogThrottle     = 0f;
            _diagConstrainLogThrottle = 0f;
            _diagDoorTagLogThrottle   = 0f;
            _diagDoorTagMissingTimer  = 0f;
            _diagDoorTagMissingLogged = false;
            _stuckRecoveryTimer       = 0f;
            _prevTickPosition         = transform.position; // 以當前生成位置為基準，防止首 Tick 誤判卡住
            _cachedGraphNode          = null; // 重置快取，生成後首次 FollowPath 觸發時重新取得
            // RVOController 由 Unity 的 OnEnable/OnDisable 生命週期管理（隨 SetActive 自動注冊/移除）
        }

        public void OnReturnToPool()
        {
            _isAlive = false;
            _rb.velocity = Vector2.zero;
            // 回收時通知 RVO 此 Agent 速度歸零（OnDisable 會移除 Agent，此呼叫確保最後一幀正確）
            if (_rvoController != null && _rvoController.rvoAgent != null)
                _rvoController.Move(Vector3.zero);
            _doorGate = null;
            _target   = null;
            _waypoints.Clear();
            _waypointIndex = 0;
            _hasPath = false;
        }
    }
}
