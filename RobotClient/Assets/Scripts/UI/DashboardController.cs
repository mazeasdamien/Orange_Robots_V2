using System;
using UnityEngine;
using UnityEngine.UIElements;
using RobotOrange.Networking;

namespace RobotOrange.UI
{
    [RequireComponent(typeof(HubSocket))]
    public class DashboardController : MonoBehaviour
    {
        [Header("3D Integration")]
        public Camera ghostCamera; 
        private RenderTexture _sceneRT;

        public UIDocument document;
        private HubSocket _hubSocket;

        private VisualElement _videoFeed1;
        private VisualElement _videoFeed2;
        private TextField _intentInput;
        private Slider _activationSlider;
        private Label _sliderValueLbl;

        private Texture2D _latestTex1;
        private Texture2D _latestTex2;

        void Awake()
        {
            _hubSocket = GetComponent<HubSocket>();
            _hubSocket.OnMessageReceived += HandleIncomingMessage;

            _latestTex1 = new Texture2D(2, 2);
            _latestTex2 = new Texture2D(2, 2);
        }

        void OnEnable()
        {
            if (document == null) return;

            var root = document.rootVisualElement;
            _videoFeed1 = root.Q<VisualElement>("VideoFeed1");
            _videoFeed2 = root.Q<VisualElement>("VideoFeed2");

            _intentInput = root.Q<TextField>("IntentInput");

            _activationSlider = root.Q<Slider>("ActivationSlider");
            _sliderValueLbl = root.Q<Label>("SliderValueLbl");

            var sceneViewport = root.Q<VisualElement>("SceneViewport");
            if (sceneViewport != null && ghostCamera != null)
            {
                // Auto-generate a high-res RenderTexture and direct the camera output into the UI!
                _sceneRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                ghostCamera.targetTexture = _sceneRT;
                sceneViewport.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_sceneRT));
            }

            // Event Hooks
            if (_activationSlider != null)
            {
                _activationSlider.RegisterValueChangedCallback(evt =>
                {
                    _sliderValueLbl.text = evt.newValue.ToString("0.00");
                });
            }

            var scanBtn = root.Q<Button>("ScanBtn");
            if (scanBtn != null) scanBtn.clicked += () => Debug.Log("SCAN clicked");

            var submitBtn = root.Q<Button>("SubmitBtn");
            if (submitBtn != null) submitBtn.clicked += () => Debug.Log($"SUBMIT Semantic: {_intentInput?.value}");
        }

        private void HandleIncomingMessage(string jsonMessage)
        {
            // Background thread. Dispatch to main thread.
            UnityMainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    // Exact match using quotes to avoid "updateCameraFeed2" triggering "updateCameraFeed"
                    if (jsonMessage.Contains("\"updateCameraFeed\""))
                    {
                        ExtractAndApplyBase64Image(jsonMessage, _latestTex1, _videoFeed1);
                    }
                    else if (jsonMessage.Contains("\"updateCameraFeed2\""))
                    {
                        ExtractAndApplyBase64Image(jsonMessage, _latestTex2, _videoFeed2);
                    }
                    else if (jsonMessage.Contains("\"setRobotJoints\""))
                    {
                        // Ignore joint logs to prevent spam
                    }
                    else
                    {
                        Debug.Log($"[Dashboard] Received non-video msg: {(jsonMessage.Length > 80 ? jsonMessage.Substring(0, 80) + "..." : jsonMessage)}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Parse error: " + e.Message + " | StackTrace: " + e.StackTrace);
                }
            });
        }

        private void ExtractAndApplyBase64Image(string jsonMessage, Texture2D targetTex, VisualElement targetPanel)
        {
            if (targetPanel == null)
            {
                Debug.LogWarning("[Dashboard] Target Panel is NULL! Cannot apply video frame.");
                return;
            }

            // Specifically targets ' "data":"<base64>" ' from within the UnityPushServer wrapped payload
            int dataIndex = jsonMessage.IndexOf("\"data\"");
            if (dataIndex != -1)
            {
                int colonIndex = jsonMessage.IndexOf(':', dataIndex);
                int startQuote = jsonMessage.IndexOf('"', colonIndex + 1);
                if (startQuote != -1)
                {
                    int start = startQuote + 1;
                    int end = jsonMessage.IndexOf('"', start);
                    if (end != -1)
                    {
                        string b64 = jsonMessage.Substring(start, end - start);
                        byte[] imageBytes = Convert.FromBase64String(b64);
                        if (targetTex.LoadImage(imageBytes))
                        {
                            targetPanel.style.backgroundImage = new StyleBackground(targetTex);
                        }
                        else 
                        {
                            Debug.LogWarning("[Dashboard] targetTex.LoadImage failed to parse base64 bytes!");
                        }
                    }
                }
            }
        }

        void Update()
        {
            UnityMainThreadDispatcher.Update();
        }

        void OnDestroy()
        {
            if (_latestTex1 != null) Destroy(_latestTex1);
            if (_latestTex2 != null) Destroy(_latestTex2);
            if (_sceneRT != null) { _sceneRT.Release(); Destroy(_sceneRT); }
            if (ghostCamera != null) ghostCamera.targetTexture = null;
        }
    }

    public static class UnityMainThreadDispatcher
    {
        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> _executionQueue = new();

        public static void Enqueue(Action action) => _executionQueue.Enqueue(action);

        public static void Update()
        {
            while (_executionQueue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }
    }
}
