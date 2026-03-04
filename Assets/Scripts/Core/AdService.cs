using System;
using UnityEngine;

namespace SurvivalDemo.Core
{
    /// <summary>
    /// 微信廣告 SDK 介面 Stub：第一版先做假實作，確保核心玩法可跑。
    /// 日後替換為真正的微信 SDK 呼叫。
    /// </summary>
    public class AdService : MonoBehaviour
    {
        private static AdService _instance;
        public static AdService Instance => _instance;

        [Header("Stub 設定")]
        [Tooltip("Stub 模式下觀看廣告是否自動成功")]
        [SerializeField] private bool _stubAutoSuccess = true;
        [Tooltip("Stub 模式下模擬廣告載入延遲（秒）")]
        [SerializeField] private float _stubDelay = 1f;

        private Action _onAdCompleted;
        private Action _onAdFailed;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 請求觀看激勵廣告。
        /// onCompleted：廣告觀看完成時回呼。
        /// onFailed：廣告失敗/取消時回呼。
        /// </summary>
        public void ShowRewardedAd(Action onCompleted, Action onFailed)
        {
            _onAdCompleted = onCompleted;
            _onAdFailed = onFailed;

            DebugLogger.Log("[AdService Stub] 請求觀看激勵廣告...", LogCategory.Core);

            // Stub 模式：延遲後自動回傳結果
            if (_stubDelay > 0f)
            {
                StartCoroutine(StubAdCoroutine());
            }
            else
            {
                ResolveStub();
            }
        }

        private System.Collections.IEnumerator StubAdCoroutine()
        {
            yield return new WaitForSecondsRealtime(_stubDelay);
            ResolveStub();
        }

        private void ResolveStub()
        {
            if (_stubAutoSuccess)
            {
                DebugLogger.Log("[AdService Stub] 廣告觀看完成（模擬成功）。", LogCategory.Core);
                _onAdCompleted?.Invoke();
            }
            else
            {
                DebugLogger.Log("[AdService Stub] 廣告觀看失敗（模擬失敗）。", LogCategory.Core);
                _onAdFailed?.Invoke();
            }

            _onAdCompleted = null;
            _onAdFailed = null;
        }

        /// <summary>
        /// 檢查廣告是否可用（Stub 模式永遠回 true）。
        /// </summary>
        public bool IsAdReady()
        {
            DebugLogger.Log("[AdService Stub] IsAdReady = true（Stub 模式）。", LogCategory.Core);
            return true;
        }
    }
}
