using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 敵人資料表：每種敵人一個 SO 實例。
    /// 包含 HP、傷害、移速、攻擊方式等。
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemy", menuName = "SurvivalDemo/EnemyData")]
    public class EnemyData : ScriptableObject
    {
        [TitleGroup("基本資訊")]
        [ReadOnly, ShowInInspector, LabelText("敵人 ID（自動同步檔名）")]
        [InfoBox("此欄位會自動同步為 ScriptableObject 的檔名，無法手動修改。")]
        public string EnemyId => name;

        [TitleGroup("基本資訊")]
        [Tooltip("敵人的顯示名稱，用於 UI 顯示。")]
        [LabelText("顯示名稱")]
        [SerializeField] private string _displayName;

        [TitleGroup("基本資訊")]
        [Tooltip("敵人類型：Melee = 近戰（走到面前攻擊）、Ranged = 遠程（保持距離發射投射物）。")]
        [LabelText("敵人類型")]
        [SerializeField] private EnemyType _enemyType;

        [TitleGroup("基本資訊")]
        [Tooltip("勾選表示此敵人為精英怪，通常更強、更少出現。")]
        [LabelText("精英怪")]
        [SerializeField] private bool _isElite;

        [TitleGroup("戰鬥數值")]
        [Tooltip("敵人的基礎最大血量。會隨 Stage 成長。")]
        [LabelText("最大血量")]
        [Min(1f)]
        [SerializeField] private float _maxHP = 30f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("敵人的移動速度（世界單位/秒）。")]
        [LabelText("移動速度")]
        [Min(0.1f)]
        [SerializeField] private float _moveSpeed = 3f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("敵人的基礎傷害值。會隨 Stage 成長。")]
        [LabelText("傷害")]
        [Min(1f)]
        [SerializeField] private float _damage = 5f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("兩次攻擊之間的間隔時間（秒）。")]
        [LabelText("攻擊間隔（秒）")]
        [Min(0.1f)]
        [SerializeField] private float _attackInterval = 1f;

        [TitleGroup("戰鬥數值")]
        [Tooltip("敵人開始攻擊的距離。近戰建議 1~2，遠程建議 6~10。")]
        [LabelText("攻擊距離")]
        [Min(0.5f)]
        [SerializeField] private float _attackRange = 1.5f;

        [TitleGroup("遠程敵人專用")]
        [Tooltip("遠程敵人的投射物在物件池中的 ID。近戰敵人可留空。")]
        [LabelText("投射物池 ID")]
        [ShowIf("_enemyType", EnemyType.Ranged)]
        [SerializeField] private string _projectilePoolId;

        [TitleGroup("遠程敵人專用")]
        [Tooltip("遠程敵人投射物的飛行速度。")]
        [LabelText("投射物速度")]
        [ShowIf("_enemyType", EnemyType.Ranged)]
        [Min(1f)]
        [SerializeField] private float _projectileSpeed = 10f;

        [TitleGroup("難度成長")]
        [Tooltip("每經過一個 Stage，血量增加的百分比。0.1 表示每 Stage +10%。")]
        [LabelText("每 Stage 血量加成 %")]
        [Range(0f, 1f)]
        [SerializeField] private float _hpPerStage = 0.1f;

        [TitleGroup("難度成長")]
        [Tooltip("每經過一個 Stage，傷害增加的百分比。0.05 表示每 Stage +5%。")]
        [LabelText("每 Stage 傷害加成 %")]
        [Range(0f, 1f)]
        [SerializeField] private float _damagePerStage = 0.05f;

        [TitleGroup("物件池")]
        [Tooltip("此敵人 Prefab 在物件池（GenericPool）中的 ID。必須與 GenericPool 的 PoolEntry.id 完全一致。")]
        [LabelText("敵人池 ID")]
        [Required("必須填寫敵人池 ID，否則無法生成此敵人！")]
        [SerializeField] private string _poolId;

        // ── 公開屬性 ──
        public string DisplayName => _displayName;
        public EnemyType Type => _enemyType;
        public bool IsElite => _isElite;
        public float MaxHP => _maxHP;
        public float MoveSpeed => _moveSpeed;
        public float Damage => _damage;
        public float AttackInterval => _attackInterval;
        public float AttackRange => _attackRange;
        public string ProjectilePoolId => _projectilePoolId;
        public float ProjectileSpeed => _projectileSpeed;
        public string PoolId => _poolId;

        public float GetHPAtStage(int stage)
        {
            return _maxHP * (1f + _hpPerStage * stage);
        }

        public float GetDamageAtStage(int stage)
        {
            return _damage * (1f + _damagePerStage * stage);
        }
    }

    public enum EnemyType
    {
        [LabelText("近戰")] Melee,
        [LabelText("遠程")] Ranged
    }
}
