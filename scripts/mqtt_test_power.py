"""
MQTT test: two gateways, four measurements each (power, current, tension, power factor).

Published JSON shapes (numeric fields vary at runtime):

iocloud/response/{gatewayId}/sensor/report — periodic reading (indices 0..3):
{
  "timestamp": 1734567890.123456,
  "sensors": [
    {"active": true, "value": 1210.5},
    {"active": true, "value": 5.52},
    {"active": true, "value": 219.1},
    {"active": true, "value": 0.9382}
  ]
}

iocloud/response/{gatewayId}/command — system status (command_index 2):
{
  "command_index": 2,
  "command_status": 0,
  "device_id": "1C69209DPW01",
  "ip_address": "192.168.3.79",
  "uptime": 19510,
  "sensors": [
    {"gain": 1, "offset": 0, "index": 0, "state": 0, "unit": "W"},
    {"gain": 1, "offset": 0, "index": 1, "state": 0, "unit": "A"},
    {"gain": 1, "offset": 0, "index": 2, "state": 0, "unit": "V"},
    {"gain": 1, "offset": 0, "index": 3, "state": 0, "unit": "PF"}
  ]
}
"""

import time
import random
import json
from datetime import datetime
from typing import Any, Dict, List, Optional

import paho.mqtt.client as mqtt

# Broker
BROKER = "localhost"  # 'broker.hivemq.com'  # "mqtt.eclipseprojects.io"
PORT = 1883

# Two gateways; each reports 4 measurements (list order = panel index 0..3)
GATEWAYS: List[Dict[str, Any]] = [
    {
        "id": "1C69209DFC01",
        "label": "circuit_a",
        "channels": [
            {"index": 0, "name": "power", "unit": "W", "base": 1250.0, "jitter": (-45.0, 45.0)},
            {"index": 1, "name": "current", "unit": "A", "base": 5.65, "jitter": (-0.18, 0.18)},
            {"index": 2, "name": "tension", "unit": "V", "base": 220.0, "jitter": (-2.0, 2.0)},
            {"index": 3, "name": "power_factor", "unit": "PF", "base": 0.94, "jitter": (-0.02, 0.02)},
        ],
    },
    {
        "id": "1C69209DFC02",
        "label": "circuit_b",
        "channels": [
            {"index": 0, "name": "power", "unit": "W", "base": 2100.0, "jitter": (-80.0, 80.0)},
            {"index": 1, "name": "current", "unit": "A", "base": 9.45, "jitter": (-0.28, 0.28)},
            {"index": 2, "name": "tension", "unit": "V", "base": 220.0, "jitter": (-2.0, 2.0)},
            {"index": 3, "name": "power_factor", "unit": "PF", "base": 0.91, "jitter": (-0.025, 0.025)},
        ],
    },
]

GATEWAY_BY_ID = {g["id"]: g for g in GATEWAYS}


def on_connect(mqtt_client, userdata, flags, rc):
    if rc == 0:
        print("Connected successfully")
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
        return "W"
    sid = str(sensor_id)
    for ch in cfg["channels"]:
        if str(ch["index"]) == sid:
            return ch["unit"]
    return "W"


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
    unit = unit_for_sensor_id(gw, sensor_id) if gw else "W"

    obj["command_index"] = 1
    obj["command_status"] = 0
    obj["sensor_id"] = sensor_id
    obj["gain"] = obj["params"]["gain"]
    obj["offset"] = obj["params"]["offset"]
    obj["unit"] = unit

    mqtt_client.publish(response_topic, json.dumps(obj))
    print(f"Responded to topic: {response_topic}")


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
        for g in GATEWAYS:
            sensors_out = []
            parts = []
            for ch in sorted(g["channels"], key=lambda c: c["index"]):
                lo, hi = ch["jitter"]
                varied = round(ch["base"] + random.uniform(lo, hi), 4)
                if ch["name"] == "power_factor":
                    varied = max(0.0, min(1.0, varied))
                sensors_out.append({"active": True, "value": varied})
                parts.append(f"{ch['name']}={varied}{ch['unit']}")

            payload = {
                "timestamp": datetime.now().timestamp(),
                "sensors": sensors_out,
            }
            topic = f"iocloud/response/{g['id']}/sensor/report"
            client.publish(topic, json.dumps(payload))
            print(f"[{g['label']}] {', '.join(parts)} -> {topic}")
        time.sleep(60)
except KeyboardInterrupt:
    print("Stopping the client.")
    client.loop_stop()
    client.disconnect()
