using System;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 統一事件匯流排（C# static event）。
    /// 所有跨系統溝通透過這裡發送，避免直接耦合。
    /// 訂閱端務必在 OnDisable / OnDestroy 取消訂閱以防記憶體洩漏。
    /// </summary>
    public static class GameEvents
    {
        // ── 遊戲狀態 ──
        public static event Action OnGameStart;
        public static event Action OnGameOver;
        public static event Action OnGamePause;
        public static event Action OnGameResume;
        public static event Action<GameState> OnGameStateChanged;

        // ── 波次 ──
        public static event Action<int> OnWaveStarted;        // waveIndex
        public static event Action<int> OnWaveCleared;         // waveIndex
        public static event Action<int> OnBloodMoonLevelUp;    // newLevel
        public static event Action OnRageWaveTriggered;

        // ── 搜索 / 互動 ──
        public static event Action<SearchResultData> OnTreasureFound;
        public static event Action OnSearchStarted;
        public static event Action OnSearchCompleted;
        public static event Action<float> OnSearchProgress;    // 0~1

        // ── 門 ──
        public static event Action<int> OnDoorPatched;         // doorId
        public static event Action<int> OnDoorDestroyed;       // doorId

        // ── 撤離 ──
        public static event Action OnEvacCalled;
        public static event Action OnEvacCompleted;
        public static event Action OnEmergencyEvac;

        // ── 倖存者 ──
        public static event Action<int> OnSurvivorRescued;     // survivorId

        // ── 升級 ──
        public static event Action OnUpgradeReady;
        public static event Action<UpgradeChoiceData> OnUpgradeChosen;

        // ── 玩家 ──
        public static event Action<float> OnPlayerHealthChanged;              // currentHP
        public static event Action OnPlayerDied;
        public static event Action<ReviveOpportunityData> OnReviveOpportunity; // 復活選擇機會
        public static event Action OnPlayerRevived;
        public static event Action<int> OnCurrencyChanged;                    // newAmount
        public static event Action<int> OnBatteryChanged;                     // newAmount
        public static event Action<int> OnSurvivorCurrencyChanged;            // newAmount

        // ── 敵人 ──
        public static event Action<int> OnEnemyKilled;           // enemyId
        public static event Action<int> OnEliteSpawned;          // eliteId

        // ── 結算 ──
        public static event Action<ResultType> OnGameResult;

        // ══════════════════════════════════════
        //  觸發方法（Fire methods）
        // ══════════════════════════════════════

        // ── 遊戲狀態 ──
        public static void FireGameStart() => OnGameStart?.Invoke();
        public static void FireGameOver() => OnGameOver?.Invoke();
        public static void FireGamePause() => OnGamePause?.Invoke();
        public static void FireGameResume() => OnGameResume?.Invoke();
        public static void FireGameStateChanged(GameState state) => OnGameStateChanged?.Invoke(state);

        // ── 波次 ──
        public static void FireWaveStarted(int idx) => OnWaveStarted?.Invoke(idx);
        public static void FireWaveCleared(int idx) => OnWaveCleared?.Invoke(idx);
        public static void FireBloodMoonLevelUp(int lvl) => OnBloodMoonLevelUp?.Invoke(lvl);
        public static void FireRageWaveTriggered() => OnRageWaveTriggered?.Invoke();

        // ── 搜索 / 互動 ──
        public static void FireTreasureFound(SearchResultData data) => OnTreasureFound?.Invoke(data);
        public static void FireSearchStarted() => OnSearchStarted?.Invoke();
        public static void FireSearchCompleted() => OnSearchCompleted?.Invoke();
        public static void FireSearchProgress(float t) => OnSearchProgress?.Invoke(t);

        // ── 門 ──
        public static void FireDoorPatched(int id) => OnDoorPatched?.Invoke(id);
        public static void FireDoorDestroyed(int id) => OnDoorDestroyed?.Invoke(id);

        // ── 撤離 ──
        public static void FireEvacCalled() => OnEvacCalled?.Invoke();
        public static void FireEvacCompleted() => OnEvacCompleted?.Invoke();
        public static void FireEmergencyEvac() => OnEmergencyEvac?.Invoke();

        // ── 倖存者 ──
        public static void FireSurvivorRescued(int id) => OnSurvivorRescued?.Invoke(id);

        // ── 升級 ──
        public static void FireUpgradeReady() => OnUpgradeReady?.Invoke();
        public static void FireUpgradeChosen(UpgradeChoiceData data) => OnUpgradeChosen?.Invoke(data);

        // ── 玩家 ──
        public static void FirePlayerHealthChanged(float hp) => OnPlayerHealthChanged?.Invoke(hp);
        public static void FirePlayerDied() => OnPlayerDied?.Invoke();
        public static void FireReviveOpportunity(ReviveOpportunityData data) => OnReviveOpportunity?.Invoke(data);
        public static void FirePlayerRevived() => OnPlayerRevived?.Invoke();
        public static void FireCurrencyChanged(int amount) => OnCurrencyChanged?.Invoke(amount);
        public static void FireBatteryChanged(int amount) => OnBatteryChanged?.Invoke(amount);
        public static void FireSurvivorCurrencyChanged(int amount) => OnSurvivorCurrencyChanged?.Invoke(amount);

        // ── 敵人 ──
        public static void FireEnemyKilled(int id) => OnEnemyKilled?.Invoke(id);
        public static void FireEliteSpawned(int id) => OnEliteSpawned?.Invoke(id);

        // ── 結算 ──
        public static void FireGameResult(ResultType t) => OnGameResult?.Invoke(t);
    }

    // ══════════════════════════════════════
    //  事件用資料結構
    // ══════════════════════════════════════

    public enum GameState
    {
        Menu,
        Playing,
        Paused,
        Evacuation,
        Reviving,   // 玩家死亡等待復活選擇（timeScale=0）
        Result
    }

    public struct SearchResultData
    {
        public SearchRewardType RewardType;
        public Rarity Rarity;
        public int Amount;
    }

    public enum SearchRewardType
    {
        Currency,
        Upgrade,
        Heal,
        Battery,
        Key
    }

    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    public struct UpgradeChoiceData
    {
        public int ChosenIndex;   // 0, 1, 2
        public Rarity Rarity;
        public string UpgradeId;
    }

    /// <summary>
    /// 遊戲結算類型：決定 ResultPanel 顯示的標題與樣式。
    /// </summary>
    public enum ResultType
    {
        GameOver,       // 玩家耗盡復活機會後死亡
        EvacSuccess,    // 正常撤離防守成功
        EmergencyEvac   // 電池滿格觸發緊急撤離
    }

    /// <summary>
    /// 玩家死亡時傳遞給 RevivePanel 的復活機會資料。
    /// </summary>
    public struct ReviveOpportunityData
    {
        public int ReviveCount;     // 目前已復活次數（0-based；0 = 首次機會）
        public int MaxRevives;      // 本局最大復活次數上限
        public int Battery;         // 目前電池數量
        public int BatteryCost;     // 首次復活所需電池
        public bool CanUseBattery;  // 本次可用電池復活（reviveCount==0 且電池足夠）
        public bool CanWatchAd;     // 本次可看廣告復活（廣告可用）
    }
}
