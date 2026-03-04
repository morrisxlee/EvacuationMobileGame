using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.AI
{
    /// <summary>
    /// 敵人動畫控制器：
    ///   1) 每幀讀取 Rigidbody2D 速度並更新子物件 Model 的 Animator float 參數「Speed」。
    ///      Speed > 0.1 → Walk；Speed &lt; 0.1 → Idle。
    ///   2) 依據水平移動方向翻轉 Model 子物件（預設朝右：Y=0；向左：Y=180）。
    ///
    /// 效能設計（針對 1000+ 敵人同時存在）：
    ///   1. Animator 引用在 Awake 快取，不每幀 GetComponent。
    ///   2. sqrMagnitude dirty check：速度不變時每幀零開銷（無 sqrt、無 SetFloat）。
    ///   3. OnEnable 重置快取，確保從物件池生成後第一幀強制更新 Animator。
    ///   4. 診斷 Log 以 LogOnce（prefab 名稱為 key）去重，1000 隻同類型敵人最多只打一次。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyAnimator : MonoBehaviour
    {
        private static readonly int SpeedHash    = Animator.StringToHash("Speed");
        private const float SqrSpeedEpsilon      = 0.0001f;
        private const float WalkThresholdSqr     = 0.1f * 0.1f;
        private const float FacingFlipThreshold  = 0.05f; // 水平速度超過此值才翻轉，避免抖動

        [TitleGroup("診斷設定")]
        [InfoBox("⚠ 1000+ 敵人同時存在時請勿開啟此選項！\n" +
                 "已使用 LogOnce 去重：同類型敵人最多只打一次 Log，但大量不同類型仍可能造成輸出量大。\n" +
                 "建議只在單隻測試敵人時開啟，確認動畫正常後立即關閉。", InfoMessageType.Warning)]
        [Tooltip("開啟後輸出 Idle ↔ Walk 狀態轉換的詳細 Log。\n" +
                 "以 LogOnce（prefab 名稱）去重，每種敵人類型最多輸出一次。\n" +
                 "受 DebugLogger 的 AI 類別開關控制，兩者都開才會輸出。")]
        [LabelText("詳細動畫診斷")]
        [SerializeField] private bool _verboseDiag = false;

        // ── 執行時引用 ──
        private Rigidbody2D _rb;
        private Animator    _animator;
        private Transform   _modelTransform;

        // ── dirty check 快取，-1f = 哨兵值確保首幀強制更新 ──
        private float _prevSqrSpeed = -1f;
        private bool  _isFacingRight = true;

        // ── 診斷用：prefab 名稱（去除 Unity 自動加的「(Clone)」，用作 LogOnce key）──
        private string _prefabTypeName;

        // ── Odin：執行期唯讀診斷欄位 ──
        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("目前傳入 Animator 的 Speed 值（world unit/s）。Play 模式下即時更新。")]
        [ShowInInspector, ReadOnly, LabelText("rb.velocity 速度")]
        private float DebugSpeed => _rb != null ? _rb.velocity.magnitude : 0f;

        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("偵測到的 Animator 元件掛在哪個物件上。若為空代表 Awake 時未找到。")]
        [ShowInInspector, ReadOnly, LabelText("Animator 所在物件")]
        private string DebugAnimatorObject => _animator != null ? _animator.gameObject.name : "（未找到）";

        [TitleGroup("執行期診斷（唯讀）")]
        [Tooltip("目前 Model 子物件朝向。True=朝右（Y=0），False=朝左（Y=180）。")]
        [ShowInInspector, ReadOnly, LabelText("朝右")]
        private bool DebugFacingRight => _isFacingRight;

        private void Awake()
        {
            _rb             = GetComponent<Rigidbody2D>();
            _animator       = GetComponentInChildren<Animator>();
            _prefabTypeName = gameObject.name.Replace("(Clone)", "").Trim();

            // ── 根因 1：找不到 Animator（LogOnceError 以 prefab 名稱去重，1000 隻只報一次）──
            if (_animator == null)
            {
                Core.DebugLogger.LogOnceError(
                    $"EnemyAnim_NoAnimator_{_prefabTypeName}",
                    $"[EnemyAnimator] 根因：敵人 Prefab「{_prefabTypeName}」的所有子物件中找不到 Animator 元件！\n" +
                    "  必要階層：Enemy（Root）→ Model（子物件）→ Animator（掛在 Model 上）\n" +
                    "  修復：選取 Enemy Prefab 的 Model 子物件 → Add Component → Animator\n" +
                    "  此訊息針對同類型 Prefab 只輸出一次（使用 LogOnce 去重）。",
                    Core.LogCategory.AI);
                return;
            }

            // ── 根因 2：Animator Controller 未指定 ──
            if (_animator.runtimeAnimatorController == null)
            {
                Core.DebugLogger.LogOnceError(
                    $"EnemyAnim_NoController_{_prefabTypeName}",
                    $"[EnemyAnimator] 根因：敵人 Prefab「{_prefabTypeName}」的 Animator 元件沒有指定 Animator Controller！\n" +
                    "  動畫完全不會運作，SetFloat 呼叫全部被忽略。\n" +
                    "  修復：選取 Model 子物件 → Animator → Controller 欄位 → 拖入你的 Animator Controller Asset",
                    Core.LogCategory.AI);
                return;
            }

            // ── 根因 3：Animator Controller 中沒有名為「Speed」的 Float 參數 ──
            // SetFloat 在參數不存在時完全靜默失敗，是最常見的無報錯不運作根因
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
                Core.DebugLogger.LogOnceError(
                    $"EnemyAnim_NoSpeedParam_{_prefabTypeName}",
                    $"[EnemyAnimator] 根因：敵人 Prefab「{_prefabTypeName}」的 Animator Controller" +
                    $"「{_animator.runtimeAnimatorController.name}」中找不到名為「Speed」的 Float 參數！\n" +
                    "  SetFloat(\"Speed\", ...) 呼叫完全靜默無效，動畫不會切換。\n" +
                    "  修復步驟：\n" +
                    "    1) 雙擊打開 Animator Controller\n" +
                    "    2) 左側 Parameters 面板 → 點「+」→ 選「Float」\n" +
                    "    3) 命名為「Speed」（大小寫完全一致）\n" +
                    "    4) Idle → Walk Transition Condition：Speed Greater 0.1\n" +
                    "    5) Walk → Idle Transition Condition：Speed Less 0.1",
                    Core.LogCategory.AI);
                _animator = null;
                return;
            }

            _modelTransform = _animator.transform;
            _isFacingRight = Mathf.Abs(Mathf.DeltaAngle(_modelTransform.localEulerAngles.y, 0f)) <= 90f;

            Core.DebugLogger.LogOnce(
                $"EnemyAnim_Init_{_prefabTypeName}",
                $"[EnemyAnimator] 初始化成功（類型：{_prefabTypeName}）。\n" +
                $"  Animator 位於子物件：'{_animator.gameObject.name}'\n" +
                $"  Controller：{_animator.runtimeAnimatorController.name}\n" +
                $"  初始朝向：{(_isFacingRight ? "右（Y=0）" : "左（Y=180）")}",
                Core.LogCategory.AI);
        }

        private void OnEnable()
        {
            // 每次從物件池生成時重置快取，強制下一幀更新 Animator 狀態
            _prevSqrSpeed = -1f;
        }

        private void Update()
        {
            if (_animator == null) return;

            float sqrSpeed = _rb.velocity.sqrMagnitude;

            if (Mathf.Abs(sqrSpeed - _prevSqrSpeed) > SqrSpeedEpsilon)
            {
                float speed = Mathf.Sqrt(sqrSpeed);
                _animator.SetFloat(SpeedHash, speed);
                _prevSqrSpeed = sqrSpeed;

                // ── 詳細診斷：Idle ↔ Walk 轉換（LogOnce 以 prefab 名稱 + 狀態去重）──
                // 每種敵人類型每個狀態最多輸出一次，防止 1000 隻同時觸發 log 爆炸
                if (_verboseDiag)
                {
                    bool isMoving = sqrSpeed > WalkThresholdSqr;
                    string stateKey = $"EnemyAnim_State_{_prefabTypeName}_{(isMoving ? "Walk" : "Idle")}";
                    Core.DebugLogger.LogOnce(
                        stateKey,
                        $"[EnemyAnimator] 動畫狀態轉換（{_prefabTypeName}）→ {(isMoving ? "Walk（Speed > 0.1）" : "Idle（Speed < 0.1）")}\n" +
                        $"  rb.velocity={_rb.velocity}  Speed={speed:F3}\n" +
                        $"  ⚠ 此訊息對同類型敵人每種狀態只輸出一次（LogOnce 去重）",
                        Core.LogCategory.AI);
                }
            }

            // 依水平速度翻轉敵人朝向（預設朝右，向左移動時 Y=180）
            UpdateFacingByVelocityX();
        }

        /// <summary>
        /// 依據 rb.velocity.x 翻轉 Model 子物件的 Y 軸方向。
        /// |velocity.x| 小於閾值時不翻轉，避免純垂直移動時來回抖動。
        /// </summary>
        private void UpdateFacingByVelocityX()
        {
            float vx = _rb.velocity.x;
            if (Mathf.Abs(vx) < FacingFlipThreshold) return;

            bool shouldFaceRight = vx > 0f;
            if (shouldFaceRight == _isFacingRight) return;

            _isFacingRight = shouldFaceRight;
            _modelTransform.localEulerAngles = new Vector3(0f, _isFacingRight ? 0f : 180f, 0f);

            if (_verboseDiag)
            {
                string key = $"EnemyAnim_Facing_{_prefabTypeName}_{(_isFacingRight ? "Right" : "Left")}";
                Core.DebugLogger.LogOnce(
                    key,
                    $"[EnemyAnimator] 朝向翻轉（{_prefabTypeName}）→ {(_isFacingRight ? "右（Y=0）" : "左（Y=180）")}\n" +
                    $"  velocity.x={vx:F3}  flipThreshold={FacingFlipThreshold}\n" +
                    $"  ⚠ 此訊息對同類型敵人每個方向只輸出一次（LogOnce 去重）",
                    Core.LogCategory.AI);
            }
        }
    }
}
