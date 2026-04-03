# Project Orange (V2)

Welcome to the **Project Orange V2** Monorepo.

This repository unifies the dual-robot remote telepresence platform into a hyper-performant, real-time client-server architecture using explicit bidirectional binary bridges over Cloudflare tunnels.

## Architecture

*   **[`RobotHub/`](./RobotHub)**: An intensive C# ASP.NET Core background service that acts as the universal hardware driver and network multiplexer. 
    *   Initiates direct low-level UDP/TCP bindings locally into both independent ROS cores controlling the Niryo arms.
    *   Houses `RobotRelayService.cs` which manages high throughput telemetry multiplexing.
    *   Hosts the specialized `Scene3D` WebSocket pusher for pure 30 FPS video relay without clogging command loops.
    *   *Usage*: `dotnet run` 

*   **[`RobotClient/`](./RobotClient)**: A lightweight, cutting-edge Unity 3D native Expert Dashboard (URP/UI Toolkit).
    *   Decoupled completely from ROS; purely consumes HTTP JSON and raw WebSockets.
    *   Provides sub-50ms glassmorphic overlay interactions and AR scene overlays.
    *   No legacy UI dependencies—entirely scripted in UXML/USS flex-box methodologies.

## Legacy Code (V1)
The legacy Unity client, including the obsolete semantic routing logic and non-performant Win32-style canvas implementations, has been fully preserved on the [`v1` branch](https://github.com/mazeasdamien/Robot_Orange-main/tree/v1).

## Recent Changelog (Antigravity Migration)
*   **Wiped root Unity dependencies:** Stripped old logic to establish a fully decoupled two-part system.
*   **Scene3D Splitting:** Forced all high-capacity video streams (`/compressed_video_stream`) from the robots to route uniquely through the `scene3d-ws` websocket instead of the unified `/unity` command loop buffer.
*   **Video Multiplexing Bug Fixed:** Identified and repaired a serious backend C# fault in `RobotHub` where both robots were incorrectly writing over the `"updateCameraFeed"` block asynchronously. 
*   **Client Aggregation Logic:** Recreated the V1 User Interface strictly using modern `UI Toolkit`. Recompiled base64 JPEG networking parsers with complete C# MemoryStream handlers to solve MTU fragmentation over Cloudflare sockets.
*   **Unified WebSocket Control:** Modified `UnityPushServer` to forward Unity incoming commands downstream to the robots natively over the `/scene3d-ws` feed stream, merging the telemetry sink and command loop into one robust channel.
