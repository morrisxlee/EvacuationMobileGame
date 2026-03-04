using TMPro;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Interaction
{
    /// <summary>
    /// 探索點：玩家站在上面自動讀條，完成後從掉落池抽獎。
    /// 每個探索點有隨機 3~6 次可搜次數，用完後永久失效。
    /// 讀條時間依「下一個掉落物稀有度」對應不同固定秒數。
    /// </summary>
    public class SearchPoint : MonoBehaviour
    {
        [TitleGroup("資料參照")]
        [Tooltip("稀有度機率表，決定此次搜索掉落物的稀有度與讀條時間。")]
        [LabelText("稀有度表")]
        [Required("必須指定稀有度表！")]
        [SerializeField] private Data.RarityTable _rarityTable;

        [TitleGroup("資料參照")]
        [Tooltip("掉落表，決定搜索完成後掉落的獎勵類型與數量。")]
        [LabelText("掉落表")]
        [Required("必須指定掉落表！")]
        [SerializeField] private Data.SearchLootTable _lootTable;

        [TitleGroup("搜索設定")]
        [Tooltip("此探索點可搜索次數的最小值。")]
        [LabelText("最小搜索次數")]
        [Min(1)]
        [SerializeField] private int _minSearchCount = 3;

        [TitleGroup("搜索設定")]
        [Tooltip("此探索點可搜索次數的最大值。實際次數會在最小值與最大值之間隨機。")]
        [LabelText("最大搜索次數")]
        [Min(1)]
        [SerializeField] private int _maxSearchCount = 6;

        [TitleGroup("搜索設定")]
        [Tooltip("搜索完成後掉落物生成位置的水平隨機偏移範圍（世界單位）。\n" +
                 "掉落物會在 transform.position.x ± 此值範圍內隨機生成，避免全部疊在同一點。\n" +
                 "建議 1.0~2.0，依探索點碰撞體大小調整。")]
        [LabelText("掉落偏移範圍 X")]
        [Min(0f)]
        [SerializeField] private float _spawnOffsetX = 1.5f;

        [TitleGroup("視覺效果")]
        [Tooltip("探索點的 SpriteRenderer，用於顯示狀態變化。")]
        [LabelText("Sprite 渲染器")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [TitleGroup("視覺效果")]
        [Tooltip("探索點可用時的顏色。")]
        [LabelText("可用顏色")]
        [SerializeField] private Color _activeColor = Color.yellow;

        [TitleGroup("視覺效果")]
        [Tooltip("探索點用完後的顏色。")]
        [LabelText("耗盡顏色")]
        [SerializeField] private Color _depletedColor = Color.gray;

        [TitleGroup("視覺效果")]
        [Tooltip("（選填）Shapes Disc 元件，用於顯示搜索進度環。\n" +
                 "在 Inspector 將此探索點子物件的 Disc 元件拖入此欄位。\n" +
                 "建議在 Disc Inspector 設定：Type = Arc、Thickness 自行調整。\n" +
                 "腳本自動控制顏色、AngRadiansStart / AngRadiansEnd，無需手動設定。\n" +
                 "留空則不顯示進度環。")]
        [LabelText("進度環（Disc，選填）")]
        [SerializeField] private Shapes.Disc _progressDisc;

        [TitleGroup("視覺效果")]
        [Tooltip("（選填）世界空間 TextMeshPro，顯示當前搜索稀有度標籤（普通／稀有／史詩／傳說）。\n" +
                 "文字顏色會自動跟隨稀有度改變，無需手動設定。\n" +
                 "留空則不顯示稀有度標籤。")]
        [LabelText("稀有度標籤（TMP，選填）")]
        [SerializeField] private TextMeshPro _rarityLabel;

        // ── 稀有度顏色（與 UpgradeCardButton 保持一致，α=1 適用於世界空間）──
        private static readonly Color RarityColorCommon    = new Color(0.20f, 0.85f, 0.35f, 1f); // 綠
        private static readonly Color RarityColorRare      = new Color(0.27f, 0.53f, 1.00f, 1f); // 藍
        private static readonly Color RarityColorEpic      = new Color(0.67f, 0.27f, 1.00f, 1f); // 紫
        private static readonly Color RarityColorLegendary = new Color(1.00f, 0.72f, 0.00f, 1f); // 金

        // ── 運行時 ──
        private int _remainingSearches;
        private bool _isSearching;
        private float _searchTimer;
        private float _currentSearchDuration;
        private Core.Rarity _nextRarity;
        private bool _playerInRange;
        private Player.PlayerStats _playerStats;
        private Feedback.FeedbackBridge _feedbackBridge;

        public bool IsDepeleted => _remainingSearches <= 0;
        public bool IsSearching => _isSearching;
        public float SearchProgress => _currentSearchDuration > 0f ? _searchTimer / _currentSearchDuration : 0f;
        public int RemainingSearches => _remainingSearches;

        private void OnEnable()
        {
            Core.GameEvents.OnUpgradeChosen += HandleUpgradeChosen;
        }

        private void OnDisable()
        {
            Core.GameEvents.OnUpgradeChosen -= HandleUpgradeChosen;
        }

        private void Awake()
        {
            _feedbackBridge = GetComponent<Feedback.FeedbackBridge>();
            if (_feedbackBridge == null)
                Core.DebugLogger.LogWarning(
                    $"SearchPoint '{gameObject.name}' 上找不到 FeedbackBridge 元件！\n" +
                    "搜索開始、中斷、完成的音效與特效將全部靜音。\n" +
                    "請在此 GameObject 上加入 FeedbackBridge 元件，並在 Inspector 設定對應的 MMF Player 插槽。",
                    Core.LogCategory.Interaction);
        }

        private void Start()
        {
            _remainingSearches = Random.Range(_minSearchCount, _maxSearchCount + 1);
            PrepareNextSearch();
            UpdateVisual();
        }

        private void Update()
        {
            if (IsDepeleted || !_playerInRange || _playerStats == null || _playerStats.IsDead) 
            {
                if (_isSearching) CancelSearch();
                return;
            }

            // 遊戲暫停時不更新
            var state = Core.GameLoopManager.Instance?.CurrentState ?? Core.GameState.Playing;
            if (state != Core.GameState.Playing && state != Core.GameState.Evacuation)
            {
                if (_isSearching) CancelSearch();
                return;
            }

            if (!_isSearching)
            {
                StartSearch();
            }

            if (_isSearching)
            {
                float speedMul = _playerStats.InteractionSpeedMultiplier;
                _searchTimer += Time.deltaTime * speedMul;
                float progress = SearchProgress;
                Core.GameEvents.FireSearchProgress(progress);
                UpdateProgressDisc(progress);

                if (_searchTimer >= _currentSearchDuration)
                {
                    CompleteSearch();
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (IsDepeleted) return;
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null)
            {
                _playerInRange = true;
                _playerStats = stats;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var stats = other.GetComponent<Player.PlayerStats>();
            if (stats != null && stats == _playerStats)
            {
                _playerInRange = false;
                _playerStats = null;
                CancelSearch();
            }
        }

        private void StartSearch()
        {
            _isSearching = true;
            _searchTimer = 0f;
            _feedbackBridge?.PlaySearchStart();
            Core.GameEvents.FireSearchStarted();
            ApplyRarityVisuals(_nextRarity);
            Core.DebugLogger.Log($"開始搜索，稀有度={_nextRarity}，需時={_currentSearchDuration}秒", Core.LogCategory.Interaction);
        }

        /// <summary>
        /// 升級選擇完成後呼叫。timeScale 已恢復為 1。
        /// 若玩家仍在範圍內，重置搜索狀態讓 Update 在下一幀重新啟動搜索。
        /// </summary>
        private void HandleUpgradeChosen(Core.UpgradeChoiceData _)
        {
            if (_playerInRange && !IsDepeleted)
            {
                _isSearching = false;
                _searchTimer = 0f;
                UpdateProgressDisc(0f);
            }
        }

        private void CancelSearch()
        {
            if (!_isSearching) return;
            _isSearching = false;
            _searchTimer = 0f;
            _feedbackBridge?.PlaySearchStop();
            Core.GameEvents.FireSearchProgress(0f);
            UpdateProgressDisc(0f);
            if (_rarityLabel != null) _rarityLabel.text = string.Empty;
        }

        private void CompleteSearch()
        {
            _isSearching = false;
            _searchTimer = 0f;
            _remainingSearches--;
            _feedbackBridge?.PlaySearchStop();      // 停止循環讀條音效
            _feedbackBridge?.PlaySearchComplete();  // 播放完成特效／音效
            UpdateProgressDisc(0f);

            // 抽取掉落物
            var lootEntry = _lootTable.Roll();
            if (lootEntry != null)
            {
                var result = new Core.SearchResultData
                {
                    RewardType = lootEntry.rewardType,
                    Rarity = _nextRarity,
                    Amount = Random.Range(lootEntry.minAmount, lootEntry.maxAmount + 1)
                };

                SpawnPickup(result, lootEntry);
                Core.GameEvents.FireTreasureFound(result);
                Core.GameEvents.FireSearchCompleted();

                Core.DebugLogger.Log(
                    $"搜索完成！獎勵={result.RewardType}, 稀有度={result.Rarity}, 數量={result.Amount}",
                    Core.LogCategory.Interaction);
            }

            if (!IsDepeleted)
            {
                PrepareNextSearch();
            }
            UpdateVisual();
        }

        /// <summary>
        /// 在探索點附近生成對應掉落物 Prefab，並呼叫 Init() 注入獎勵資料。
        /// 掉落物由玩家物理走過觸發撿取，不立即套用效果。
        /// </summary>
        private void SpawnPickup(Core.SearchResultData result, Data.SearchLootTable.LootEntry lootEntry)
        {
            if (string.IsNullOrEmpty(lootEntry.pickupPoolId))
            {
                Core.DebugLogger.LogError(
                    $"[SearchPoint] '{gameObject.name}' 的 LootEntry（{lootEntry.rewardType}）pickupPoolId 為空！\n" +
                    "請在 SearchLootTable Inspector 中填寫對應的 Pickup Pool ID。\n" +
                    "此次搜索不會生成掉落物。",
                    Core.LogCategory.Interaction);
                return;
            }

            if (Pooling.GenericPool.Instance == null)
            {
                Core.DebugLogger.LogError(
                    $"[SearchPoint] '{gameObject.name}' 嘗試生成掉落物時 GenericPool.Instance 為 null！\n" +
                    "請確認場景中有 GenericPool 物件且尚未被銷毀。",
                    Core.LogCategory.Interaction);
                return;
            }

            float offsetX = Random.Range(-_spawnOffsetX, _spawnOffsetX);
            Vector3 spawnPos = transform.position + new Vector3(offsetX, 0f, 0f);

            var go = Pooling.GenericPool.Instance.Spawn(lootEntry.pickupPoolId, spawnPos, Quaternion.identity);
            if (go == null)
            {
                Core.DebugLogger.LogError(
                    $"[SearchPoint] '{gameObject.name}' 無法從池 '{lootEntry.pickupPoolId}' 生成掉落物！\n" +
                    "可能原因：池 ID 錯誤、池未在 GenericPool 中註冊、或池已達上限。\n" +
                    "請檢查 GenericPool Inspector 中是否有 ID='{lootEntry.pickupPoolId}' 的 PoolEntry。",
                    Core.LogCategory.Interaction);
                return;
            }

            var pickup = go.GetComponent<PickupItem>();
            if (pickup == null)
            {
                Core.DebugLogger.LogError(
                    $"[SearchPoint] 池 '{lootEntry.pickupPoolId}' 生成的物件 '{go.name}' 上找不到 PickupItem 元件！\n" +
                    "請確認此池的 Prefab 上已掛載 PickupItem 腳本。",
                    Core.LogCategory.Interaction);
                return;
            }

            pickup.Init(result.RewardType, result.Amount, lootEntry.pickupPoolId);
        }

        /// <summary>
        /// 預先決定下一次搜索的稀有度與讀條時間。
        /// </summary>
        private void PrepareNextSearch()
        {
            if (_rarityTable == null)
            {
                Core.DebugLogger.LogError("SearchPoint 沒有指定 RarityTable！", Core.LogCategory.Interaction);
                _nextRarity = Core.Rarity.Common;
                _currentSearchDuration = 2f;
                return;
            }
            _nextRarity = _rarityTable.Roll();
            _currentSearchDuration = _rarityTable.GetSearchTime(_nextRarity);
        }

        /// <summary>
        /// 更新 Shapes Disc 進度環角度與顏色。
        /// progress 0→1 對應 0%→100% 進度，順時針從 12 點鐘方向掃過完整圓。
        /// 搜索未進行或探索點耗盡時隱藏 Disc。
        /// </summary>
        private void UpdateProgressDisc(float progress)
        {
            if (_progressDisc == null) return;

            bool show = _isSearching && !IsDepeleted;
            _progressDisc.gameObject.SetActive(show);
            if (!show) return;

            // 12 點鐘位置（π/2），往順時針（正角度方向）掃到 progress*2π
            const float kTwoPi = Mathf.PI * 2f;
            const float kStart  = Mathf.PI * 0.5f;
            _progressDisc.AngRadiansStart = kStart;
            _progressDisc.AngRadiansEnd   = kStart + kTwoPi * Mathf.Clamp01(progress);
            _progressDisc.Color = GetRarityColor(_nextRarity);
        }

        /// <summary>
        /// 同時更新 Disc 顏色與稀有度標籤文字／顏色。
        /// 每次 StartSearch() 被呼叫時執行，確保顏色跟隨當前 _nextRarity。
        /// </summary>
        private void ApplyRarityVisuals(Core.Rarity rarity)
        {
            Color c = GetRarityColor(rarity);

            if (_progressDisc != null)
                _progressDisc.Color = c;

            if (_rarityLabel != null)
            {
                _rarityLabel.text  = GetRarityLabel(rarity);
                _rarityLabel.color = c;
            }
        }

        private static Color GetRarityColor(Core.Rarity rarity) => rarity switch
        {
            Core.Rarity.Common    => RarityColorCommon,
            Core.Rarity.Rare      => RarityColorRare,
            Core.Rarity.Epic      => RarityColorEpic,
            Core.Rarity.Legendary => RarityColorLegendary,
            _                     => RarityColorCommon
        };

        private static string GetRarityLabel(Core.Rarity rarity) => rarity switch
        {
            Core.Rarity.Common    => "普通",
            Core.Rarity.Rare      => "稀有",
            Core.Rarity.Epic      => "史詩",
            Core.Rarity.Legendary => "傳說",
            _                     => "普通"
        };

        private void UpdateVisual()
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.color = IsDepeleted ? _depletedColor : _activeColor;
        }
    }
}
