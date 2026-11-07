using TMPro;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 将工程名 TMP 输入框接到 Unity/Android 系统软键盘。
    ///
    /// PICO 真机是否显示键盘由系统版本和输入法策略决定；本桥接不依赖厂商私有
    /// Java API。系统键盘不可用时仍保留原有 TMP 输入框，编辑器和普通 Android
    /// 工程不会被阻断。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasSystemKeyboardBridge : MonoBehaviour
    {
        public TMP_InputField inputField;
        public bool openOnSelect = true;
        public TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;
        public bool alert = false;

        private TouchScreenKeyboard keyboard;
        private bool syncing;

        private void Awake()
        {
            if (inputField == null) inputField = GetComponent<TMP_InputField>();
            if (inputField != null) inputField.onSelect.AddListener(OnSelected);
        }

        private void OnDestroy()
        {
            if (inputField != null) inputField.onSelect.RemoveListener(OnSelected);
        }

        private void OnSelected(string _)
        {
            if (!openOnSelect || Application.platform != RuntimePlatform.Android || !TouchScreenKeyboard.isSupported) return;
            Open();
        }

        public void Open()
        {
            if (inputField == null || Application.platform != RuntimePlatform.Android || !TouchScreenKeyboard.isSupported) return;
            keyboard = TouchScreenKeyboard.Open(inputField.text ?? string.Empty, keyboardType, false, false, false, alert, "工程名");
        }

        private void Update()
        {
            if (keyboard == null || inputField == null) return;
            if (!syncing && inputField.text != keyboard.text)
            {
                syncing = true;
                inputField.SetTextWithoutNotify(keyboard.text ?? string.Empty);
                syncing = false;
            }
            if (keyboard.status == TouchScreenKeyboard.Status.Done || keyboard.status == TouchScreenKeyboard.Status.Canceled)
                keyboard = null;
        }
    }
}
