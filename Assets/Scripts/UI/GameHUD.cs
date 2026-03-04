using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SurvivalDemo.Core;
using SurvivalDemo.AI;
using SurvivalDemo.Player;
using Sirenix.OdinInspector;

namespace SurvivalDemo.UI
{
    /// <summary>
    /// 遊戲 HUD 管理器。
    /// 掛在 Canvas 下的空 GameHUD GameObject 上。
    /// 所有 UI 元件透過 [SerializeField] 在 Inspector 拖入——不填則略過，不會報錯。
    ///
    /// 資料來源：
    ///   事件驅動（即時）→ HP、電池、金錢、波次號、血月等級、狂暴波提示
    ///   Poll（每幀）     → 波次倒計時、剩餘敵人數、活躍敵人數
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  Inspector 槽位（全部選填；不拖入則該欄位自動跳過）
        // ══════════════════════════════════════════════════════

        [TitleGroup("玩家狀態")]
        [Tooltip("顯示玩家目前 HP 數值的 TextMeshPro 元件。\n格式：「HP: 80 / 100」")]
        [LabelText("HP 文字")]
        [SerializeField] private TextMeshProUGUI _hpText;

        [TitleGroup("玩家狀態")]
        [Tooltip("玩家 HP 進度條（UnityEngine.UI.Slider）。\n最大值會在遊戲開始時從 PlayerStats 自動讀取。")]
        [LabelText("HP 進度條")]
        [SerializeField] private Slider _hpSlider;

        [TitleGroup("玩家狀態")]
        [Tooltip("顯示電池（Battery）數量的 TextMeshPro 元件。\n格式：「🔋 5」")]
        [LabelText("電池文字")]
        [SerializeField] private TextMeshProUGUI _batteryText;

        [TitleGroup("玩家狀態")]
        [Tooltip("顯示金錢數量的 TextMeshPro 元件。\n格式：「$ 120」")]
        [LabelText("金錢文字")]
        [SerializeField] private TextMeshProUGUI _currencyText;

        // ──────────────────────────────────────────────────────

        [TitleGroup("波次資訊")]
        [Tooltip("顯示目前波次編號的 TextMeshPro 元件。\n格式：「Wave 3」")]
        [LabelText("波次編號文字")]
        [SerializeField] private TextMeshProUGUI _waveNumberText;

        [TitleGroup("波次資訊")]
        [Tooltip("顯示波次狀態的 TextMeshPro 元件。\n進行中：「戰鬥中」；等待中：「下一波：12s」")]
        [LabelText("波次狀態文字")]
        [SerializeField] private TextMeshProUGUI _waveStateText;

        [TitleGroup("波次資訊")]
        [Tooltip("顯示本波剩餘敵人數量的 TextMeshPro 元件。\n格式：「剩餘：8 隻」")]
        [LabelText("剩餘敵人文字")]
        [SerializeField] private TextMeshProUGUI _enemiesRemainingText;

        [TitleGroup("波次資訊")]
        [Tooltip("顯示目前活躍（已生成、在場上）敵人數量的 TextMeshPro 元件。\n格式：「場上：15 隻」\n資料來源：EnemyTickManager.ActiveEnemyCount（poll）")]
        [LabelText("活躍敵人文字")]
        [SerializeField] private TextMeshProUGUI _activeEnemiesText;

        [TitleGroup("波次資訊")]
        [Tooltip("顯示血月等級的 TextMeshPro 元件。\n格式：「🌕 血月 Lv.2」\n等級為 0 時自動隱藏此元件。")]
        [LabelText("血月等級文字")]
        [SerializeField] private TextMeshProUGUI _bloodMoonText;

        // ──────────────────────────────────────────────────────

        [TitleGroup("狂暴波提示")]
        [Tooltip("狂暴波觸發時顯示的 GameObject（含 TMP 文字或特效）。\n觸發後顯示 _rageWaveDisplayDuration 秒後自動隱藏。\n開始時會自動隱藏，請務必拖入。")]
        [LabelText("狂暴波提示物件")]
        [SerializeField] private GameObject _rageWaveNotification;

        [TitleGroup("狂暴波提示")]
        [Tooltip("狂暴波提示顯示的持續秒數。")]
        [LabelText("提示持續時間（秒）")]
        [Min(0.5f)]
        [SerializeField] private float _rageWaveDisplayDuration = 3f;

        // ══════════════════════════════════════════════════════
        //  私有狀態
        // ══════════════════════════════════════════════════════

        private float _maxHP = 100f;
        private Coroutine _rageWaveCoroutine;

