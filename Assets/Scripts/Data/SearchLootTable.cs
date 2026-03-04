using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 搜索掉落表：定義探索點可掉落的獎勵種類與權重。
    /// </summary>
    [CreateAssetMenu(fileName = "SearchLootTable", menuName = "SurvivalDemo/SearchLootTable")]
    public class SearchLootTable : ScriptableObject
    {
        [System.Serializable]
        public class LootEntry
        {
            [Tooltip("獎勵類型：Currency=金錢、Upgrade=觸發升級、Heal=補血、Battery=電池、Key=鑰匙。")]
            [LabelText("獎勵類型")]
            public Core.SearchRewardType rewardType;

            [Tooltip("此獎勵類型的出現權重。數值越高越常出現。")]
            [LabelText("權重")]
            [Min(0.01f)]
            public float weight = 1f;

            [Tooltip("獎勵數量的最小值。Upgrade 類型填 0 即可。")]
            [LabelText("最小數量")]
            [Min(0)]
            public int minAmount = 1;

            [Tooltip("獎勵數量的最大值。Upgrade 類型填 0 即可。")]
            [LabelText("最大數量")]
            [Min(0)]
            public int maxAmount = 10;

            [Tooltip("此獎勵掉落的 PickupItem 物件池 ID。\n" +
                     "必須與 GenericPool Inspector 中對應 PoolEntry 的「池 ID」完全一致（區分大小寫）。\n" +
                     "建議命名規則：pickup_coin、pickup_health、pickup_battery、pickup_key、pickup_upgrade。\n" +
                     "留空則搜索完成時不會生成掉落物（仍會觸發 FireTreasureFound 通知 HUD）。")]
            [LabelText("Pickup Pool ID")]
            public string pickupPoolId;
        }

        [TitleGroup("掉落設定")]
        [InfoBox("設定搜索點可掉落的獎勵種類與機率。建議包含 Currency、Heal、Battery、Key、Upgrade 各至少一項。")]
        [Tooltip("搜索點可掉落的獎勵列表。")]
        [LabelText("掉落列表")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowItemCount = true)]
        [SerializeField] private List<LootEntry> _lootEntries = new();

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
            _totalWeight = 0f;
            for (int i = 0; i < _lootEntries.Count; i++)
            {
                _totalWeight += _lootEntries[i].weight;
            }
        }

        /// <summary>
        /// 隨機抽取一個掉落結果。
        /// </summary>
        public LootEntry Roll()
        {
            if (_lootEntries.Count == 0)
            {
                Core.DebugLogger.LogError("SearchLootTable 的 lootEntries 為空，無法抽取！", Core.LogCategory.Core);
                return null;
            }

            float roll = Random.Range(0f, _totalWeight);
            for (int i = 0; i < _lootEntries.Count; i++)
            {
                roll -= _lootEntries[i].weight;
                if (roll <= 0f) return _lootEntries[i];
            }
            return _lootEntries[_lootEntries.Count - 1];
        }
    }
}
