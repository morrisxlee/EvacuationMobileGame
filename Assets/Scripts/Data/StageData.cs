using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// Stage 設定：定義該 stage 使用的敵人種類、波次曲線、難度係數等。
    /// 無限 stage 模式下可用基底 + 成長公式。
    /// </summary>
    [CreateAssetMenu(fileName = "NewStage", menuName = "SurvivalDemo/StageData")]
    public class StageData : ScriptableObject
    {
        [TitleGroup("基本資訊")]
        [ReadOnly, ShowInInspector, LabelText("關卡 ID（自動同步檔名）")]
        [InfoBox("此欄位會自動同步為 ScriptableObject 的檔名，無法手動修改。")]
        public string StageId => name;

        [TitleGroup("基本資訊")]
        [Tooltip("關卡的顯示名稱，用於 UI 顯示（例如「廢棄工廠」）。")]
        [LabelText("顯示名稱")]
        [SerializeField] private string _displayName;

        [TitleGroup("難度係數")]
        [Tooltip("影響首波延遲的乘數。小於 1 表示更快出怪，大於 1 表示更慢。")]
        [LabelText("首波延遲倍率")]
        [Min(0.1f)]
        [SerializeField] private float _firstWaveDelayMultiplier = 1f;

        [TitleGroup("難度係數")]
        [Tooltip("第一波的基礎敵人數量。")]
        [LabelText("每波基礎敵人數")]
        [Min(1)]
        [SerializeField] private int _baseEnemiesPerWave = 8;

        [TitleGroup("難度係數")]
        [Tooltip("每經過一波，敵人數量增加多少。")]
        [LabelText("每波敵人成長量")]
        [Min(0)]
        [SerializeField] private int _enemiesGrowthPerWave = 2;

        [TitleGroup("敵人配置")]
        [Tooltip("此關卡可生成的普通敵人類型。必須至少有一種，否則不會生成敵人！")]
        [LabelText("普通敵人")]
        [Required("必須至少設定一種普通敵人！")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        [SerializeField] private List<EnemyData> _normalEnemies = new();

        [TitleGroup("敵人配置")]
        [Tooltip("此關卡可生成的精英敵人類型。可留空，表示不生成精英。")]
        [LabelText("精英敵人")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        [SerializeField] private List<EnemyData> _eliteEnemies = new();

        // ── 公開屬性 ──
        public string DisplayName => _displayName;
        public float FirstWaveDelayMultiplier => _firstWaveDelayMultiplier;
        public int BaseEnemiesPerWave => _baseEnemiesPerWave;
        public int EnemiesGrowthPerWave => _enemiesGrowthPerWave;
        public IReadOnlyList<EnemyData> NormalEnemies => _normalEnemies;
        public IReadOnlyList<EnemyData> EliteEnemies => _eliteEnemies;

        /// <summary>
        /// 取得指定波次的怪物數量。
        /// </summary>
        public int GetEnemyCountForWave(int waveIndex)
        {
            return _baseEnemiesPerWave + _enemiesGrowthPerWave * waveIndex;
        }
    }
}
