using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace SurvivalDemo.UI
{
    /// <summary>
    /// 升級卡片按鈕（Prefab 根節點元件）。
    /// 由 UpgradePanel 在升級選擇時動態 Instantiate，透過 Setup() 填入資料。
    ///
    /// Prefab 建議結構：
    ///   UpgradeCardButton (Button + UpgradeCardButton.cs)
    ///   ├── RarityBackground  (Image — 半透明底色，依稀有度著色)
    ///   ├── IconImage         (Image — upgrade.icon，無圖示時隱藏)
    ///   ├── NameText          (TextMeshProUGUI — 升級名稱)
    ///   ├── DescriptionText   (TextMeshProUGUI — 升級描述)
    ///   └── RarityBadge       (TextMeshProUGUI — 稀有度標籤文字)
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UpgradeCardButton : MonoBehaviour
    {
        [TitleGroup("UI 元件")]
        [Tooltip("卡片背景圖，依稀有度著色（半透明底）。\n請拖入 Prefab 內的 RarityBackground Image。")]
        [LabelText("稀有度背景")]
        [SerializeField] private Image _rarityBackground;

        [TitleGroup("UI 元件")]
        [Tooltip("升級圖示 Image。若 UpgradeEntry 沒有設定 icon 則自動隱藏此元件。")]
        [LabelText("圖示")]
        [SerializeField] private Image _iconImage;

        [TitleGroup("UI 元件")]
        [Tooltip("升級名稱 TextMeshProUGUI。顯示 UpgradeEntry.displayName。")]
        [LabelText("名稱文字")]
        [SerializeField] private TextMeshProUGUI _nameText;

        [TitleGroup("UI 元件")]
        [Tooltip("升級描述 TextMeshProUGUI。顯示 UpgradeEntry.description。")]
        [LabelText("描述文字")]
        [SerializeField] private TextMeshProUGUI _descriptionText;

        [TitleGroup("UI 元件")]
        [Tooltip("稀有度標籤 TextMeshProUGUI。顯示「普通」/「稀有」/「史詩」/「傳說」。")]
        [LabelText("稀有度標籤")]
        [SerializeField] private TextMeshProUGUI _rarityBadge;

        // ── 稀有度顏色對照（靜態，所有卡片共用）──
        private static readonly Color ColorCommon    = new Color(0.20f, 0.85f, 0.35f, 0.85f); // 綠
        private static readonly Color ColorRare      = new Color(0.27f, 0.53f, 1.00f, 0.85f); // #4488FF
        private static readonly Color ColorEpic      = new Color(0.67f, 0.27f, 1.00f, 0.85f); // #AA44FF
        private static readonly Color ColorLegendary = new Color(1.00f, 0.72f, 0.00f, 0.85f); // #FFB800

        private Button _button;
        private int _choiceIndex;
        private Action<int> _onChosen;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(HandleClick);
        }

        /// <summary>
        /// 由 UpgradePanel 在 Instantiate 後立即呼叫，填入升級資料與回調。
        /// </summary>
        /// <param name="entry">升級條目資料。</param>
        /// <param name="index">此卡片在選項陣列中的索引（0/1/2），傳回給 UpgradeManager.Choose()。</param>
        /// <param name="onChosen">玩家點擊後的回調，參數為 index。</param>
        public void Setup(Data.UpgradeEntry entry, int index, Action<int> onChosen)
        {
            _choiceIndex = index;
            _onChosen    = onChosen;

            // 名稱
            if (_nameText != null)
                _nameText.text = entry.displayName;

            // 描述
            if (_descriptionText != null)
                _descriptionText.text = entry.description;

            // 圖示
            if (_iconImage != null)
            {
                if (entry.icon != null)
                {
                    _iconImage.sprite  = entry.icon;
                    _iconImage.enabled = true;
                }
                else
                {
                    _iconImage.enabled = false;
                }
            }

            // 稀有度標籤與背景顏色
            Color rarityColor = GetRarityColor(entry.rarity);
            string rarityLabel = GetRarityLabel(entry.rarity);

            if (_rarityBackground != null)
                _rarityBackground.color = rarityColor;

            if (_rarityBadge != null)
                _rarityBadge.text = rarityLabel;
        }

        private void HandleClick()
        {
            _button.interactable = false; // 防止重複點擊
            _onChosen?.Invoke(_choiceIndex);
        }

        // ──────────────────────────────────────────────────────────

        private static Color GetRarityColor(Core.Rarity rarity) => rarity switch
        {
            Core.Rarity.Common    => ColorCommon,
            Core.Rarity.Rare      => ColorRare,
            Core.Rarity.Epic      => ColorEpic,
            Core.Rarity.Legendary => ColorLegendary,
            _                     => ColorCommon
        };

        private static string GetRarityLabel(Core.Rarity rarity) => rarity switch
        {
            Core.Rarity.Common    => "普通",
            Core.Rarity.Rare      => "稀有",
            Core.Rarity.Epic      => "史詩",
            Core.Rarity.Legendary => "傳說",
            _                     => "普通"
        };
    }
}
