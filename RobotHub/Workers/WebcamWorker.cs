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

        public WebcamWorker(ILogger<WebcamWorker> logger, UnityPushServer unityPush)
        {
            _logger = logger;
            _unityPush = unityPush;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[WebcamWorker] Starting independent USB Webcam capture pipelines...");

            var t1 = Task.Run(() => CaptureLoop(0, "updateCameraFeed", stoppingToken), stoppingToken);
            var t2 = Task.Run(() => CaptureLoop(1, "updateCameraFeed2", stoppingToken), stoppingToken);

            await Task.WhenAll(t1, t2);
        }

        private async Task CaptureLoop(int cameraIndex, string pushKey, CancellationToken token)
        {
            try
            {
                // Use DirectShow for Windows - significantly more stable for older webcams!
                using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                if (!capture.IsOpened())
                {
                    _logger.LogWarning($"[WebcamWorker] USB Camera {cameraIndex} not found or busy!");
                    return;
                }

                _logger.LogInformation($"[WebcamWorker] USB Camera {cameraIndex} active. Streaming immediately...");

                var encodeParams = new[] { (int)ImwriteFlags.JpegQuality, 75 }; // Balanced Jpeg quality

                while (!token.IsCancellationRequested)
                {
                    using var frame = new Mat();
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    // Optional: dynamically downscale massive raw webcams to prevent latency
                    using var sendMat = new Mat();
                    if (frame.Cols > 800)
                    {
                        var width = 640;
                        var height = (int)((float)width / frame.Cols * frame.Rows);
                        Cv2.Resize(frame, sendMat, new Size(width, height));
                    }
                    else
                    {
                        frame.CopyTo(sendMat);
                    }

                    Cv2.ImEncode(".jpg", sendMat, out byte[] imageBytes, encodeParams);
                    string b64 = Convert.ToBase64String(imageBytes);

                    // Push video frames directly into Unity client (independent of ROS)
                    _ = _unityPush.BroadcastAsync(pushKey, $"{{\"data\":\"{b64}\"}}");

                    // Small yield to ensure thread scheduler doesn't lock completely
                    await Task.Delay(1, token);
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