        // ══════════════════════════════════════════════════════
        //  Unity 生命週期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_rageWaveNotification != null)
                _rageWaveNotification.SetActive(false);
        }

        private void OnEnable()
        {
            GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
            GameEvents.OnBatteryChanged       += HandleBatteryChanged;
            GameEvents.OnCurrencyChanged      += HandleCurrencyChanged;
            GameEvents.OnWaveStarted          += HandleWaveStarted;
            GameEvents.OnWaveCleared          += HandleWaveCleared;
            GameEvents.OnBloodMoonLevelUp     += HandleBloodMoonLevelUp;
            GameEvents.OnRageWaveTriggered    += HandleRageWaveTriggered;
            GameEvents.OnGameStart            += HandleGameStart;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;
            GameEvents.OnBatteryChanged       -= HandleBatteryChanged;
            GameEvents.OnCurrencyChanged      -= HandleCurrencyChanged;
            GameEvents.OnWaveStarted          -= HandleWaveStarted;
            GameEvents.OnWaveCleared          -= HandleWaveCleared;
            GameEvents.OnBloodMoonLevelUp     -= HandleBloodMoonLevelUp;
            GameEvents.OnRageWaveTriggered    -= HandleRageWaveTriggered;
            GameEvents.OnGameStart            -= HandleGameStart;
        }

        private void Start()
        {
            // 嘗試從場景中已存在的 PlayerStats 讀取 MaxHP 初始值
            var stats = FindFirstObjectByType<PlayerStats>();
            if (stats != null)
            {
                _maxHP = stats.MaxHP;
                RefreshHP(stats.CurrentHP);
            }

            // 初始化血月文字狀態
            SetBloodMoonLevel(0);
        }

        /// <summary>
        /// 每幀 Poll WaveManager 與 EnemyTickManager，更新倒計時、剩餘敵人、活躍敵人。
        /// </summary>
        private void Update()
        {
            var waveMgr = WaveManager.Instance;
            if (waveMgr == null) return;

            // 波次倒計時 / 狀態
            if (_waveStateText != null)
            {
                if (waveMgr.IsWaitingForNextWave)
                    _waveStateText.text = $"下一波：{Mathf.CeilToInt(waveMgr.WaveTimer)}s";
                else if (waveMgr.IsWaveActive)
                    _waveStateText.text = "戰鬥中";
                else
                    _waveStateText.text = "—";
            }

            // 剩餘敵人
            if (_enemiesRemainingText != null)
                _enemiesRemainingText.text = $"剩餘：{waveMgr.EnemiesRemainingInWave} 隻";

            // 活躍敵人（EnemyTickManager）
            if (_activeEnemiesText != null)
            {
                int active = EnemyTickManager.Instance?.ActiveEnemyCount ?? 0;
                _activeEnemiesText.text = $"場上：{active} 隻";
            }
        }

        // ══════════════════════════════════════════════════════
        //  事件處理器
        // ══════════════════════════════════════════════════════

        private void HandleGameStart()
        {
            // 遊戲開始時重新讀取 MaxHP（可能在 MenuController 之後才觸發）
            var stats = FindFirstObjectByType<PlayerStats>();
            if (stats != null)
            {
                _maxHP = stats.MaxHP;
                RefreshHP(stats.CurrentHP);
            }
        }

        private void HandleHealthChanged(float currentHP)
        {
            RefreshHP(currentHP);
        }

        private void HandleBatteryChanged(int amount)
        {
            if (_batteryText != null)
                _batteryText.text = $"電池 {amount}";
        }

        private void HandleCurrencyChanged(int amount)
        {
            if (_currencyText != null)
                _currencyText.text = $"$ {amount}";
        }

        private void HandleWaveStarted(int waveIndex)
        {
            if (_waveNumberText != null)
                _waveNumberText.text = $"Wave {waveIndex + 1}";
        }

        private void HandleWaveCleared(int waveIndex)
        {
            if (_waveStateText != null)
                _waveStateText.text = "波次清除！";
        }

        private void HandleBloodMoonLevelUp(int newLevel)
        {
            SetBloodMoonLevel(newLevel);
        }

        private void HandleRageWaveTriggered()
        {
            if (_rageWaveNotification == null) return;

            if (_rageWaveCoroutine != null)
                StopCoroutine(_rageWaveCoroutine);
            _rageWaveCoroutine = StartCoroutine(ShowRageWaveNotification());
        }

        // ══════════════════════════════════════════════════════
        //  內部輔助
        // ══════════════════════════════════════════════════════

        private void RefreshHP(float currentHP)
        {
            if (_hpText != null)
                _hpText.text = $"HP {Mathf.CeilToInt(currentHP)} / {Mathf.CeilToInt(_maxHP)}";

            if (_hpSlider != null)
            {
                _hpSlider.maxValue = _maxHP;
                _hpSlider.value    = currentHP;
            }
        }

        private void SetBloodMoonLevel(int level)
        {
            if (_bloodMoonText == null) return;

            if (level <= 0)
            {
                _bloodMoonText.gameObject.SetActive(false);
            }
            else
            {
                _bloodMoonText.gameObject.SetActive(true);
                _bloodMoonText.text = $"血月 Lv.{level}";
            }
        }

        private IEnumerator ShowRageWaveNotification()
        {
            _rageWaveNotification.SetActive(true);
            yield return new WaitForSeconds(_rageWaveDisplayDuration);
            _rageWaveNotification.SetActive(false);
            _rageWaveCoroutine = null;
        }
    }
}
