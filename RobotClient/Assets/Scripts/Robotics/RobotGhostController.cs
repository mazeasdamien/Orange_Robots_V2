using System.Collections.Generic;
using UnityEngine;
using RobotOrange.Networking;
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

        [Serializable]
        private class TelemetryRoot
        {
            public TelemetryPayload payload;
        }

        [Serializable]
        private class TelemetryPayload
        {
            public string robotId;
            public float[] joints;
        }

        void Start()
        {
            if (hubSocket == null) hubSocket = FindAnyObjectByType<HubSocket>();
            if (hubSocket != null) hubSocket.OnMessageReceived += HandleTelemetry;
        }

        private void HandleTelemetry(string jsonString)
        {
            if (!jsonString.Contains("\"setRobotJoints\"")) return;

            try
            {
                var msg = JsonUtility.FromJson<TelemetryRoot>(jsonString);
                if (msg != null && msg.payload != null && msg.payload.robotId == targetRobotId)
                {
                    if (msg.payload.joints != null)
                    {
                        for (int i = 0; i < Mathf.Min(6, msg.payload.joints.Length); i++)
                        {
                            _targetJoints[i] = msg.payload.joints[i];
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
