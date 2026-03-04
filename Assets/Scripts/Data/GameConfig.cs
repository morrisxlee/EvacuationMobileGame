using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Data
{
    /// <summary>
    /// 全局遊戲設定：波次基礎時間、撤離條件、復活規則等。
    /// 在 Inspector 中調整，不需改程式碼。
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "SurvivalDemo/GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [TitleGroup("波次設定")]
        [Tooltip("遊戲開始後，首波敵人出現前的等待時間（秒）。會被 StageData 的難度係數修正。")]
        [LabelText("首波延遲（秒）")]
        [Min(5f)]
        [SerializeField] private float _firstWaveDelay = 40f;

        [TitleGroup("波次設定")]
        [Tooltip("每波敵人之間的基礎間隔時間（秒）。")]
        [LabelText("波次間隔（秒）")]
        [Min(3f)]
        [SerializeField] private float _waveInterval = 10f;

        [TitleGroup("波次設定")]
        [Tooltip("每幾波觸發一次狂暴波（血月）。狂暴波會生成更多精英。")]
        [LabelText("狂暴波間隔")]
        [Min(1)]
        [SerializeField] private int _rageWaveThreshold = 5;

        [TitleGroup("撤離設定")]
        [Tooltip("一般撤離觸發後，玩家需要防守的時間（秒）。")]
        [LabelText("撤離防守時間（秒）")]
        [Min(10f)]
        [SerializeField] private float _evacDefendDuration = 60f;

        [TitleGroup("撤離設定")]
        [Tooltip("撤離期間，敵人增援的基礎間隔時間（秒）。會隨時間加速。")]
        [LabelText("增援間隔（秒）")]
        [Min(1f)]
        [SerializeField] private float _evacReinforcementInterval = 5f;

        [TitleGroup("撤離設定")]
        [Tooltip("觸發緊急撤離（直接撤離，跳過防守）所需的電池數量。")]
        [LabelText("緊急撤離電池需求")]
        [Min(1)]
        [SerializeField] private int _emergencyEvacBatteryCost = 6;

        [TitleGroup("復活設定")]
        [Tooltip("單局遊戲中玩家最多可以復活的次數。")]
        [LabelText("最大復活次數")]
        [Min(0)]
        [SerializeField] private int _maxRevives = 3;

        [TitleGroup("復活設定")]
        [Tooltip("第一次復活消耗的電池數量。後續復活需觀看廣告。")]
        [LabelText("首次復活電池消耗")]
        [Min(0)]
        [SerializeField] private int _firstReviveBatteryCost = 1;

        [TitleGroup("效能設定")]
        [Tooltip("同時存在於場景中的敵人上限。超過此數量時會暫停生成新敵人。建議根據目標設備調整。")]
        [LabelText("同屏敵人上限")]
        [Min(10)]
        [SerializeField] private int _maxActiveEnemies = 100;

        [TitleGroup("精英怪公式")]
        [Tooltip("每經過幾個 Stage，精英怪數量增加 1 隻。")]
        [LabelText("精英成長間隔")]
        [Min(1)]
        [SerializeField] private int _eliteGrowthInterval = 3;

        [TitleGroup("精英怪公式")]
        [Tooltip("第一個 Stage 的精英怪基礎數量。")]
        [LabelText("精英基礎數量")]
        [Min(0)]
        [SerializeField] private int _eliteBaseCount = 1;

        [TitleGroup("精英怪公式")]
        [Tooltip("精英怪數量上限（不含狂暴波加成）。")]
        [LabelText("精英上限")]
        [Min(1)]
        [SerializeField] private int _eliteMaxCount = 6;

        [TitleGroup("精英怪公式")]
        [Tooltip("狂暴波時額外增加的精英怪數量。")]
        [LabelText("狂暴波精英加成")]
        [Min(0)]
        [SerializeField] private int _rageEliteBonus = 1;

        [TitleGroup("精英怪公式")]
        [Tooltip("精英怪的絕對上限（含狂暴波加成）。")]
        [LabelText("精英絕對上限")]
        [Min(1)]
        [SerializeField] private int _eliteAbsoluteMax = 7;

        // ── 公開屬性（唯讀） ──
        public float FirstWaveDelay => _firstWaveDelay;
        public float WaveInterval => _waveInterval;
        public int RageWaveThreshold => _rageWaveThreshold;
        public float EvacDefendDuration => _evacDefendDuration;
        public float EvacReinforcementInterval => _evacReinforcementInterval;
        public int EmergencyEvacBatteryCost => _emergencyEvacBatteryCost;
        public int MaxRevives => _maxRevives;
        public int FirstReviveBatteryCost => _firstReviveBatteryCost;
        public int MaxActiveEnemies => _maxActiveEnemies;
        public int EliteGrowthInterval => _eliteGrowthInterval;
        public int EliteBaseCount => _eliteBaseCount;
        public int EliteMaxCount => _eliteMaxCount;
        public int RageEliteBonus => _rageEliteBonus;
        public int EliteAbsoluteMax => _eliteAbsoluteMax;

        /// <summary>
        /// 計算指定 stage 的精英數量。
        /// EliteCount(stage) = min(1 + floor(stage / 3), 6)
        /// </summary>
        public int GetEliteCount(int stage, bool isRageWave)
        {
            int count = Mathf.Min(_eliteBaseCount + Mathf.FloorToInt((float)stage / _eliteGrowthInterval), _eliteMaxCount);
            if (isRageWave) count += _rageEliteBonus;
            return Mathf.Min(count, _eliteAbsoluteMax);
        }
    }
}
