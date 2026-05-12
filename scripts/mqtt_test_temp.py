"""
MQTT test: 20 temperature (°C) + 4 pressure (kPa) gateways.

Published JSON shapes (numeric fields vary at runtime):

iocloud/response/{gatewayId}/sensor/report — periodic reading (one sensor per gateway):
{
  "timestamp": 1734567890.123456,
  "sensors": [
    {"active": true, "value": 22.417}
  ]
}

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

import time
import random
import json
from datetime import datetime
from typing import Optional

import paho.mqtt.client as mqtt

# Broker
BROKER = "localhost"  # 'broker.hivemq.com'  # "mqtt.eclipseprojects.io"
PORT = 1883

# 20 temperature gateways + 4 pressure gateways (distinct device IDs / topics)
GATEWAYS = [
    {
        "id": f"1C69209DFC{i:02d}",
        "label": "temperature",
        "base_value": round(20.0 + (i % 8) * 0.6, 2),
        "unit": "°C",
        "jitter": (-0.35, 0.35),
    }
    for i in range(1, 21)
] + [
    {
        "id": f"1C69209DFP{i:02d}",
        "label": "pressure",
        "base_value": round(101.2 + (i - 1) * 0.05, 3),
        "unit": "kPa",
        "jitter": (-0.2, 0.2),
    }
    for i in range(1, 5)
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


def send_gateway_status(mqtt_client, gateway_id: str):
    cfg = GATEWAY_BY_ID.get(gateway_id)
    if not cfg:
        print(f"Unknown gateway id for status: {gateway_id}")
        return

    panel = {
        "gain": 1,
        "offset": 0,
        "index": 0,
        "state": 0,
        "unit": cfg["unit"],
    }
    status_payload = {
        "command_index": 2,
        "command_status": 0,
        "device_id": gateway_id,
        "ip_address": "192.168.3.79",
        "uptime": 19510,
        "sensors": [panel],
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

    unit = "°C"
    if gw and gw in GATEWAY_BY_ID:
        unit = GATEWAY_BY_ID[gw]["unit"]

    obj["command_index"] = 1
    obj["command_status"] = 0
    obj["sensor_id"] = obj["params"]["sensor_id"]
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
            lo, hi = g["jitter"]
            varied = round(g["base_value"] + random.uniform(lo, hi), 3)
            payload = {
                "timestamp": datetime.now().timestamp(),
                "sensors": [{"active": True, "value": varied}],
            }
            topic = f"iocloud/response/{g['id']}/sensor/report"
            client.publish(topic, json.dumps(payload))
            print(f"[{g['label']}] {varied} {g['unit']} -> {topic}")
        time.sleep(60)
except KeyboardInterrupt:
    print("Stopping the client.")
    client.loop_stop()
    client.disconnect()
