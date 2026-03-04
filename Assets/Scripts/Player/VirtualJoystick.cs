using UnityEngine;
using UnityEngine.EventSystems;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 虛擬搖桿：Mobile 直屏用，拖曳控制玩家移動方向。
    /// 掛在 UI Canvas 上的 Image 物件，搭配背景圓盤與搖桿把手。
    /// 輸出正規化方向給 PlayerMovement.InputDirection。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("UI 元素")]
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;

        [Header("設定")]
        [Tooltip("把手最大移動半徑（像素）")]
        [SerializeField] private float _handleRange = 80f;
        [Tooltip("死區（0~1）")]
        [SerializeField] private float _deadZone = 0.1f;

        [Header("目標")]
        [SerializeField] private PlayerMovement _playerMovement;

        private Vector2 _inputVector;
        private Canvas _parentCanvas;

        public Vector2 Direction => _inputVector;

        private void Start()
        {
            _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas == null)
            {
                Core.DebugLogger.LogError("VirtualJoystick 需要放在 Canvas 底下！", Core.LogCategory.Player);
            }

            if (_handle != null)
                _handle.anchoredPosition = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_background == null || _parentCanvas == null) return;

            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, eventData.position, _parentCanvas.worldCamera, out localPoint);

            // 正規化到 -1 ~ 1
            Vector2 sizeDelta = _background.sizeDelta;
            _inputVector = new Vector2(
                localPoint.x / (sizeDelta.x * 0.5f),
                localPoint.y / (sizeDelta.y * 0.5f));

            // 限制在圓形範圍內
            if (_inputVector.magnitude > 1f)
                _inputVector = _inputVector.normalized;

            // 死區
            if (_inputVector.magnitude < _deadZone)
                _inputVector = Vector2.zero;

            // 把手視覺位置
            if (_handle != null)
                _handle.anchoredPosition = _inputVector * _handleRange;

            // 輸出方向
            if (_playerMovement != null)
                _playerMovement.InputDirection = _inputVector;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _inputVector = Vector2.zero;

            if (_handle != null)
                _handle.anchoredPosition = Vector2.zero;

            if (_playerMovement != null)
                _playerMovement.InputDirection = Vector2.zero;
        }
    }
}
