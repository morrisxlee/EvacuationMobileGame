using UnityEngine;
using Sirenix.OdinInspector;

namespace SurvivalDemo.Feedback
{
    /// <summary>
    /// 掉落物專屬回饋元件：掛在每個 PickupItem Prefab 上。
    /// 與 FeedbackBridge 分離，避免不必要的插槽污染。
    /// 複用 FeedbackSlot 結構（定義於 FeedbackBridge.cs）。
    /// </summary>
    public class PickupFeedback : MonoBehaviour
    {
        [TitleGroup("掉落物回饋")]
        [InfoBox("將此 Prefab 子物件上的 MMF_Player 拖入對應插槽。留空只輸出提示 Log，不影響遊戲。")]
        [Tooltip("掉落物生成落地時觸發（落地彈跳動畫、粒子爆發等）。\n" +
                 "建議在 MMF_Player 上設定一次性播放（不循環）。\n" +
                 "拖入此 Prefab 子物件上的 MMF_Player GameObject。")]
        [LabelText("生成 (OnSpawn)")]
        [SerializeField] private FeedbackSlot _onSpawn;

        [TitleGroup("掉落物回饋")]
        [Tooltip("玩家成功撿起掉落物時觸發（撿取閃光、拾取音效等）。\n" +
                 "建議在 MMF_Player 上設定一次性播放（不循環）。\n" +
                 "拖入此 Prefab 子物件上的 MMF_Player GameObject。")]
        [LabelText("撿起 (OnCollected)")]
        [SerializeField] private FeedbackSlot _onCollected;

        /// <summary>播放生成回饋（落地動畫、粒子等）。由 PickupItem.Init() 呼叫。</summary>
        public void PlaySpawn()     => _onSpawn.Play(gameObject.name, "OnSpawn");

        /// <summary>播放撿取回饋（閃光、音效等）。由 PickupItem.OnTriggerEnter2D 呼叫。</summary>
        public void PlayCollected() => _onCollected.Play(gameObject.name, "OnCollected");
    }
}
