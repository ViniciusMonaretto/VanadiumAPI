"""
MQTT test for DbConfig/config_temp.json, using the new iocloud protocol:
  60 temperature (C), 4 pressure (kPa), 4 energy readings on one gateway (W, A, V, PF).

Published/subscribed topics (numeric fields vary at runtime):

iocloud/{deviceId}/heartbeat:
{"device_id": "1C69209DFC01", "ip": "192.168.3.79", "uptime_ms": 200060}

iocloud/{deviceId}/telemetry:
{
  "device_id": "1C69209DFC01",
  "timestamp": "2026-07-16T14:32:00Z",
  "readings": [{"sensor_id": 0, "type": "temperature", "value": 22.417}]
}
Disabled sensors (SET_SENSOR_CONFIG(_BULK) enabled=false) are simply omitted from readings.

iocloud/{deviceId}/commands/request (host -> device):
{"id": 0, "cmd": 1, "params": {}}

iocloud/{deviceId}/commands/response (device -> host):
{"cmd": 1, "id": 0, "data": {}, "status": "ok"}

Commands: 1=REBOOT, 2=SET_SENSOR_CONFIG, 3=GET_SENSORS, 4=SET_SENSOR_CONFIG_BULK, 5=GET_DEVICE_INFO.
"""

import json
import random
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import paho.mqtt.client as mqtt

# Broker
BROKER = "localhost"  # 'broker.hivemq.com'  # "mqtt.eclipseprojects.io"
PORT = 1883

CONFIG_PATH = Path(__file__).resolve().parent.parent / "DbConfig" / "config_temp.json"

# PanelType -> simulation defaults (matches Shared.Models.PanelType)
PANEL_TYPE_META: Dict[int, Dict[str, Any]] = {
    0: {"label": "temperature", "unit": "°C", "jitter": (-0.35, 0.35)},
    1: {"label": "pressure", "unit": "kPa", "jitter": (-0.2, 0.2)},
    3: {"label": "power", "unit": "W", "base": 1250.0, "jitter": (-45.0, 45.0)},
    4: {"label": "current", "unit": "A", "base": 5.65, "jitter": (-0.18, 0.18)},
    5: {"label": "voltage", "unit": "V", "base": 220.0, "jitter": (-2.0, 2.0)},
    6: {"label": "power_factor", "unit": "PF", "base": 0.94, "jitter": (-0.02, 0.02)},
}

CAPABILITIES_BY_TYPE: Dict[int, Dict[str, Any]] = {
    0: {"range_min": -40, "range_max": 125, "resolution": 0.01},
    1: {"range_min": 0, "range_max": 1000, "resolution": 0.1},
    3: {"range_min": 0, "range_max": 5000, "resolution": 0.1},
    4: {"range_min": 0, "range_max": 100, "resolution": 0.01},
    5: {"range_min": 0, "range_max": 300, "resolution": 0.1},
    6: {"range_min": 0, "range_max": 1, "resolution": 0.001},
}


def base_value_for_panel(panel_type: int, ordinal: int) -> float:
    if panel_type == 0:
        return round(20.0 + (ordinal % 8) * 0.6, 2)
    if panel_type == 1:
        return round(101.2 + (ordinal - 1) * 0.05, 3)
    meta = PANEL_TYPE_META[panel_type]
    return float(meta["base"])


def load_gateways_from_config(path: Path) -> List[Dict[str, Any]]:
    with path.open(encoding="utf-8") as f:
        config = json.load(f)

    by_gateway: Dict[str, List[Dict[str, Any]]] = {}
    for panel in config["Panels"]:
        by_gateway.setdefault(panel["GatewayId"], []).append(panel)

    type_ordinals: Dict[int, int] = {0: 0, 1: 0}
    gateways: List[Dict[str, Any]] = []

    for gateway_id in sorted(by_gateway):
        panels = sorted(by_gateway[gateway_id], key=lambda p: int(p["Index"]))
        channels: List[Dict[str, Any]] = []

        for panel in panels:
            panel_type = panel["Type"]
            meta = PANEL_TYPE_META[panel_type]
            if panel_type in (0, 1):
                type_ordinals[panel_type] = type_ordinals.get(panel_type, 0) + 1
                ordinal = type_ordinals[panel_type]
            else:
                ordinal = int(panel["Index"])

            channels.append(
                {
                    "index": int(panel["Index"]),
                    "name": panel["Name"],
                    "label": meta["label"],
                    "unit": meta["unit"],
                    "base": base_value_for_panel(panel_type, ordinal),
                    "jitter": meta["jitter"],
                    "panel_type": panel_type,
                }
            )

        labels = {ch["label"] for ch in channels}
        gateway_label = labels.pop() if len(labels) == 1 else "energy"
        gateways.append({"id": gateway_id, "label": gateway_label, "channels": channels})

    return gateways


GATEWAYS = load_gateways_from_config(CONFIG_PATH)
GATEWAY_BY_ID = {g["id"]: g for g in GATEWAYS}
START_TIME = time.monotonic()


def init_state() -> Dict[str, Dict[int, Dict[str, Any]]]:
    state: Dict[str, Dict[int, Dict[str, Any]]] = {}
    for g in GATEWAYS:
        state[g["id"]] = {
            ch["index"]: {"gain": 1.0, "offset": 0.0, "sampling_ms": 1000, "enabled": True}
            for ch in g["channels"]
        }
    return state


# Simulated per-sensor config, mutated via SET_SENSOR_CONFIG / SET_SENSOR_CONFIG_BULK.
STATE = init_state()


def capabilities_for_channel(ch: Dict[str, Any]) -> Dict[str, Any]:
    caps = dict(CAPABILITIES_BY_TYPE.get(ch["panel_type"], {"range_min": 0, "range_max": 100, "resolution": 0.1}))
    caps["unit"] = ch["unit"]
    return caps


