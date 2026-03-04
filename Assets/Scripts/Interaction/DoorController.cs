using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using SurvivalDemo.Feedback;
using Pathfinding;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 門控制器（A* Tag 架構）：
    ///   - 不再使用實體阻擋碰撞器，敵人路徑永遠穿過門所在節點。
    ///   - 改用 A* GraphUpdateObject 將門節點標記為 Tag=DoorNodeTag，
    ///     讓 EnemyController.FollowPath() 每 Tick 主動偵測並自設 _doorGate。
    ///   - 觸發區（DoorSensor）只偵測玩家，負責修補計時與 Sprite 切換。
    ///   - 敵人完全自主管理 _doorGate，不依賴任何 Physics callback。
    ///
    /// ⚠ Unity 場景一次性設定（必做）：
    ///   1) Edit → Project Settings → Tags and Layers → 新增 Layer 'DoorSensor'
    ///   2) 將 _triggerZone 子物件的 Layer 設為 DoorSensor
    ///   3) Physics 2D Layer Collision Matrix：DoorSensor ↔ Player 啟用（敵人不需要）
    ///   4) A* Inspector → Graph → Tag Names：Tag 1 命名為 "Door"（僅供辨識，不影響邏輯）
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class DoorController : MonoBehaviour, Combat.IDamageable
    {
        // ══════════════════════════════════════
        //  A* Node Tag（全場景共用常數）
        // ══════════════════════════════════════

        /// <summary>
        /// 門節點的 A* Tag 值。所有 IsIntact 的門在圖中對應的節點都標記此 Tag。
        /// EnemyController.FollowPath() 讀取 _cachedGraphNode.Tag 判斷是否踏上門節點。
        /// </summary>
        public const uint DoorNodeTag = 1u;

        // 靜態門列表，供 FindNearestIntact() 查詢。門數量通常 < 20，O(n) 完全可接受。
        // Start() 注冊、OnDestroy() 移除，確保列表始終只含存活中的門。
        private static readonly List<DoorController> s_allDoors = new List<DoorController>();

        // ══════════════════════════════════════
        //  Inspector 欄位
        // ══════════════════════════════════════

        [TitleGroup("門數值")]
        [Tooltip("門的最大耐久值。數值越高門越耐打，敵人需要更多次攻擊才能破壞。")]
        [LabelText("最大 HP")]
        [Min(1f)]
        [SerializeField] private float _maxHP = 50f;

        [TitleGroup("門數值")]
        [Tooltip("玩家每次自動修補時回復的 HP 量。例如設 10 配合 5 次修補即可從 0 回滿 50HP。")]
        [LabelText("每次修補量")]
        [Min(1f)]
        [SerializeField] private float _repairPerTick = 10f;

        [TitleGroup("門數值")]
        [Tooltip("玩家站在觸發區內時，每隔多久自動修補一次（秒）。建議 0.5~2.0。\n數值越小修補越快，建議配合門的 MaxHP 調整平衡。")]
        [LabelText("修補間隔（秒）")]
        [Min(0.1f)]
        [SerializeField] private float _repairInterval = 1f;

        [TitleGroup("A* Tag 設定")]
        [InfoBox("⚠ 新架構說明：門阻擋改用 A* Node Tag（Tag 1）實現。\n" +
                 "門完整時，觸發區對應的 Grid 節點被標記為 DoorNodeTag，\n" +
                 "EnemyController 踩到此節點時自動設定 _doorGate 停下攻擊。\n" +
                 "門被破壞或修好時，UpdateNodeTags() 自動同步更新圖。")]
        [Tooltip("敵人偵測門時的搜尋半徑（世界單位）。預設 2 單位，覆蓋門寬即可。\n" +
                 "半徑過大可能誤認相鄰的門，過小可能在門邊緣踩上節點時找不到。")]
        [LabelText("門偵測搜尋半徑（單位）")]
        [Range(0.5f, 5f)]
        [SerializeField] private float _doorDetectRadius = 2f;

        [TitleGroup("A* Tag 設定")]
        [Tooltip("更新門節點 Tag 時，對 _triggerZone.bounds 在 X/Y 方向額外擴張的距離（世界單位）。\n" +
                 "用途：避免觸發區邊界剛好落在 Grid node 邊緣，導致節點漏標記。\n" +
                 "建議 0.1~0.5；數值越大涵蓋越保守，但也會影響鄰近通道節點。")]
        [LabelText("Tag 範圍 XY 擴張")]
        [Min(0f)]
        [SerializeField] private float _tagBoundsPaddingXY = 0.2f;

        [TitleGroup("A* Tag 設定")]
        [Tooltip("更新門節點 Tag 時，對 bounds 的 Z 厚度補償（世界單位）。\n" +
                 "在 2D 專案中常見 Z 厚度過薄，可能造成 GraphUpdateObject 未命中節點。\n" +
                 "建議 1~4。")]
        [LabelText("Tag 範圍 Z 厚度補償")]
        [Min(0f)]
        [SerializeField] private float _tagBoundsPaddingZ = 2f;

        [TitleGroup("A* Tag 設定")]
        [Tooltip("勾選後，門狀態轉換（修好/破壞）時會立即 FlushGraphUpdates()。\n" +
                 "用途：強制同一幀完成 Tag 套用，方便除錯「剛修好但敵人尚未讀到 Tag」的時序問題。\n" +
                 "大量門同幀切換時可能增加尖峰，建議先開啟驗證，穩定後可關閉。")]
        [LabelText("狀態轉換時立即 Flush Graph")]
        [SerializeField] private bool _flushGraphUpdatesOnStateChange = true;

        [TitleGroup("A* Tag 設定")]
        [Tooltip("勾選後輸出 DoorTag 詳細診斷日誌（寫入範圍、最近節點 Tag 驗證結果）。\n" +
                 "關閉時仍保留 LogError，不會靜默忽略錯誤。")]
        [LabelText("輸出 DoorTag 診斷日誌")]
        [SerializeField] private bool _enableDoorTagDiagnostics = true;

        [TitleGroup("碰撞設定")]
        [InfoBox("⚠ 新架構：觸發區現在只偵測玩家，不再需要偵測敵人。\n" +
                 "Physics 2D Layer Collision Matrix 只需啟用 DoorSensor ↔ Player。\n" +
                 "執行時 Start() 會自動驗證 Layer 設定並輸出詳細錯誤日誌。")]
        [Tooltip("偵測玩家進出的觸發感應區。\n" +
                 "⚠ Is Trigger 必須勾選，且所在 GameObject Layer 必須是 DoorSensor。\n" +
                 "玩家進入：開始自動修補，顯示開門 Sprite。\n" +
                 "新架構下不再用於偵測敵人。")]
        [LabelText("觸發感應區（Is Trigger）")]
        [SerializeField] private Collider2D _triggerZone;

        [TitleGroup("Sprite 切換")]
        [Tooltip("門的 SpriteRenderer，用於根據狀態切換外觀。可留空（不影響門邏輯）。")]
        [LabelText("Sprite 渲染器")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [TitleGroup("Sprite 切換")]
        [Tooltip("門完整且玩家不在附近時顯示的 Sprite（正常關閉、阻擋敵人狀態）。")]
        [LabelText("關閉 Sprite")]
        [SerializeField] private Sprite _closedSprite;

        [TitleGroup("Sprite 切換")]
        [Tooltip("玩家進入觸發區時顯示的 Sprite（玩家通行中，敵人也可通行）。")]
        [LabelText("開啟 Sprite")]
        [SerializeField] private Sprite _openSprite;

        [TitleGroup("Sprite 切換")]
        [Tooltip("門 HP 歸零被破壞後顯示的 Sprite（殘骸狀態，敵人可自由通行）。")]
        [LabelText("破壞 Sprite")]
        [SerializeField] private Sprite _destroyedSprite;

        [TitleGroup("MMF 音效")]
        [Tooltip("玩家每次修補門時觸發一次（建議輕微修補音效 / 閃光粒子）。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("修補 Tick (OnDoorRepairTick)")]
        [SerializeField] private FeedbackSlot _onDoorRepairTick;

        [TitleGroup("MMF 音效")]
        [Tooltip("門從 0 HP 修到 > 0 HP 的瞬間觸發一次（門重啟/復活瞬間音效）。\n" +
                 "只在 0 -> >0 的狀態轉換觸發，不會在後續持續修補中重複播放。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("修補完成瞬間 (OnDoorPatched)")]
        [SerializeField] private FeedbackSlot _onDoorPatched;

        [TitleGroup("MMF 音效")]
        [Tooltip("門被敵人攻擊一次時觸發（受擊音效 / 門框顫動）。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("受傷 (OnDoorDamaged)")]
        [SerializeField] private FeedbackSlot _onDoorDamaged;

        [TitleGroup("MMF 音效")]
        [Tooltip("門 HP 歸零被破壞時觸發（爆炸 / 崩塌音效特效）。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("破壞 (OnDoorDestroyed)")]
        [SerializeField] private FeedbackSlot _onDoorDestroyed;

        [TitleGroup("MMF 音效")]
        [Tooltip("門切換為「開啟」視覺狀態時觸發（開門滑動音效）。\n" +
                 "新架構下改在 UpdateDoorState() 狀態轉換時觸發，確保不論玩家站在門內修好，\n" +
                 "或門修好後玩家剛好在場，音效都能正確播放一次。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("開門 (OnDoorOpen)")]
        [SerializeField] private FeedbackSlot _onDoorOpen;

        [TitleGroup("MMF 音效")]
        [Tooltip("門切換為「關閉」視覺狀態時觸發（關門上鎖音效）。\n" +
                 "新架構下改在 UpdateDoorState() 狀態轉換時觸發，確保玩家離開後才播放。\n" +
                 "門已破壞時玩家離開不播放（無門可關）。拖入對應 MMF_Player 物件，可留空。")]
        [LabelText("關門 (OnDoorClose)")]
        [SerializeField] private FeedbackSlot _onDoorClose;

        // ══════════════════════════════════════
        //  運行時私有欄位
        // ══════════════════════════════════════

        private float _currentHP;
        private bool _playerInRange;
        private float _repairTimer;
        private int _doorId;

        // 追蹤上一幀視覺開/關狀態，確保音效只在狀態轉換時觸發一次，避免重複播放。
        private bool _wasShowingOpenSprite;

        // ══════════════════════════════════════
        //  公開屬性
        // ══════════════════════════════════════

        public bool IsIntact   => _currentHP > 0f;
        public bool IsFullHP   => _currentHP >= _maxHP;
        public float CurrentHP => _currentHP;
        public float MaxHP     => _maxHP;
        public float HPRatio   => _maxHP > 0f ? _currentHP / _maxHP : 0f;
        public bool IsAlive    => IsIntact; // IDamageable

        /// <summary>玩家目前是否在觸發感應區內。供 EnemyController.Tick() 的 _doorGate 釋放條件使用。</summary>
        public bool PlayerInRange => _playerInRange;

        // ══════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════

        private void Start()
        {
            _doorId    = gameObject.GetInstanceID();
            _currentHP = 0f; // 門初始不可用（需玩家站在觸發區修補）
            s_allDoors.Add(this);
            ValidateDoorSensorLayer();
            UpdateDoorState();
        }

        private void OnDestroy()
        {
            s_allDoors.Remove(this);
            // 門物件銷毀時清除 A* Tag，讓圖恢復乾淨狀態
            UpdateNodeTags(false);
        }

        private void Update()
        {
            if (!_playerInRange || IsFullHP) return;

            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (state != Core.GameState.Playing && state != Core.GameState.Evacuation) return;

            _repairTimer -= Time.deltaTime;
            if (_repairTimer <= 0f)
            {
                Repair();
                _repairTimer = _repairInterval;
            }
        }

        // ══════════════════════════════════════
        //  A* Node Tag 操作
        // ══════════════════════════════════════

        /// <summary>
        /// 對 _triggerZone.bounds 範圍內的所有 A* 節點設定或清除 DoorNodeTag。
        /// intact=true  → Tag=DoorNodeTag（門完整，敵人踩到節點後設 _doorGate 停下）
        /// intact=false → Tag=0（門破壞，節點標記清除，敵人直接通過）
        ///
        /// 效能備注：
        ///   UpdateGraphs() 為非同步操作，不阻塞主執行緒。
        ///   每場景門 < 20 個，且只在狀態轉換時呼叫（非每幀），效能影響極小。
        /// </summary>
        private void UpdateNodeTags(bool intact)
        {
            if (AstarPath.active == null || _triggerZone == null) return;

            Bounds expandedBounds = _triggerZone.bounds;
            expandedBounds.Expand(new Vector3(_tagBoundsPaddingXY * 2f, _tagBoundsPaddingXY * 2f, _tagBoundsPaddingZ));

            var guo = new GraphUpdateObject(expandedBounds)
            {
                modifyTag = true,
                setTag    = new Pathfinding.PathfindingTag(intact ? DoorNodeTag : 0u)
            };
            AstarPath.active.UpdateGraphs(guo);

            if (_flushGraphUpdatesOnStateChange)
                AstarPath.active.FlushGraphUpdates();

            if (_enableDoorTagDiagnostics)
            {
                Core.DebugLogger.Log(
                    $"門 '{gameObject.name}' 更新 A* Tag：{(intact ? $"設 Tag={DoorNodeTag}（阻擋）" : "清除 Tag（通行）")}\n" +
                    $"  更新 bounds center={expandedBounds.center} size={expandedBounds.size}\n" +
                    $"  FlushGraphUpdates={_flushGraphUpdatesOnStateChange}",
                    Core.LogCategory.Interaction);
            }

            // 寫入後立即驗證：以門中心最近節點判斷 Tag 是否符合預期（不做 fallback，直接報錯）
            var nearestInfo = AstarPath.active.GetNearest(_triggerZone.bounds.center, NNConstraint.None);
            if (nearestInfo.node == null)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}' 更新 Tag 後驗證失敗：找不到最近 A* 節點。\n" +
                    $"  檢查項目：\n" +
                    $"    1) Grid Graph 是否覆蓋門位置（{_triggerZone.bounds.center}）\n" +
                    $"    2) AstarPath 是否已 Scan 且啟用\n" +
                    $"    3) _triggerZone 是否位於正確場景座標",
                    Core.LogCategory.Interaction);
                return;
            }

            uint expectedTag = intact ? DoorNodeTag : 0u;
            uint actualTag   = nearestInfo.node.Tag;
            if (actualTag != expectedTag)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}' 更新 Tag 後驗證失敗：最近節點 Tag 不符。\n" +
                    $"  期望 Tag={expectedTag}，實際 Tag={actualTag}\n" +
                    $"  節點位置={nearestInfo.position}  門中心={_triggerZone.bounds.center}\n" +
                    $"  建議檢查：\n" +
                    $"    1) _tagBoundsPaddingXY / _tagBoundsPaddingZ 是否過小\n" +
                    $"    2) 門的 _triggerZone 是否覆蓋敵人實際走位路徑\n" +
                    $"    3) Grid Graph node size 是否過大導致門區域只覆蓋到鄰近節點",
                    Core.LogCategory.Interaction);
            }
            else if (_enableDoorTagDiagnostics)
            {
                Core.DebugLogger.Log(
                    $"門 '{gameObject.name}' Tag 寫入驗證成功：最近節點 Tag={actualTag}（期望={expectedTag}）。",
                    Core.LogCategory.Interaction);
            }
        }

        /// <summary>
        /// 在所有存活門中尋找最近的完整門（IsIntact=true）。
        /// 由 EnemyController.FollowPath() 踩上 DoorNodeTag 節點時呼叫。
        ///
        /// 效能備注：O(n)，n = 存活門數（通常 &lt; 20）。1000+ 敵人同幀呼叫也僅迭代 20 次。
        /// </summary>
        /// <param name="pos">查詢中心（敵人世界座標）</param>
        /// <param name="searchRadiusSqr">搜尋半徑的平方（世界單位²）</param>
        public static DoorController FindNearestIntact(Vector2 pos, float searchRadiusSqr)
        {
            DoorController nearest    = null;
            float          nearestSqr = searchRadiusSqr;

            foreach (var d in s_allDoors)
            {
                if (d == null || !d.IsIntact) continue;
                float sqr = ((Vector2)d.transform.position - pos).sqrMagnitude;
                if (sqr < nearestSqr) { nearestSqr = sqr; nearest = d; }
            }
            return nearest;
        }

        /// <summary>
        /// 以每扇門各自設定的 _doorDetectRadius 進行最近完整門查找。
        /// 用於 EnemyController 踩上 DoorNodeTag 時的門綁定，避免寫死半徑造成偏移漏抓。
        /// </summary>
        public static DoorController FindNearestIntact(Vector2 pos)
        {
            DoorController nearest = null;
            float nearestSqr = float.PositiveInfinity;

            foreach (var d in s_allDoors)
            {
                if (d == null || !d.IsIntact) continue;
                float sqr = ((Vector2)d.transform.position - pos).sqrMagnitude;
                float radiusSqr = d._doorDetectRadius * d._doorDetectRadius;
                if (sqr <= radiusSqr && sqr < nearestSqr)
                {
                    nearest = d;
                    nearestSqr = sqr;
                }
            }

            return nearest;
        }

        // ══════════════════════════════════════
        //  啟動時驗證 DoorSensor Layer 設定
        // ══════════════════════════════════════

        private void ValidateDoorSensorLayer()
        {
            int doorSensorLayer = LayerMask.NameToLayer("DoorSensor");

            if (doorSensorLayer < 0)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}'：找不到 'DoorSensor' Layer！\n" +
                    "請依序執行以下設定：\n" +
                    "  1) Edit → Project Settings → Tags and Layers → 在空白欄位輸入 'DoorSensor'\n" +
                    "  2) Edit → Project Settings → Physics 2D → Layer Collision Matrix → 啟用 Player ↔ DoorSensor\n" +
                    "  3) 在 Hierarchy 選取此門的 _triggerZone 子物件，Layer 改為 DoorSensor\n" +
                    "若未設定，玩家無法觸發修補，門阻擋功能完全失效。",
                    Core.LogCategory.Interaction);
                return;
            }

            if (_triggerZone == null)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}'：_triggerZone 未指定！\n" +
                    "請在 Inspector 的『觸發感應區（Is Trigger）』插槽拖入觸發區 Collider。",
                    Core.LogCategory.Interaction);
                return;
            }

            if (_triggerZone.gameObject.layer != doorSensorLayer)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}'：_triggerZone 的 Layer 是 " +
                    $"'{LayerMask.LayerToName(_triggerZone.gameObject.layer)}'，必須設為 'DoorSensor'！\n" +
                    "請在 Hierarchy 選取 _triggerZone 子物件 → Inspector 上方 Layer 下拉 → 選擇 DoorSensor。",
                    Core.LogCategory.Interaction);
            }

            if (AstarPath.active == null)
            {
                Core.DebugLogger.LogError(
                    $"門 '{gameObject.name}'：場景中找不到 AstarPath 元件！\n" +
                    "新架構的門阻擋依賴 A* Node Tag，必須有 AstarPath 才能標記節點。\n" +
                    "請確認場景中已放置 AstarPath 物件並設定好 Grid Graph。",
                    Core.LogCategory.Interaction);
            }
        }

        // ══════════════════════════════════════
        //  修補
        // ══════════════════════════════════════

        private void Repair()
        {
            bool wasIntact = IsIntact;
            _currentHP = Mathf.Min(_maxHP, _currentHP + _repairPerTick);

            _onDoorRepairTick.Play(gameObject.name, "OnDoorRepairTick");
            Core.DebugLogger.Log($"門修補中：HP={_currentHP}/{_maxHP}", Core.LogCategory.Interaction);

            if (!wasIntact && IsIntact)
            {
                // 門從破壞狀態回復完整 → 標記 A* 節點為門 Tag，敵人踩上時自動感知
                UpdateNodeTags(true);
                _onDoorPatched.Play(gameObject.name, "OnDoorPatched");
                Core.GameEvents.FireDoorPatched(_doorId);
                Core.DebugLogger.Log(
                    $"門 '{gameObject.name}' 修補完成！A* 節點已標記 Tag={DoorNodeTag}（敵人踩上即停止）。",
                    Core.LogCategory.Interaction);
            }

            UpdateDoorState();
        }

        // ══════════════════════════════════════
        //  IDamageable（敵人攻擊門）
        // ══════════════════════════════════════

        public void TakeDamage(float damage)
        {
            if (!IsIntact) return;

            _currentHP = Mathf.Max(0f, _currentHP - damage);
            _onDoorDamaged.Play(gameObject.name, "OnDoorDamaged");
            Core.DebugLogger.Log($"門受到 {damage} 傷害，剩餘 HP={_currentHP}", Core.LogCategory.Interaction);

            if (!IsIntact)
            {
                // 門被破壞 → 清除 A* 節點 Tag，敵人路徑上此節點不再阻擋
                UpdateNodeTags(false);
                _onDoorDestroyed.Play(gameObject.name, "OnDoorDestroyed");
                Core.GameEvents.FireDoorDestroyed(_doorId);
                Core.DebugLogger.Log(
                    $"門 '{gameObject.name}' 被破壞！A* 節點 Tag 已清除，敵人恢復自由通行。",
                    Core.LogCategory.Interaction);
            }

            UpdateDoorState();
        }

        // ══════════════════════════════════════
        //  狀態同步（Sprite 切換 + 音效狀態機）
        // ══════════════════════════════════════

        /// <summary>
        /// 同步 Sprite 與音效。
        /// 音效改為追蹤視覺狀態轉換，確保：
        ///   · 玩家站在門內時修好門 → 立即播放開門音效（舊架構漏播）
        ///   · 門破壞時玩家離開 → 不播關門音效（無門可關）
        ///   · 正常靠近 / 離開 → 各播一次，不重複
        /// </summary>
        private void UpdateDoorState()
        {
            bool shouldShowOpen = IsIntact && _playerInRange;

            // 只在視覺狀態轉換時播放音效（false→true 或 true→false）
            if (shouldShowOpen && !_wasShowingOpenSprite)
                _onDoorOpen.Play(gameObject.name, "OnDoorOpen");
            else if (!shouldShowOpen && _wasShowingOpenSprite && IsIntact)
                _onDoorClose.Play(gameObject.name, "OnDoorClose");

            _wasShowingOpenSprite = shouldShowOpen;

            if (_spriteRenderer == null) return;

            if (!IsIntact)
                _spriteRenderer.sprite = _destroyedSprite;
            else if (_playerInRange)
                _spriteRenderer.sprite = _openSprite;
            else
                _spriteRenderer.sprite = _closedSprite;
        }

        // ══════════════════════════════════════
        //  觸發區（只偵測玩家）
        // ══════════════════════════════════════

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponent<Player.PlayerStats>() == null) return;

            _playerInRange = true;
            _repairTimer   = _repairInterval; // 立即開始第一次修補計時
            UpdateDoorState();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponent<Player.PlayerStats>() == null) return;

            _playerInRange = false;
            UpdateDoorState();
        }
    }
}
