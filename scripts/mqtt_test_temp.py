"""
MQTT test for DbConfig/config_temp.json:
  60 temperature (°C), 4 pressure (kPa), 4 energy readings on one gateway (W, A, V, PF).

Published JSON shapes (numeric fields vary at runtime):

iocloud/response/{gatewayId}/sensor/report — periodic reading:
{
  "timestamp": 1734567890.123456,
  "sensors": [
    {"active": true, "value": 22.417}
  ]
}

Multi-channel gateway (energy) publishes four values (indices 0..3).

iocloud/response/{gatewayId}/command — system status (command_index 2):
{
  "command_index": 2,
  "command_status": 0,
  "device_id": "1C69209DFC01",
  "ip_address": "192.168.3.79",
  "uptime": 19510,
  "sensors": [
    {"gain": 1, "offset": 0, "index": 0, "state": 0, "unit": "°C"}
  ]
}
"""

import json
import random
import time
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

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


def on_connect(mqtt_client, userdata, flags, rc):
    if rc == 0:
        print(f"Connected successfully ({len(GATEWAYS)} gateways from {CONFIG_PATH.name})")
        mqtt_client.subscribe("iocloud/request/#")
    else:
        print(f"Connection failed with code {rc}")


def request_gateway_id(topic: str) -> Optional[str]:
    parts = topic.split("/")
    if len(parts) >= 4 and parts[0] == "iocloud" and parts[1] == "request":
        return parts[2]
    return None


def unit_for_sensor_id(gateway_id: str, sensor_id: Any) -> str:
    cfg = GATEWAY_BY_ID.get(gateway_id)
    if not cfg:
        return "°C"
    sid = str(sensor_id)
    for ch in cfg["channels"]:
        if str(ch["index"]) == sid:
            return ch["unit"]
    return cfg["channels"][0]["unit"]


def send_gateway_status(mqtt_client, gateway_id: str):
    cfg = GATEWAY_BY_ID.get(gateway_id)
    if not cfg:
        print(f"Unknown gateway id for status: {gateway_id}")
        return

    panels = []
    for ch in sorted(cfg["channels"], key=lambda c: c["index"]):
        panels.append(
            {
                "gain": 1,
                "offset": 0,
                "index": ch["index"],
                "state": 0,
                "unit": ch["unit"],
            }
        )
    status_payload = {
        "command_index": 2,
        "command_status": 0,
        "device_id": gateway_id,
        "ip_address": "192.168.3.79",
        "uptime": 19510,
        "sensors": panels,
    }
    status_topic = f"iocloud/response/{gateway_id}/command"
    mqtt_client.publish(status_topic, json.dumps(status_payload))
    print(f"Sent status ({cfg['label']}) to {status_topic}")


def on_message(mqtt_client, userdata, msg):
    print(f"Received command on topic: {msg.topic}")
    print(f"Payload: {msg.payload}")

    gw = request_gateway_id(msg.topic)
    response_topic = msg.topic.replace("iocloud/", "iocloud/", 1)
    response_topic = response_topic.replace("request/", "response/", 1)

    if not msg.payload:
        print(f"Empty payload received on topic: {msg.topic}")
        return

    try:
        obj = json.loads(msg.payload)
        print(f"Payload content: {msg.payload}")
    except json.JSONDecodeError as e:
        print(f"Invalid JSON payload on topic {msg.topic}: {e}")
        print(f"Payload content: {msg.payload}")
        return

    if gw and obj.get("command") == 2:
        send_gateway_status(mqtt_client, gw)
        return

    if "params" not in obj:
        print(f"Missing 'params' field in payload on topic: {msg.topic}")
        return

    if not all(key in obj["params"] for key in ["sensor_id", "gain", "offset"]):
        print(f"Missing required fields in params on topic: {msg.topic}")
        return

    sensor_id = obj["params"]["sensor_id"]
    unit = unit_for_sensor_id(gw, sensor_id) if gw else "°C"

    obj["command_index"] = 1
    obj["command_status"] = 0
    obj["sensor_id"] = sensor_id
    obj["gain"] = obj["params"]["gain"]
    obj["offset"] = obj["params"]["offset"]
    obj["unit"] = unit

    mqtt_client.publish(response_topic, json.dumps(obj))
    print(f"Responded to topic: {response_topic}")


def publish_readings(mqtt_client):
    for g in GATEWAYS:
        sensors_out = []
        parts = []
        for ch in sorted(g["channels"], key=lambda c: c["index"]):
            lo, hi = ch["jitter"]
            varied = round(ch["base"] + random.uniform(lo, hi), 4)
            if ch["panel_type"] == 6:
                varied = max(0.0, min(1.0, varied))
            sensors_out.append({"active": True, "value": varied})
            parts.append(f"{ch['name']}={varied}{ch['unit']}")

        payload = {
            "timestamp": datetime.now().timestamp(),
            "sensors": sensors_out,
        }
        topic = f"iocloud/response/{g['id']}/sensor/report"
        mqtt_client.publish(topic, json.dumps(payload))
        print(f"[{g['label']}] {', '.join(parts)} -> {topic}")


client = mqtt.Client()
client.on_connect = on_connect
client.on_message = on_message

client.connect(BROKER, PORT)
client.loop_start()
client.subscribe("iocloud/request/#")

try:
    for g in GATEWAYS:
        send_gateway_status(client, g["id"])

    while True:
        publish_readings(client)
        time.sleep(60)
except KeyboardInterrupt:
    print("Stopping the client.")
    client.loop_stop()
    client.disconnect()
