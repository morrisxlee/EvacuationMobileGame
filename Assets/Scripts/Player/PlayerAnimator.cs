using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 玩家動畫控制器：
    ///   1. 每幀讀取 Rigidbody2D 速度，更新子物件 Model 的 Animator float 參數「Speed」。
    ///      Speed > 0.1 → Walk；Speed &lt; 0.1 → Idle。
    ///   2. 根據移動水平方向翻轉 Model 子物件（Y 旋轉 0° = 朝右，180° = 朝左）。
    ///      預設朝右；水平速度超過閾值才觸發翻轉，避免純垂直移動時方向抖動。
    /// 死亡時強制 Speed = 0，不再翻轉方向。
    /// 不依賴任何 Inspector 插槽——所有引用在 Awake 自動取得，缺少元件時輸出 Error Log。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerAnimator : MonoBehaviour
    {
        // ── Animator 參數 Hash（static 確保只計算一次）──
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private const float SqrSpeedEpsilon  = 0.0001f;
        private const float WalkThresholdSqr  = 0.1f * 0.1f;

        // 水平速度需超過此閾值才觸發方向翻轉，避免純垂直移動時頻繁抖動
        private const float FacingFlipThreshold = 0.05f;

        [TitleGroup("診斷設定")]
        [InfoBox("開啟後會在 Console 輸出動畫狀態轉換（Idle ↔ Walk）與方向翻轉的時間點和速度值。\n" +
                 "僅供定位問題使用，確認正常後請關閉。")]
        [Tooltip("開啟後輸出 Idle ↔ Walk 狀態轉換及左右翻轉的詳細 Log。\n" +
                 "受 DebugLogger 的 Player 類別開關控制，兩者都開才會輸出。")]
        [LabelText("詳細動畫診斷")]
        [SerializeField] private bool _verboseDiag = false;

        // ── 執行時引用（Awake 自動取得）──
        private Rigidbody2D _rb;
        private Animator    _animator;
        private PlayerStats _playerStats;
        private Transform   _modelTransform; // Model 子物件的 Transform，用於方向翻轉

        // ── dirty check 快取 ──
        private float _prevSqrSpeed = -1f;

        // ── 方向狀態快取（避免每幀重複設定 Rotation）──
        private bool _isFacingRight = true; // 預設朝右（Y rotation = 0）

        // ── 診斷狀態追蹤 ──
        private bool _wasMoving         = false;
        private bool _firstUpdateLogged = false;

        // ── Odin：執行期唯讀診斷欄位 ──
        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("目前傳入 Animator 的 Speed 值（world unit/s）。Play 模式下即時更新。")]
        [ShowInInspector, ReadOnly, LabelText("rb.velocity 速度")]
        private float DebugSpeed => _rb != null ? _rb.velocity.magnitude : 0f;

        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("目前 Model 子物件的朝向。True = 朝右（Y 0°），False = 朝左（Y 180°）。")]
        [ShowInInspector, ReadOnly, LabelText("朝右")]
        private bool DebugFacingRight => _isFacingRight;

        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("目前 PlayerStats.IsDead 的狀態。死亡時 Speed 強制歸零、不再翻轉。")]
        [ShowInInspector, ReadOnly, LabelText("玩家已死亡")]
        private bool DebugIsDead => _playerStats != null && _playerStats.IsDead;

        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("偵測到的 Animator 元件掛在哪個物件上。若為空代表 Awake 時未找到。")]
        [ShowInInspector, ReadOnly, LabelText("Animator 所在物件")]
        private string DebugAnimatorObject => _animator != null ? _animator.gameObject.name : "（未找到）";

        private void Awake()
        {
            _rb          = GetComponent<Rigidbody2D>();
            _playerStats = GetComponent<PlayerStats>();
            _animator    = GetComponentInChildren<Animator>();

            // ── 根因 1：找不到 Animator ──
            if (_animator == null)
            {
                Core.DebugLogger.LogError(
                    $"[PlayerAnimator] 根因：'{gameObject.name}' 的所有子物件中找不到 Animator 元件！\n" +
                    "  必要階層：Player（Root）→ Model（子物件）→ Animator（掛在 Model 上）\n" +
                    "  修復：選取 Model 子物件 → Add Component → Animator",
                    Core.LogCategory.Player);
                return;
            }

            // ── 根因 2：Animator Controller 未指定 ──
            if (_animator.runtimeAnimatorController == null)
            {
                Core.DebugLogger.LogError(
                    $"[PlayerAnimator] 根因：'{_animator.gameObject.name}' 的 Animator 元件沒有指定 Animator Controller！\n" +
                    "  動畫完全不會運作，SetFloat 呼叫全部被忽略。\n" +
                    "  修復：選取 Model 子物件 → Animator → Controller 欄位 → 拖入你的 Animator Controller Asset",
                    Core.LogCategory.Player);
                return;
            }

            // ── 根因 3：Animator Controller 中沒有名為「Speed」的 Float 參數 ──
            // SetFloat 在參數不存在時完全靜默失敗，這是最常見的無報錯不運作根因
            bool speedParamFound = false;
            foreach (var param in _animator.parameters)
            {
                if (param.nameHash == SpeedHash &&
                    param.type == AnimatorControllerParameterType.Float)
                {
                    speedParamFound = true;
                    break;
                }
            }
            if (!speedParamFound)
            {
                Core.DebugLogger.LogError(
                    $"[PlayerAnimator] 根因：Animator Controller「{_animator.runtimeAnimatorController.name}」\n" +
                    "  中找不到名為「Speed」的 Float 參數！\n" +
                    "  SetFloat(\"Speed\", ...) 呼叫完全靜默無效，動畫不會切換。\n" +
                    "  修復步驟：\n" +
                    "    1) 雙擊打開 Animator Controller\n" +
                    "    2) 左側 Parameters 面板 → 點「+」→ 選「Float」\n" +
                    "    3) 命名為「Speed」（大小寫完全一致，注意不要多空格）\n" +
                    "    4) 設定 Idle → Walk Transition Condition：Speed Greater 0.1\n" +
                    "    5) 設定 Walk → Idle Transition Condition：Speed Less 0.1",
                    Core.LogCategory.Player);
                _animator = null; // 讓 Update 提前 return，避免無效 SetFloat
                return;
            }

            // ── 根因 4：PlayerStats 未找到 ──
            if (_playerStats == null)
                Core.DebugLogger.LogError(
                    $"[PlayerAnimator] '{gameObject.name}' 找不到 PlayerStats！\n" +
                    "  PlayerAnimator 需與 PlayerStats 掛在同一個 Root 物件上。\n" +
                    "  死亡判定將失效，Speed 不會在死亡時歸零。",
                    Core.LogCategory.Player);

            // ── 快取 Model Transform（Animator 所在的子物件，即翻轉目標）──
            _modelTransform = _animator.transform;

            Core.DebugLogger.Log(
                $"[PlayerAnimator] 初始化成功。\n" +
                $"  Animator／Model 位於子物件：'{_modelTransform.gameObject.name}'\n" +
                $"  Controller：{_animator.runtimeAnimatorController.name}\n" +
                $"  翻轉閾值：水平速度 > {FacingFlipThreshold}",
                Core.LogCategory.Player);
        }

        private void Update()
        {
            if (_animator == null) return;

            bool isDead    = _playerStats != null && _playerStats.IsDead;
            float sqrSpeed = isDead ? 0f : _rb.velocity.sqrMagnitude;

            // ── 詳細診斷：首次 Update 快照（只打一次）──
            if (!_firstUpdateLogged && _verboseDiag)
            {
                _firstUpdateLogged = true;
                Core.DebugLogger.Log(
                    $"[PlayerAnimator] 首次 Update 快照\n" +
                    $"  rb.velocity={_rb.velocity}  speed={Mathf.Sqrt(sqrSpeed):F3}  isDead={isDead}\n" +
                    $"  AnimatorController={_animator.runtimeAnimatorController?.name ?? "null"}",
                    Core.LogCategory.Player);
            }

            // ── Animator Speed 更新（dirty check）──
            if (Mathf.Abs(sqrSpeed - _prevSqrSpeed) > SqrSpeedEpsilon)
            {
                float speed = Mathf.Sqrt(sqrSpeed);
                _animator.SetFloat(SpeedHash, speed);
                _prevSqrSpeed = sqrSpeed;

                if (_verboseDiag)
                {
                    bool isMoving = sqrSpeed > WalkThresholdSqr;
                    if (isMoving != _wasMoving)
                    {
                        _wasMoving = isMoving;
                        Core.DebugLogger.Log(
                            $"[PlayerAnimator] 動畫狀態轉換 → {(isMoving ? "Walk（Speed > 0.1）" : "Idle（Speed < 0.1）")}\n" +
                            $"  rb.velocity={_rb.velocity}  Speed={speed:F3}  isDead={isDead}",
                            Core.LogCategory.Player);
                    }
                }
            }

            // ── Model 朝向翻轉（根據水平速度方向）──
            UpdateFacing(isDead);
        }

        /// <summary>
        /// 依據 rb.velocity.x 翻轉 Model 子物件的 Y 軸旋轉。
        /// 水平速度需超過 FacingFlipThreshold 才觸發，避免純垂直移動時抖動。
        /// 死亡時不翻轉，保持死前的朝向。
        /// </summary>
        private void UpdateFacing(bool isDead)
        {
            if (isDead) return;

            float vx = _rb.velocity.x;

            // 水平速度不足閾值，維持目前朝向不變
            if (Mathf.Abs(vx) < FacingFlipThreshold) return;

            bool shouldFaceRight = vx > 0f;
            if (shouldFaceRight == _isFacingRight) return; // 方向未改變，跳過

            _isFacingRight = shouldFaceRight;
            _modelTransform.localEulerAngles = new Vector3(0f, _isFacingRight ? 0f : 180f, 0f);

            if (_verboseDiag)
                Core.DebugLogger.Log(
                    $"[PlayerAnimator] 方向翻轉 → {(_isFacingRight ? "朝右（Y 0°）" : "朝左（Y 180°）")}\n" +
                    $"  velocity.x={vx:F3}  閾值={FacingFlipThreshold}",
                    Core.LogCategory.Player);
        }
    }
}
