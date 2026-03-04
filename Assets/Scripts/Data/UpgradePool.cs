using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 升級池：單一 SO 包含所有 24 個升級條目（6 種屬性 × 4 個稀有度）。
    /// 稀有度由每個條目自身的 rarity 欄位決定，不再分組。
    /// 點擊 Inspector 的「一鍵生成全部 24 個預設升級」按鈕即可自動填入所有預設值。
    /// </summary>
    [CreateAssetMenu(fileName = "UpgradePool", menuName = "SurvivalDemo/UpgradePool")]
    public class UpgradePool : ScriptableObject
    {
        [TitleGroup("升級清單")]
        [InfoBox("所有升級都在這一個清單裡。每個條目內有「稀有度」欄位可個別設定。\n若清單為空，點擊下方按鈕一鍵生成全部 24 個預設升級（6 屬性 × 4 稀有度）。")]
        [Tooltip("所有可用的升級條目。稀有度由每個條目內的「稀有度」欄位決定，UpgradeManager 會依當前抽到的稀有度篩選。")]
        [LabelText("升級清單")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowItemCount = true)]
        [SerializeField] private List<UpgradeEntry> _upgrades = new();

        [Button("一鍵生成全部 24 個預設升級（會清除現有清單）"), TitleGroup("升級清單")]
        [GUIColor(0.4f, 0.9f, 0.6f)]
        private void GenerateAllUpgrades()
        {
            _upgrades.Clear();

            // ── 傷害（Damage）──
            Add("傷害強化 I",    "武器傷害 +10%",         UpgradeStatType.Damage,          0.10f,  true,  Core.Rarity.Common);
            Add("傷害強化 II",   "武器傷害 +20%",         UpgradeStatType.Damage,          0.20f,  true,  Core.Rarity.Rare);
            Add("傷害強化 III",  "武器傷害 +35%",         UpgradeStatType.Damage,          0.35f,  true,  Core.Rarity.Epic);
            Add("致命打擊",      "武器傷害 +60%",         UpgradeStatType.Damage,          0.60f,  true,  Core.Rarity.Legendary);
            // ── 射速（FireRate）──
            Add("射速提升 I",    "射速 +8%",              UpgradeStatType.FireRate,        0.08f,  true,  Core.Rarity.Common);
            Add("射速提升 II",   "射速 +15%",             UpgradeStatType.FireRate,        0.15f,  true,  Core.Rarity.Rare);
            Add("射速提升 III",  "射速 +25%",             UpgradeStatType.FireRate,        0.25f,  true,  Core.Rarity.Epic);
            Add("連射狂潮",      "射速 +40%",             UpgradeStatType.FireRate,        0.40f,  true,  Core.Rarity.Legendary);
            // ── 攻擊範圍（AttackRange）──
            Add("射程延伸 I",    "攻擊範圍 +10%",         UpgradeStatType.AttackRange,     0.10f,  true,  Core.Rarity.Common);
            Add("射程延伸 II",   "攻擊範圍 +20%",         UpgradeStatType.AttackRange,     0.20f,  true,  Core.Rarity.Rare);
            Add("射程延伸 III",  "攻擊範圍 +30%",         UpgradeStatType.AttackRange,     0.30f,  true,  Core.Rarity.Epic);
            Add("全域掌控",      "攻擊範圍 +50%",         UpgradeStatType.AttackRange,     0.50f,  true,  Core.Rarity.Legendary);
            // ── 投射物速度（ProjectileSpeed）──
            Add("彈速強化 I",    "子彈速度 +10%",         UpgradeStatType.ProjectileSpeed, 0.10f,  true,  Core.Rarity.Common);
            Add("彈速強化 II",   "子彈速度 +20%",         UpgradeStatType.ProjectileSpeed, 0.20f,  true,  Core.Rarity.Rare);
            Add("彈速強化 III",  "子彈速度 +35%",         UpgradeStatType.ProjectileSpeed, 0.35f,  true,  Core.Rarity.Epic);
            Add("光速彈幕",      "子彈速度 +60%",         UpgradeStatType.ProjectileSpeed, 0.60f,  true,  Core.Rarity.Legendary);
            // ── 子彈數量（PelletCount，固定加算）──
            Add("多彈 I",        "多發射 +1 顆子彈",      UpgradeStatType.PelletCount,     1f,     false, Core.Rarity.Common);
            Add("多彈 II",       "多發射 +1 顆子彈",      UpgradeStatType.PelletCount,     1f,     false, Core.Rarity.Rare);
            Add("多彈 III",      "多發射 +2 顆子彈",      UpgradeStatType.PelletCount,     2f,     false, Core.Rarity.Epic);
            Add("彈幕風暴",      "多發射 +3 顆子彈",      UpgradeStatType.PelletCount,     3f,     false, Core.Rarity.Legendary);
            // ── 散射縮減（SpreadReduction，負百分比 = 散射角縮小）──
            Add("精準度 I",      "散射角度縮小 5%",       UpgradeStatType.SpreadReduction, -0.05f, true,  Core.Rarity.Common);
            Add("精準度 II",     "散射角度縮小 10%",      UpgradeStatType.SpreadReduction, -0.10f, true,  Core.Rarity.Rare);
            Add("精準度 III",    "散射角度縮小 20%",      UpgradeStatType.SpreadReduction, -0.20f, true,  Core.Rarity.Epic);
            Add("神槍手",        "散射角度縮小 35%",      UpgradeStatType.SpreadReduction, -0.35f, true,  Core.Rarity.Legendary);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[UpgradePool] 已生成 {_upgrades.Count} 個升級條目。");
        }

        private void Add(string name, string desc, UpgradeStatType stat, float value, bool isPercent, Core.Rarity rarity)
        {
            _upgrades.Add(new UpgradeEntry
            {
                displayName  = name,
                description  = desc,
                statType     = stat,
                value        = value,
                isPercentage = isPercent,
                rarity       = rarity
            });
        }

        /// <summary>
        /// 取得指定稀有度的所有升級（每次呼叫線性掃描 24 條，GC 極低）。
        /// </summary>
        public List<UpgradeEntry> GetPoolByRarity(Core.Rarity rarity)
        {
            var result = new List<UpgradeEntry>();
            for (int i = 0; i < _upgrades.Count; i++)
                if (_upgrades[i].rarity == rarity) result.Add(_upgrades[i]);
            return result;
        }

        /// <summary>
        /// 從指定稀有度池隨機抽取 count 個升級（不重複）。
        /// 若池中數量不足，則回傳全部可用的。
        /// </summary>
        public List<UpgradeEntry> GetRandomUpgrades(Core.Rarity rarity, int count)
        {
            var pool = GetPoolByRarity(rarity);
            var result = new List<UpgradeEntry>(count);

            if (pool == null || pool.Count == 0)
            {
                Core.DebugLogger.LogError(
                    $"稀有度 {rarity} 的升級池為空！\n" +
                    "請在 UpgradePool 資產上點擊「一鍵生成全部 24 個預設升級」按鈕，或手動在清單中新增對應稀有度的條目。",
                    Core.LogCategory.Progression);
                return result;
            }

            // Fisher-Yates 部分洗牌（不修改原清單，使用索引陣列）
            int poolCount = pool.Count;
            int[] indices = new int[poolCount];
            for (int i = 0; i < poolCount; i++) indices[i] = i;

            int pickCount = Mathf.Min(count, poolCount);
            for (int i = 0; i < pickCount; i++)
            {
                int j = Random.Range(i, poolCount);
                (indices[i], indices[j]) = (indices[j], indices[i]);
                result.Add(pool[indices[i]]);
            }

            return result;
        }
    }

    /// <summary>
    /// 單筆升級定義。稀有度由條目自身的 rarity 欄位決定。
    /// </summary>
    [System.Serializable]
    public class UpgradeEntry
    {
        [TitleGroup("基本資訊")]
        [Tooltip("升級的顯示名稱，用於 UI。")]
        [LabelText("名稱")]
        public string displayName;

        [TitleGroup("基本資訊")]
        [Tooltip("升級的描述文字，用於 UI。")]
        [LabelText("描述")]
        [TextArea(2, 4)]
        public string description;

        [TitleGroup("基本資訊")]
        [Tooltip("升級的圖示，用於 UI。可留空。")]
        [LabelText("圖示")]
        [PreviewField(40, ObjectFieldAlignment.Left)]
        public Sprite icon;

        [TitleGroup("基本資訊")]
        [Tooltip("此升級的稀有度，決定在哪個等級的抽獎輪次中出現。\nCommon=普通 / Rare=稀有 / Epic=史詩 / Legendary=傳說")]
        [LabelText("稀有度")]
        public Core.Rarity rarity;

        [TitleGroup("效果設定")]
        [Tooltip("此升級影響的武器數值類型。\nDamage=傷害 / FireRate=射速 / AttackRange=範圍 / ProjectileSpeed=彈速 / PelletCount=子彈數 / SpreadReduction=精準度")]
        [LabelText("影響數值")]
        public UpgradeStatType statType;

        [TitleGroup("效果設定")]
        [Tooltip("加成數值。\n若為百分比：0.10 = +10%，-0.10 = -10%（散射縮減用負值）。\n若非百分比（子彈數量）：直接填整數如 1、2、3。")]
        [LabelText("加成值")]
        public float value;

        [TitleGroup("效果設定")]
        [Tooltip("勾選 = 百分比加成（乘算）；不勾選 = 固定值加成（加算），例如子彈數量 +1。")]
        [LabelText("百分比加成")]
        public bool isPercentage = true;
    }

    /// <summary>
    /// 升級影響的數值類型。
    /// </summary>
    public enum UpgradeStatType
    {
        [LabelText("傷害")]        Damage,
        [LabelText("射速")]        FireRate,
        [LabelText("攻擊範圍")]    AttackRange,
        [LabelText("投射物速度")]  ProjectileSpeed,
        [LabelText("子彈數量")]    PelletCount,
        [LabelText("散射縮減")]    SpreadReduction
    }
}
