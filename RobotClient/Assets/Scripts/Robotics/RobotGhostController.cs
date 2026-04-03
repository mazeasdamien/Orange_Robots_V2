using System.Collections.Generic;
using UnityEngine;
using RobotOrange.Networking;
using System.Text.Json;
using System;

namespace RobotOrange.Robotics
{
    public class RobotGhostController : MonoBehaviour
    {
        [Header("Networking")]
        public HubSocket hubSocket;
        public string targetRobotId = "Robot_Niryo_01";

        [Header("Robot Joints (Must assign exactly 6)")]
        public Transform[] jointTransforms;

        [Header("Rotation Axes (Usually Y or Z)")]
        public Vector3[] rotationAxes = new Vector3[] {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up
        };

        private float[] _targetJoints = new float[6];

        void Start()
        {
            if (hubSocket == null) hubSocket = FindObjectOfType<HubSocket>();
            if (hubSocket != null) hubSocket.OnMessageReceived += HandleTelemetry;
        }

        private void HandleTelemetry(string jsonString)
        {
            if (!jsonString.Contains("\"setRobotJoints\"")) return;

            // Run parsing on background thread, apply locally
            try
            {
                using var pdoc = JsonDocument.Parse(jsonString);
                var root = pdoc.RootElement;
                if (root.TryGetProperty("payload", out var payload) && 
                    payload.TryGetProperty("robotId", out var rId) &&
                    rId.GetString() == targetRobotId)
                {
                    if (payload.TryGetProperty("joints", out var jointsArray))
                    {
                        for (int i = 0; i < Mathf.Min(6, jointsArray.GetArrayLength()); i++)
                        {
                            _targetJoints[i] = (float)jointsArray[i].GetDouble();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GhostController] Error parsing joint data: {e.Message}");
            }
        }

        void Update()
        {
            if (jointTransforms == null || jointTransforms.Length < 6) return;

            // Smoothly interpolate the 3D meshes to match the raw physical floats
            for (int i = 0; i < 6; i++)
            {
                if (jointTransforms[i] == null) continue;

                // ROS floats are usually in radians, Unity uses degrees.
                float targetAngleDeg = _targetJoints[i] * Mathf.Rad2Deg;

                Quaternion targetRot = Quaternion.AngleAxis(targetAngleDeg, rotationAxes[i]);
                
                // Lerp for smooth visuals even if network stutters
                jointTransforms[i].localRotation = Quaternion.Slerp(jointTransforms[i].localRotation, targetRot, Time.deltaTime * 15f);
            }
        }
        
        void OnDestroy()
        {
            if (hubSocket != null) hubSocket.OnMessageReceived -= HandleTelemetry;
        }
    }
}
