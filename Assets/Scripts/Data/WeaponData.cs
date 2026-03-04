using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 武器資料表：每把武器一個 SO 實例。
    /// 自動攻擊射程、傷害、射速等皆可獨立設定。
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "SurvivalDemo/WeaponData")]
    public class WeaponData : ScriptableObject
    {
        [TitleGroup("基本資訊")]
        [ReadOnly, ShowInInspector, LabelText("武器 ID（自動同步檔名）")]
        [InfoBox("此欄位會自動同步為 ScriptableObject 的檔名，無法手動修改。")]
        public string WeaponId => name;

        [TitleGroup("基本資訊")]
        [Tooltip("武器的顯示名稱，用於 UI 顯示。")]
        [LabelText("顯示名稱")]
        [SerializeField] private string _displayName;

        [TitleGroup("基本資訊")]
        [Tooltip("武器的圖示，用於 UI 顯示。")]
        [LabelText("圖示")]
        [PreviewField(50, ObjectFieldAlignment.Left)]
        [SerializeField] private Sprite _icon;

        [TitleGroup("基本資訊")]
        [Tooltip("武器類型：AutoRifle = 自動步槍（單發）、Shotgun = 霰彈槍（多發散射）。")]
        [LabelText("武器類型")]
        [SerializeField] private WeaponType _weaponType;

        [TitleGroup("戰鬥數值")]
        [Tooltip("武器的基礎傷害值，升級時會以此為基底計算。")]
        [LabelText("基礎傷害")]
        [Min(0.1f)]
        [SerializeField] private float _baseDamage = 10f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("每秒發射次數。數值越高攻擊越頻繁。")]
        [LabelText("射速（發/秒）")]
        [Min(0.1f)]
        [SerializeField] private float _fireRate = 2f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("自動鎖定敵人的最大距離（世界單位）。超出此範圍不會攻擊。")]
        [LabelText("攻擊範圍")]
        [Min(0.5f)]
        [SerializeField] private float _attackRange = 5f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("投射物飛行速度。設為 0 表示命中掃描型（瞬間命中）。")]
        [LabelText("投射物速度")]
        [Min(0f)]
        [SerializeField] private float _projectileSpeed = 20f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("每次攻擊發射的子彈數量。霰彈槍通常設為 3~7。")]
        [LabelText("子彈數量")]
        [Min(1)]
        [SerializeField] private int _pelletCount = 1;

        [TitleGroup("戰鬥數值")]
        [Tooltip("子彈散射的角度範圍（度）。0 = 無散射，30 = 左右各 15 度。")]
        [LabelText("散射角度")]
        [Range(0f, 90f)]
        [SerializeField] private float _spreadAngle = 0f;

        [TitleGroup("投射物")]
        [Tooltip("子彈 Prefab 在物件池（GenericPool）中的 ID。必須與 GenericPool 的 PoolEntry.id 完全一致。")]
        [LabelText("投射物池 ID")]
        [Required("必須填寫投射物池 ID，否則無法發射子彈！")]
        [SerializeField] private string _projectilePoolId;

        [TitleGroup("升級成長")]
        [Tooltip("每升一級，傷害增加的百分比。0.15 表示每級 +15%。")]
        [LabelText("每級傷害加成 %")]
        [Range(0f, 1f)]
        [SerializeField] private float _damagePerLevel = 0.15f;

        [TitleGroup("升級成長")]
        [Tooltip("每升一級，射速增加的百分比。0.05 表示每級 +5%。")]
        [LabelText("每級射速加成 %")]
        [Range(0f, 1f)]
        [SerializeField] private float _fireRatePerLevel = 0.05f;

        // ── 公開屬性 ──
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public WeaponType Type => _weaponType;
        public float BaseDamage => _baseDamage;
        public float FireRate => _fireRate;
        public float AttackRange => _attackRange;
        public float ProjectileSpeed => _projectileSpeed;
        public int PelletCount => _pelletCount;
        public float SpreadAngle => _spreadAngle;
        public string ProjectilePoolId => _projectilePoolId;
        public float DamagePerLevel => _damagePerLevel;
        public float FireRatePerLevel => _fireRatePerLevel;

        /// <summary>
        /// 取得指定等級的實際傷害。
        /// </summary>
        public float GetDamageAtLevel(int level)
        {
            return _baseDamage * (1f + _damagePerLevel * level);
        }

        /// <summary>
        /// 取得指定等級的實際射速。
        /// </summary>
        public float GetFireRateAtLevel(int level)
        {
            return _fireRate * (1f + _fireRatePerLevel * level);
        }
    }

    public enum WeaponType
    {
        [LabelText("自動步槍")] AutoRifle,
        [LabelText("霰彈槍")] Shotgun
    }
}
