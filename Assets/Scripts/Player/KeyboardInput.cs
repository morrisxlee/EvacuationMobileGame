using UnityEngine;

namespace SurvivalDemo.Player
{
    /// <summary>
    /// 鍵盤輸入（編輯器測試用）：WASD 移動，自動把方向寫入 PlayerMovement。
    /// 手機上不會用到此腳本，可在 Build 時排除或直接不掛。
    /// </summary>
    public class KeyboardInput : MonoBehaviour
    {
        [SerializeField] private PlayerMovement _playerMovement;

        private void Awake()
        {
            if (_playerMovement == null)
                _playerMovement = GetComponent<PlayerMovement>();
        }

        private void Update()
        {
            if (_playerMovement == null) return;

            float h = 0f;
            float v = 0f;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;

            _playerMovement.InputDirection = new Vector2(h, v);
        }
    }
}
