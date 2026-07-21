VanadiumAPI deployment (win-x64)
===================================

Run:
  Linux:   ./VanadiumAPI
  Windows: VanadiumAPI.exe

Dashboard + API + SignalR hub on the port in appsettings.json (default http://localhost:5010).

Requirements on target machine:
  - MongoDB reachable (Mongo:ConnectionString in appsettings.json)
  - MQTT broker if using live gateways (MqttOptions in appsettings.json)
  - SQLite app.db is created on first run next to the executable

Files in this folder:
  - VanadiumAPI executable (+ native deps if not single-file)
  - wwwroot/          Angular dashboard
  - appsettings.json  Server URL, Mongo, JWT, CORS
  - config.json       Optional seed data for empty SQLite DB

Build date: 2026-07-05T13:13:28Z
