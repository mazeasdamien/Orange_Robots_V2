# Changelog

## [2026-04-03]
- **Major Architecture Shift:** Preserved legacy single-app framework in branch `v1`. 
- **Repository Optimization:** Deleted 4.6GB of legacy Unity code components in `main` branch. Merged `RobotHub` (backend Asp.Net) and `RobotClient` (UI Toolkit Unity Client) physically into the `Robot_Orange-main` repository, transforming it into a complete multi-project V2 monorepo system.
- **Bug Fix**: Hot-patched `RobotRelayService.cs` multiplexing bug which erroneously routed robot streams.
- **Added Documentation:** Rewrote `README.md` to map new folders.
- **Video Routing Fix**: Updated `UnityPushServer.cs` to forward incoming commands to robots, allowing a single websocket connection (`/scene3d-ws`) for both telemetry feeds and commands.
