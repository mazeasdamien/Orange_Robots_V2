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

        // Orbit & Pan State
        private float _orbitX = 45f;
        private float _orbitY = 30f;
        private float _orbitDist = 3f;
        private bool _isOrbiting = false;
        private bool _isPanning = false;
        private Vector3 _panOffset = Vector3.zero;

        // Teleoperation Safety State
        private Button _teleopBtn;
        private bool _isArmed = false;
        private bool _teleopActive = false;
        private float _armTimeout = 0f;

        public UIDocument document;
        private HubSocket _hubSocket;

        private VisualElement _videoFeed1;
        private VisualElement _videoFeed2;
        private TextField _intentInput;
        private Slider _activationSlider;
        private Label _sliderValueLbl;

        // Dynamic Gauges (Robot 1)
        private ProgressBar _r1Conf;
        private ProgressBar _r1Amp;
        private Label _r1Urg;

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

            var btnMinus = root.Q<Button>("BtnMinus");
            if (btnMinus != null && _activationSlider != null) 
                btnMinus.clicked += () => _activationSlider.value = Mathf.Clamp(_activationSlider.value - 0.05f, 0f, 1f);

            var btnPlus = root.Q<Button>("BtnPlus");
            if (btnPlus != null && _activationSlider != null) 
                btnPlus.clicked += () => _activationSlider.value = Mathf.Clamp(_activationSlider.value + 0.05f, 0f, 1f);

            _teleopBtn = root.Q<Button>("TeleopBtn");
            if (_teleopBtn != null)
            {
                _teleopBtn.clicked += () =>
                {
                    if (!_teleopActive)
                    {
                        if (!_isArmed)
                        {
                            _isArmed = true;
                            _armTimeout = Time.time + 3f; // 3 sec arm window
                            _teleopBtn.text = "CONFIRMER";
                            _teleopBtn.style.backgroundColor = new StyleColor(new Color(1f, 0.6f, 0f, 0.2f)); // Warning orange
                            _teleopBtn.style.color = new StyleColor(new Color(1f, 0.6f, 0f, 1f));
                            var warnColor = new StyleColor(new Color(1f, 0.6f, 0f, 1f));
                            _teleopBtn.style.borderTopColor = warnColor;
                            _teleopBtn.style.borderBottomColor = warnColor;
                            _teleopBtn.style.borderLeftColor = warnColor;
                            _teleopBtn.style.borderRightColor = warnColor;
                        }
                        else
                        {
                            _isArmed = false;
                            _teleopActive = true;
                            _teleopBtn.text = "STOP TÉLÉOP";
                            _teleopBtn.style.backgroundColor = new StyleColor(new Color(0.93f, 0.26f, 0.26f, 1f)); // Solid red
                            _teleopBtn.style.color = new StyleColor(Color.white);
                        }
                    }
                    else
                    {
                        _teleopActive = false;
                        _teleopBtn.text = "ACTIVER ROBOTS";
                        ResetTeleopBtnSkin();
                    }
                };
            }

            // 3D Viewport Controls
            var camReset = root.Q<Button>("CamReset");
            if (camReset != null) camReset.clicked += () =>
            {
                _orbitX = 45f;
                _orbitY = 30f;
                _orbitDist = 3f;
                _panOffset = Vector3.zero;
            };

            var camZoomIn = root.Q<Button>("CamZoomIn");
            if (camZoomIn != null) camZoomIn.clicked += () => _orbitDist = Mathf.Clamp(_orbitDist - 0.5f, 0.5f, 10f);

            var camZoomOut = root.Q<Button>("CamZoomOut");
            if (camZoomOut != null) camZoomOut.clicked += () => _orbitDist = Mathf.Clamp(_orbitDist + 0.5f, 0.5f, 10f);

            var scanBtn = root.Q<Button>("ScanBtn");
            if (scanBtn != null) scanBtn.clicked += () => Debug.Log("SCAN clicked");

            var submitBtn = root.Q<Button>("SubmitBtn");
            if (submitBtn != null) submitBtn.clicked += () => Debug.Log($"SUBMIT Semantic: {_intentInput?.value}");

            // Bind Gauges using Query to get the first (R1) instance
            var confBars = root.Query<ProgressBar>("ConfianceBar").ToList();
            if (confBars.Count > 0) _r1Conf = confBars[0];

            var ampBars = root.Query<ProgressBar>("AmplitudeBar").ToList();
            if (ampBars.Count > 0) _r1Amp = ampBars[0];

            var urgLbls = root.Query<Label>("UrgenceLbl").ToList();
            if (urgLbls.Count > 0) _r1Urg = urgLbls[0];
        }

        // Extremely fast method to dynamically sweep gauges and swap pulse colors
        public void UpdateR1Stats(float confidence, float amplitude, string urgency)
        {
            if (_r1Conf != null)
            {
                _r1Conf.value = confidence;
                _r1Conf.title = $"{confidence:0.0}%";
            }
            if (_r1Amp != null)
            {
                _r1Amp.value = amplitude;
                _r1Amp.title = $"{amplitude:0.00}";
            }
            if (_r1Urg != null)
            {
                _r1Urg.text = urgency.ToUpper();
                _r1Urg.RemoveFromClassList("urgency-faible");
                _r1Urg.RemoveFromClassList("urgency-moyenne");
                _r1Urg.RemoveFromClassList("urgency-haute");

                if (urgency.ToLower() == "haute") _r1Urg.AddToClassList("urgency-haute");
                else if (urgency.ToLower() == "moyenne") _r1Urg.AddToClassList("urgency-moyenne");
                else _r1Urg.AddToClassList("urgency-faible");
            }
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
                            var signalLbl = targetPanel.Q<Label>("NoSignalLbl");
                            if (signalLbl != null) signalLbl.style.display = DisplayStyle.None;
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
            if (evt.button == 0) _isOrbiting = true; // Left click orbit
            if (evt.button == 1 || evt.button == 2) _isPanning = true; // Right or Middle click pan
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
            else if (_isPanning && ghostCamera != null)
            {
                // Pan scales smoothly with zoom distance
                float panSens = _orbitDist * 0.0015f;
                _panOffset -= ghostCamera.transform.right * evt.deltaPosition.x * panSens;
                _panOffset += ghostCamera.transform.up * evt.deltaPosition.y * panSens;
            }
        }

        private void OnScenePointerUp(EventBase evt)
        {
            if (evt is PointerUpEvent pu)
            {
                if (pu.button == 0) _isOrbiting = false;
                if (pu.button == 1 || pu.button == 2) _isPanning = false;
                var el = pu.currentTarget as VisualElement;
                el?.ReleasePointer(pu.pointerId);
            }
            else if (evt is PointerLeaveEvent)
            {
                _isOrbiting = false;
                _isPanning = false;
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
                Vector3 baseTarget = orbitTarget != null ? orbitTarget.position : Vector3.zero;
                Vector3 finalTarget = baseTarget + _panOffset;

                Quaternion rot = Quaternion.Euler(_orbitY, _orbitX, 0);
                ghostCamera.transform.position = finalTarget + rot * new Vector3(0, 0, -_orbitDist);
                ghostCamera.transform.LookAt(finalTarget);
            }

            if (_isArmed && Time.time > _armTimeout)
            {
                _isArmed = false;
                if (_teleopBtn != null) _teleopBtn.text = "ACTIVER ROBOTS";
                ResetTeleopBtnSkin();
            }
        }

        private void ResetTeleopBtnSkin()
        {
            if (_teleopBtn == null) return;
            _teleopBtn.style.backgroundColor = new StyleColor(new Color(0.93f, 0.26f, 0.26f, 0.1f));
            _teleopBtn.style.color = new StyleColor(new Color(0.93f, 0.26f, 0.26f, 1f));
            var redColor = new StyleColor(new Color(0.93f, 0.26f, 0.26f, 1f));
            _teleopBtn.style.borderTopColor = redColor;
            _teleopBtn.style.borderBottomColor = redColor;
            _teleopBtn.style.borderLeftColor = redColor;
            _teleopBtn.style.borderRightColor = redColor;
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
