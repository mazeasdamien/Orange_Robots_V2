using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RobotOrange.Networking
{
    public class HubSocket : MonoBehaviour
    {
        [Header("Connection Details")]
        public string hubUrl = "wss://niryo.dmzs-lab.com/scene3d-ws";
        
        public event Action<string> OnMessageReceived;
        public event Action<bool> OnConnectionStateChanged;
        
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;

        async void Start()
        {
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            
            try
            {
                Debug.Log($"[HubSocket] Connecting to {hubUrl}...");
                await _webSocket.ConnectAsync(new Uri(hubUrl), _cts.Token);
                Debug.Log("[HubSocket] Connected!");
                OnConnectionStateChanged?.Invoke(true);
                
                ReceiveLoop();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HubSocket] Connection failed: {ex.Message}");
                OnConnectionStateChanged?.Invoke(false);
            }
        }

        private async void ReceiveLoop()
        {
            var buffer = new byte[8192];
            
            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        if (result.Count > 0) ms.Write(buffer, 0, result.Count);
                    } 
                    while (!result.EndOfMessage && !_cts.IsCancellationRequested);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    
                    if (ms.Length > 0)
                    {
                        var jsonString = Encoding.UTF8.GetString(ms.ToArray());
                        OnMessageReceived?.Invoke(jsonString);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal exit when Play mode stops or component is destroyed
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HubSocket] Disconnected: {e.Message}");
            }
            finally
            {
                OnConnectionStateChanged?.Invoke(false);
            }
        }

        public async void SendMessageToHub(string json)
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
        }
    }
}
