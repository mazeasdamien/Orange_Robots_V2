using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using RobotHub.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RobotHub.Workers
{
    public class WebcamWorker : BackgroundService
    {
        private readonly ILogger<WebcamWorker> _logger;
        private readonly UnityPushServer _unityPush;

        // Change these indices to select which physical cameras to capture.
        // Based on your system: 
        // 0 = HD Webcam (Ceiling)
        // 1 = RealSense Depth (Black/Unsupported format)
        // 2 = Creative GestureCam
        // 4 = RealSense RGB
        public const int CAMERA_1_INDEX = 2; 
        public const int CAMERA_2_INDEX = 3;

        private volatile int _framesLastSec;
        private volatile int _totalFrames;
        private DateTime _lastFpsTime = DateTime.Now;

        public WebcamWorker(ILogger<WebcamWorker> logger, UnityPushServer unityPush)
        {
            _logger = logger;
            _unityPush = unityPush;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[WebcamWorker] Pre-caching RealSense intrinsics before asserting DSHOW camera locks...");
            
            // Appelé en amont : permet de libérer le SDK Intel RealSense avant que OpenCV ne bloque le port USB.
            _ = RealSenseIntrinsics.GetColorIntrinsics();

            _logger.LogInformation("[WebcamWorker] Starting independent USB Webcam capture pipelines...");

            var t1 = Task.Run(() => CaptureLoop(CAMERA_1_INDEX, "updateCameraFeed", stoppingToken), stoppingToken);
            
            // DSHOW backend initialization on Windows is notoriously thread-unsafe.
            // Staggering the startup prevents the "can't capture by index" race condition.
            await Task.Delay(2000, stoppingToken);
            
            var t2 = Task.Run(() => CaptureLoop(CAMERA_2_INDEX, "updateCameraFeed2", stoppingToken), stoppingToken);

            await Task.WhenAll(t1, t2);
        }

        private async Task CaptureLoop(int cameraIndex, string pushKey, CancellationToken token)
        {
            try
            {
                // OpenCV DSHOW is recommended on Windows, staggered startup prevents indexing bugs
                using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                if (!capture.IsOpened())
                {
                    _logger.LogWarning($"[WebcamWorker] USB Camera {cameraIndex} not found or busy!");
                    return;
                }

                _logger.LogInformation($"[WebcamWorker] USB Camera {cameraIndex} active. Streaming immediately...");

                // Configuration de la résolution souhaitée
                capture.Set(VideoCaptureProperties.FrameWidth, 1280);
                capture.Set(VideoCaptureProperties.FrameHeight, 720);

                var encodeParams = new[] { (int)ImwriteFlags.JpegQuality, 75 }; // Balanced Jpeg quality

                // Boucle de capture asynchrone
                while (!token.IsCancellationRequested && capture.IsOpened())
                {
                    using var frame = new Mat();
                    // Lecture de la trame matérielle
                    if (capture.Read(frame) && !frame.Empty())
                    {
                        // Encodage matériel de l'image
                        Cv2.ImEncode(".jpg", frame, out byte[] imageBytes, encodeParams);
                        string base64Image = Convert.ToBase64String(imageBytes);

                        // Track FPS for dashboard
                        Interlocked.Increment(ref _totalFrames);
                        Interlocked.Increment(ref _framesLastSec);
                        var now = DateTime.Now;
                        if ((now - _lastFpsTime).TotalSeconds >= 1)
                        {
                            RobotRelayService.PushImageStats(_framesLastSec, _totalFrames);
                            _framesLastSec = 0;
                            _lastFpsTime = now;
                        }

                        // Formater en URI Data Scheme compatible web standard
                        string dataUriParams = $"\"data:image/jpeg;base64,{base64Image}\"";
                        
                        // Push video frames directly into Unity client (independent of ROS)
                        _ = _unityPush.BroadcastAsync(pushKey, dataUriParams);
                    }
                    // OpenCvSharp naturally blocks on capture.Read() according to the hardware USB frame clock.
                    // A yielding delay prevents thread starvation without halving the framerate like Task.Delay(33) did.
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError($"[WebcamWorker] Camera {cameraIndex} fatal exception: {ex.Message}");
            }
        }
    }
}
