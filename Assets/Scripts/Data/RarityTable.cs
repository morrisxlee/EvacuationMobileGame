using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 稀有度機率表：控制搜索掉落的稀有度分布。
    /// Common 58% / Rare 25% / Epic 12% / Legendary 5%（預設）。
    /// 也同時定義各稀有度對應的讀條時間。
    /// </summary>
    [CreateAssetMenu(fileName = "RarityTable", menuName = "SurvivalDemo/RarityTable")]
    public class RarityTable : ScriptableObject
    {
        [TitleGroup("稀有度機率")]
        [InfoBox("權重加總建議為 100，方便理解機率。例如 Common=58 表示 58% 機率。")]
        [Tooltip("普通（白色）品質的出現權重。預設 58%。")]
        [LabelText("普通 (Common)")]
        [Min(0f)]
        [SerializeField] private float _commonWeight = 58f;

        [TitleGroup("稀有度機率")]
        [Tooltip("稀有（綠色）品質的出現權重。預設 25%。")]
        [LabelText("稀有 (Rare)")]
        [Min(0f)]
        [SerializeField] private float _rareWeight = 25f;

        [TitleGroup("稀有度機率")]
        [Tooltip("史詩（紫色）品質的出現權重。預設 12%。")]
        [LabelText("史詩 (Epic)")]
        [Min(0f)]
        [SerializeField] private float _epicWeight = 12f;

        [TitleGroup("稀有度機率")]
        [Tooltip("傳說（橙色）品質的出現權重。預設 5%。")]
        [LabelText("傳說 (Legendary)")]
        [Min(0f)]
        [SerializeField] private float _legendaryWeight = 5f;

        [TitleGroup("搜索讀條時間")]
        [InfoBox("稀有度越高，搜索需要的時間越長。")]
        [Tooltip("普通品質搜索點的讀條時間（秒）。")]
        [LabelText("普通讀條（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _commonSearchTime = 2f;

        [TitleGroup("搜索讀條時間")]
        [Tooltip("稀有品質搜索點的讀條時間（秒）。")]
        [LabelText("稀有讀條（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _rareSearchTime = 3f;

        [TitleGroup("搜索讀條時間")]
        [Tooltip("史詩品質搜索點的讀條時間（秒）。")]
        [LabelText("史詩讀條（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _epicSearchTime = 4f;

        [TitleGroup("搜索讀條時間")]
        [Tooltip("傳說品質搜索點的讀條時間（秒）。")]
        [LabelText("傳說讀條（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _legendarySearchTime = 5f;

        private float _totalWeight;

        private void OnEnable()
        {
            RecalculateTotalWeight();
        }

        private void OnValidate()
        {
            RecalculateTotalWeight();
        }

        private void RecalculateTotalWeight()
        {
            _totalWeight = _commonWeight + _rareWeight + _epicWeight + _legendaryWeight;
            if (Mathf.Abs(_totalWeight - 100f) > 0.01f)
            {
                Core.DebugLogger.LogWarning(
                    $"RarityTable 權重加總為 {_totalWeight}，不等於 100。隨機仍會正常運作，但建議調整。",
                    Core.LogCategory.Core);
            }
        }

        /// <summary>
        /// 隨機抽取稀有度。
        /// </summary>
        public Core.Rarity Roll()
        {
            float roll = Random.Range(0f, _totalWeight);

            if (roll < _commonWeight) return Core.Rarity.Common;
            roll -= _commonWeight;

            if (roll < _rareWeight) return Core.Rarity.Rare;
            roll -= _rareWeight;

            if (roll < _epicWeight) return Core.Rarity.Epic;

            return Core.Rarity.Legendary;
        }

        /// <summary>
        /// 取得指定稀有度對應的讀條秒數。
        /// </summary>
        public float GetSearchTime(Core.Rarity rarity)
        {
            return rarity switch
            {
                Core.Rarity.Common => _commonSearchTime,
                Core.Rarity.Rare => _rareSearchTime,
                Core.Rarity.Epic => _epicSearchTime,
                Core.Rarity.Legendary => _legendarySearchTime,
                _ => _commonSearchTime
            };
        }
    }
}
