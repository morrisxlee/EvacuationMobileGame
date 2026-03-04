using UnityEngine;
using System;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// [已棄用] 請使用 UpgradePool.cs 取代。
    /// 此腳本保留僅為相容性，新專案請勿使用。
    /// </summary>
    [Obsolete("請使用 UpgradePool.cs 取代。此腳本已棄用。")]
    [CreateAssetMenu(fileName = "NewUpgrade", menuName = "SurvivalDemo/[已棄用] UpgradeData")]
    public class UpgradeData : ScriptableObject
    {
        [Header("基本資訊")]
        [SerializeField] private string _upgradeId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private Core.Rarity _rarity;

        [Header("效果")]
        [SerializeField] private UpgradeStatType _statType;
        [Tooltip("加成數值（可為百分比或固定值，依 statType 決定）")]
        [SerializeField] private float _value;
        [Tooltip("是否為百分比加成")]
        [SerializeField] private bool _isPercentage = true;

        // ── 公開屬性 ──
        public string UpgradeId => _upgradeId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public Core.Rarity Rarity => _rarity;
        public UpgradeStatType StatType => _statType;
        public float Value => _value;
        public bool IsPercentage => _isPercentage;
    }

    // UpgradeStatType 已移至 UpgradePool.cs
}
