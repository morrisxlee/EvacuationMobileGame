using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace SurvivalDemo.UI
{
    /// <summary>
    /// 升級選擇面板（Roguelike 3 選 1）。
    ///
    /// 掛在 Canvas 下的 UpgradePanel GameObject 上，預設 SetActive(false)。
    /// 升級觸發時自動顯示，卡片逐張以動畫進場，玩家選擇後隱藏並恢復遊戲。
    ///
    /// 場景建議結構：
    ///   Canvas
    ///   └── UpgradePanel (此腳本)
    ///       ├── DimBackground   (Image — 全螢幕半透明黑色遮罩)
    ///       ├── TitleText       (TextMeshProUGUI — "選擇升級")
    ///       ├── RarityTitle     (TextMeshProUGUI — 顯示稀有度名稱)
    ///       └── CardContainer   (HorizontalLayoutGroup — 卡片動態生成於此)
    ///
    /// 注意：因遊戲暫停（timeScale=0），所有延遲必須使用 WaitForSecondsRealtime。
    /// </summary>
    public class UpgradePanel : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  Inspector 槽位
        // ══════════════════════════════════════════════════════

        [TitleGroup("必要參照")]
        [Tooltip("場景中的 UpgradeManager。\n此腳本需呼叫 UpgradeManager.Choose(index)，必須指定。")]
        [LabelText("升級管理器")]
        [Required("必須指定 UpgradeManager，否則玩家選擇後無法套用升級！")]
        [SerializeField] private Progression.UpgradeManager _upgradeManager;

        [TitleGroup("必要參照")]
        [Tooltip("卡片 Prefab（需掛有 UpgradeCardButton.cs）。\n每次升級選擇時動態 Instantiate 3 張進此容器。")]
        [LabelText("卡片 Prefab")]
        [Required("必須指定卡片 Prefab！")]
        [SerializeField] private UpgradeCardButton _cardPrefab;

        [TitleGroup("必要參照")]
        [Tooltip("卡片生成的容器（建議掛 HorizontalLayoutGroup + ContentSizeFitter）。\n卡片會逐張 Instantiate 至此節點下。")]
        [LabelText("卡片容器")]
        [Required("必須指定卡片容器！")]
        [SerializeField] private Transform _cardContainer;

        // ──────────────────────────────────────────────────────

        [TitleGroup("UI 元件（選填）")]
        [Tooltip("顯示稀有度名稱的 TextMeshPro，例如「稀有升級」。\n可留空。")]
        [LabelText("稀有度標題文字")]
        [SerializeField] private TextMeshProUGUI _rarityTitleText;

        [TitleGroup("UI 元件（選填）")]
        [Tooltip("全螢幕半透明背景遮罩 Image。\n可留空，留空時面板本身負責視覺遮擋。")]
        [LabelText("暗化背景")]
        [SerializeField] private Image _dimBackground;

        // ──────────────────────────────────────────────────────

        [TitleGroup("動畫設定")]
        [Tooltip("每張卡片進場後到下一張卡片開始生成的間隔（秒）。\n使用真實時間（不受 timeScale 影響）。建議 0.1~0.2。")]
        [LabelText("卡片生成間隔（秒）")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _cardSpawnInterval = 0.12f;

        [TitleGroup("動畫設定")]
        [Tooltip("單張卡片從 scale=0 放大到 scale=1 的動畫時長（秒）。\n使用真實時間，建議 0.15~0.25。")]
        [LabelText("卡片進場動畫時長（秒）")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _cardScaleDuration = 0.18f;

        // ══════════════════════════════════════════════════════
        //  私有狀態
        // ══════════════════════════════════════════════════════

        private readonly List<GameObject> _spawnedCards = new(3);
        private Coroutine _spawnCoroutine;
        private bool _isChosen; // 防止玩家在動畫期間重複觸發

        // ══════════════════════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 必須在 SetActive(false) 之前訂閱事件。
            // 原因：Awake → OnEnable → Start，若 Awake 先呼叫 SetActive(false)，
            // OnEnable 永遠不會觸發，事件訂閱丟失，導致面板對升級事件無反應、遊戲永久凍結。
            Core.GameEvents.OnUpgradeReady  += HandleUpgradeReady;
            Core.GameEvents.OnUpgradeChosen += HandleUpgradeChosen;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            Core.GameEvents.OnUpgradeReady  -= HandleUpgradeReady;
            Core.GameEvents.OnUpgradeChosen -= HandleUpgradeChosen;
        }

        // ══════════════════════════════════════════════════════
        //  事件處理
        // ══════════════════════════════════════════════════════

        private void HandleUpgradeReady()
        {
            if (_upgradeManager == null)
            {
                Core.DebugLogger.LogError(
                    "UpgradePanel：未指定 UpgradeManager！\n" +
                    "請在 Inspector 將場景中的 UpgradeManager 拖入「升級管理器」欄位。\n" +
                    "遊戲已暫停，玩家無法繼續。",
                    Core.LogCategory.Progression);
                return;
            }

            if (_cardPrefab == null)
            {
                Core.DebugLogger.LogError(
                    "UpgradePanel：未指定卡片 Prefab！\n" +
                    "請在 Inspector 將 UpgradeCardButton Prefab 拖入「卡片 Prefab」欄位。",
                    Core.LogCategory.Progression);
                return;
            }

            _isChosen = false;

            // 更新稀有度標題
            if (_rarityTitleText != null)
                _rarityTitleText.text = RarityToLabel(_upgradeManager.CurrentRarity) + "升級";

            // 顯示面板
            gameObject.SetActive(true);

            // 清除上次殘留的卡片（防禦性處理）
            ClearCards();

            // 啟動逐張生成協程
            if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = StartCoroutine(SpawnCardsSequentially());
        }

        private void HandleUpgradeChosen(Core.UpgradeChoiceData _)
        {
            HidePanel();
        }

        // ══════════════════════════════════════════════════════
        //  卡片生成
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 逐張生成升級卡片，每張之間間隔 _cardSpawnInterval 真實秒。
        /// 必須使用 WaitForSecondsRealtime，因為 timeScale = 0。
        /// </summary>
        private IEnumerator SpawnCardsSequentially()
        {
            var choices = _upgradeManager.CurrentChoices;

            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null) continue;

                // Instantiate 卡片
                var cardInstance = Instantiate(_cardPrefab, _cardContainer);
                int capturedIndex = i; // 閉包捕捉，避免循環變數問題
                cardInstance.Setup(choices[i], capturedIndex, OnCardChosen);
                _spawnedCards.Add(cardInstance.gameObject);

                // 啟動進場動畫（scale 0 → 1）
                StartCoroutine(ScaleInCard(cardInstance.transform));

                // 等待下一張生成
                yield return new WaitForSecondsRealtime(_cardSpawnInterval);
            }

            _spawnCoroutine = null;
        }

        /// <summary>
        /// 卡片進場動畫：從 scale=0 線性放大到 scale=1。
        /// 使用 Time.unscaledDeltaTime，不受 timeScale 影響。
        /// </summary>
        private IEnumerator ScaleInCard(Transform cardTransform)
        {
            cardTransform.localScale = Vector3.zero;

            float elapsed = 0f;
            while (elapsed < _cardScaleDuration)
            {
                // 使用非暫停時間推進
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _cardScaleDuration);
                cardTransform.localScale = Vector3.one * t;
                yield return null; // 等待下一幀（不受 timeScale 影響因為我們用 unscaledDeltaTime）
            }

            cardTransform.localScale = Vector3.one;
        }

        // ══════════════════════════════════════════════════════
        //  選擇回調
        // ══════════════════════════════════════════════════════

        private void OnCardChosen(int index)
        {
            if (_isChosen) return; // 防止動畫期間多次點擊
            _isChosen = true;

            // 禁用所有卡片按鈕，防止重複觸發
            foreach (var card in _spawnedCards)
            {
                if (card == null) continue;
                var btn = card.GetComponent<UnityEngine.UI.Button>();
                if (btn != null) btn.interactable = false;
            }

            // 呼叫 UpgradeManager.Choose() → 內部設 timeScale=1，觸發 OnUpgradeChosen 事件
            _upgradeManager.Choose(index);
            // HandleUpgradeChosen 會由事件回調觸發，在那裡執行 HidePanel()
        }

        // ══════════════════════════════════════════════════════
        //  面板控制
        // ══════════════════════════════════════════════════════

        private void HidePanel()
        {
            // 停止尚未完成的生成協程
            if (_spawnCoroutine != null)
            {
                StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }

            ClearCards();
            gameObject.SetActive(false);
        }

        private void ClearCards()
        {
            foreach (var card in _spawnedCards)
            {
                if (card != null) Destroy(card);
            }
            _spawnedCards.Clear();
        }

        // ══════════════════════════════════════════════════════
        //  輔助方法
        // ══════════════════════════════════════════════════════

        private static string RarityToLabel(Core.Rarity rarity) => rarity switch
        {
            Core.Rarity.Common    => "普通",
            Core.Rarity.Rare      => "稀有",
            Core.Rarity.Epic      => "史詩",
            Core.Rarity.Legendary => "傳說",
            _                     => "普通"
        };
    }
}
