using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Feedback
{
    /// <summary>
    /// MMF 回饋橋接層：透過可插拔介面觸發回饋效果。
    /// 若 Feel 未安裝或插槽未指定，僅輸出日誌不會炸（有 Toggle 控制）。
    /// 使用方式：在需要回饋的物件上掛此腳本，將場景中的 MMF_Player 拖入對應插槽。
    /// 注意：搜索中 (OnSearchStart) 應掛循環型 MMF_Player，並搭配 OnSearchStop 呼叫停止。
    /// </summary>
    public class FeedbackBridge : MonoBehaviour
    {
        [TitleGroup("通用回饋")]
        [Tooltip("物件受到攻擊時觸發（玩家/敵人受擊閃白、音效等）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("受擊 (OnHit)")]
        [SerializeField] private FeedbackSlot _onHit;

        [TitleGroup("通用回饋")]
        [Tooltip("物件死亡/被摧毀時觸發（爆炸、消散特效等）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("死亡 (OnDeath)")]
        [SerializeField] private FeedbackSlot _onDeath;

        [TitleGroup("通用回饋")]
        [Tooltip("武器開火時觸發（槍口閃光、音效等）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("開火 (OnFire)")]
        [SerializeField] private FeedbackSlot _onFire;

        [TitleGroup("通用回饋")]
        [Tooltip("玩家獲得升級時觸發（光芒特效、升級音效）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("升級 (OnUpgrade)")]
        [SerializeField] private FeedbackSlot _onUpgrade;

        [TitleGroup("通用回饋")]
        [Tooltip("玩家復活時觸發（復活光效、音效等）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("復活 (OnRevive)")]
        [SerializeField] private FeedbackSlot _onRevive;

        [TitleGroup("搜索回饋")]
        [InfoBox("OnSearchStart 建議搭配循環型 MMF_Player（例如讀條音效持續播放），並使用 OnSearchStop 停止循環。")]
        [Tooltip("玩家開始搜索讀條時觸發。\n建議在 MMF_Player 上設定循環，讓讀條過程中持續播放。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("搜索中 (OnSearchStart)")]
        [SerializeField] private FeedbackSlot _onSearchStart;

        [TitleGroup("搜索回饋")]
        [Tooltip("玩家搜索中斷（離開範圍/死亡）時觸發停止循環。\n呼叫 MMF_Player.StopFeedbacks() 以停止上方的循環回饋。\n拖入與 OnSearchStart 相同的 MMF_Player GameObject。")]
        [LabelText("搜索中斷 (OnSearchStop)")]
        [SerializeField] private FeedbackSlot _onSearchStop;

        [TitleGroup("搜索回饋")]
        [Tooltip("搜索讀條完成並獲得獎勵時觸發（完成音效、獎勵特效等）。\n拖入場景中對應的 MMF_Player GameObject。")]
        [LabelText("搜索完成 (OnSearchComplete)")]
        [SerializeField] private FeedbackSlot _onSearchComplete;

        // ── 通用 ──
        public void PlayHit()            => _onHit.Play(gameObject.name, "OnHit");
        public void PlayDeath()          => _onDeath.Play(gameObject.name, "OnDeath");
        public void PlayFire()           => _onFire.Play(gameObject.name, "OnFire");
        public void PlayUpgrade()        => _onUpgrade.Play(gameObject.name, "OnUpgrade");
        public void PlayRevive()         => _onRevive.Play(gameObject.name, "OnRevive");

        // ── 搜索 ──
        public void PlaySearchStart()    => _onSearchStart.Play(gameObject.name, "OnSearchStart");
        public void PlaySearchStop()     => _onSearchStop.Stop(gameObject.name, "OnSearchStop");
        public void PlaySearchComplete() => _onSearchComplete.Play(gameObject.name, "OnSearchComplete");

    }

    /// <summary>
    /// 單一回饋插槽：包裝 MMF_Player 的引用。
    /// Play()  → SendMessage("PlayFeedbacks")
    /// Stop()  → SendMessage("StopFeedbacks")，用於停止循環型 MMF。
    /// 若未指定則輸出 Log（受 DebugLogger Toggle 控制），不報錯、不 Fallback。
    /// </summary>
    [System.Serializable]
    public struct FeedbackSlot
    {
        [Tooltip("拖入場景中的 MMF_Player 物件。可留空（留空只輸出提示 Log，不影響遊戲）。")]
        [LabelText("MMF Player")]
        [SerializeField] private GameObject _feedbackPlayer;

        /// <summary>
        /// 播放回饋（呼叫 MMF_Player.PlayFeedbacks）。
        /// </summary>
        public void Play(string ownerName, string slotName)
        {
            if (_feedbackPlayer == null)
            {
                Core.DebugLogger.Log(
                    $"[FeedbackBridge] {ownerName}.{slotName} 未指定 MMF_Player，跳過。",
                    Core.LogCategory.Feedback);
                return;
            }
            // SendMessage 不直接引用 Feel 型別，避免套件移除後編譯失敗
            _feedbackPlayer.SendMessage("PlayFeedbacks", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 停止回饋（呼叫 MMF_Player.StopFeedbacks），用於停止循環型 MMF。
        /// </summary>
        public void Stop(string ownerName, string slotName)
        {
            if (_feedbackPlayer == null)
            {
                Core.DebugLogger.Log(
                    $"[FeedbackBridge] {ownerName}.{slotName} 未指定 MMF_Player，跳過停止。",
                    Core.LogCategory.Feedback);
                return;
            }
            _feedbackPlayer.SendMessage("StopFeedbacks", SendMessageOptions.DontRequireReceiver);
        }
    }
}
