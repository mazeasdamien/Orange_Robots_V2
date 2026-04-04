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
        public Transform orbitTarget;
        private RenderTexture _sceneRT;

        // Orbit State
        private float _orbitX = 45f;
        private float _orbitY = 30f;
        private float _orbitDist = 2f;
        private bool _isOrbiting = false;

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

                // Bind strictly UI-constrained camera orbit controls
                sceneViewport.RegisterCallback<PointerDownEvent>(OnScenePointerDown);
                sceneViewport.RegisterCallback<PointerMoveEvent>(OnScenePointerMove);
                sceneViewport.RegisterCallback<PointerUpEvent>(OnScenePointerUp);
                sceneViewport.RegisterCallback<PointerLeaveEvent>(OnScenePointerUp); // Safety catch
                sceneViewport.RegisterCallback<WheelEvent>(OnSceneWheel);
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

        private void OnScenePointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0 || evt.button == 1) _isOrbiting = true; // Left or Right click
            var el = evt.currentTarget as VisualElement;
            el?.CapturePointer(evt.pointerId);
        }

        private void OnScenePointerMove(PointerMoveEvent evt)
        {
            if (_isOrbiting)
            {
                _orbitX += evt.deltaPosition.x * 0.4f;
                _orbitY += evt.deltaPosition.y * 0.4f;
                _orbitY = Mathf.Clamp(_orbitY, -89f, 89f);
            }
        }

        private void OnScenePointerUp(EventBase evt)
        {
            if (evt is PointerUpEvent pu)
            {
                if (pu.button == 0 || pu.button == 1) _isOrbiting = false;
                var el = pu.currentTarget as VisualElement;
                el?.ReleasePointer(pu.pointerId);
            }
            else if (evt is PointerLeaveEvent)
            {
                _isOrbiting = false;
            }
        }

        private void OnSceneWheel(WheelEvent evt)
        {
            _orbitDist += evt.delta.y * 0.05f; // Zoom
            _orbitDist = Mathf.Clamp(_orbitDist, 0.5f, 10f);
        }

        void Update()
        {
            UnityMainThreadDispatcher.Update();

            if (ghostCamera != null)
            {
                Vector3 target = orbitTarget != null ? orbitTarget.position : Vector3.zero;
                Quaternion rot = Quaternion.Euler(_orbitY, _orbitX, 0);
                ghostCamera.transform.position = target + rot * new Vector3(0, 0, -_orbitDist);
                ghostCamera.transform.LookAt(target);
            }
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