def apply_sensor_config(gateway_id: str, sensor_id: int, params: Dict[str, Any]) -> None:
    st = STATE[gateway_id][sensor_id]
    for key in ("gain", "offset", "sampling_ms", "enabled"):
        if key in params:
            st[key] = params[key]


def handle_reboot(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    return {}, "ok"


def handle_get_sensors(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    cfg = GATEWAY_BY_ID.get(gateway_id)
    if not cfg:
        return {}, "error"

    sensors = []
    for ch in sorted(cfg["channels"], key=lambda c: c["index"]):
        st = STATE[gateway_id][ch["index"]]
        sensors.append(
            {
                "sensor_id": ch["index"],
                "type": ch["label"],
                "capabilities": capabilities_for_channel(ch),
                "config": {
                    "offset": st["offset"],
                    "gain": st["gain"],
                    "sampling_ms": st["sampling_ms"],
                    "enabled": st["enabled"],
                },
            }
        )
    return {"sensors": sensors}, "ok"


def handle_set_sensor_config(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    sensor_id = params.get("sensor_id")
    if gateway_id not in STATE or sensor_id not in STATE[gateway_id]:
        return {"errors": [{"sensor_id": sensor_id, "error": "unknown sensor_id"}]}, "error"
    apply_sensor_config(gateway_id, sensor_id, params)
    return {}, "ok"


def handle_set_sensor_config_bulk(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    sensors = params.get("sensors", [])
    known = STATE.get(gateway_id, {})
    unknown = [s for s in sensors if s.get("sensor_id") not in known]
    if unknown:
        errors = [{"sensor_id": s.get("sensor_id"), "error": "unknown sensor_id"} for s in unknown]
        return {"errors": errors}, "error"

    for s in sensors:
        apply_sensor_config(gateway_id, s["sensor_id"], s)
    return {}, "ok"


def handle_get_device_info(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    return {"model": "iocloud-sim", "firmware": "0.0.1-sim", "serial": gateway_id}, "ok"


COMMAND_HANDLERS = {
    1: handle_reboot,
    2: handle_set_sensor_config,
    3: handle_get_sensors,
    4: handle_set_sensor_config_bulk,
    5: handle_get_device_info,
}


def command_request_device_id(topic: str) -> Optional[str]:
    parts = topic.split("/")
    if len(parts) == 4 and parts[0] == "iocloud" and parts[2] == "commands" and parts[3] == "request":
        return parts[1]
    return None


def on_connect(mqtt_client, userdata, flags, rc):
    if rc == 0:
        print(f"Connected successfully ({len(GATEWAYS)} gateways from {CONFIG_PATH.name})")
        mqtt_client.subscribe("iocloud/+/commands/request")
    else:
        print(f"Connection failed with code {rc}")


def on_message(mqtt_client, userdata, msg):
    if not msg.payload:
        return

    gateway_id = command_request_device_id(msg.topic)
    if gateway_id is None:
        print(f"Ignoring message on unexpected topic: {msg.topic}")
        return

    try:
        req = json.loads(msg.payload)
    except json.JSONDecodeError as e:
        print(f"Invalid JSON payload on topic {msg.topic}: {e}")
        return

    req_id = req.get("id")
    cmd = req.get("cmd")
    params = req.get("params") or {}
    print(f"Received cmd={cmd} id={req_id} from {gateway_id}: {params}")

    handler = COMMAND_HANDLERS.get(cmd)
    if handler is None:
        data, status = {}, "error"
    else:
        data, status = handler(gateway_id, params)

    response = {"cmd": cmd, "id": req_id, "data": data, "status": status}
    response_topic = f"iocloud/{gateway_id}/commands/response"
    mqtt_client.publish(response_topic, json.dumps(response))
    print(f"Responded to {response_topic}: {response}")


def publish_heartbeat(mqtt_client):
    uptime_ms = int((time.monotonic() - START_TIME) * 1000)
    for g in GATEWAYS:
        payload = {"device_id": g["id"], "ip": "192.168.3.79", "uptime_ms": uptime_ms}
        topic = f"iocloud/{g['id']}/heartbeat"
        mqtt_client.publish(topic, json.dumps(payload))
    print(f"Sent heartbeat (uptime_ms={uptime_ms}) for {len(GATEWAYS)} gateways")


def publish_readings(mqtt_client):
    for g in GATEWAYS:
        readings = []
        parts = []
        for ch in sorted(g["channels"], key=lambda c: c["index"]):
            st = STATE[g["id"]][ch["index"]]
            if not st["enabled"]:
                continue

            lo, hi = ch["jitter"]
            varied = round(ch["base"] + random.uniform(lo, hi), 4)
            if ch["panel_type"] == 6:
                varied = max(0.0, min(1.0, varied))
            calibrated = round(varied * st["gain"] + st["offset"], 4)

            readings.append({"sensor_id": ch["index"], "type": ch["label"], "value": calibrated})
            parts.append(f"{ch['name']}={calibrated}{ch['unit']}")

        payload = {
            "device_id": g["id"],
            "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
            "readings": readings,
        }
        topic = f"iocloud/{g['id']}/telemetry"
        mqtt_client.publish(topic, json.dumps(payload))
        print(f"[{g['label']}] {', '.join(parts)} -> {topic}")


client = mqtt.Client()
client.on_connect = on_connect
client.on_message = on_message

client.connect(BROKER, PORT)
client.loop_start()
client.subscribe("iocloud/+/commands/request")

try:
    while True:
        publish_heartbeat(client)
        publish_readings(client)
        time.sleep(60)
except KeyboardInterrupt:
    print("Stopping the client.")
    client.loop_stop()
    client.disconnect()
