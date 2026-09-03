"""
MQTT test: three flow gateways (L/s), using the current iocloud protocol.

Published/subscribed topics (numeric fields vary at runtime):

iocloud/{deviceId}/heartbeat:
{"device_id": "1C69209DFC01", "ip": "192.168.3.79", "uptime_ms": 200060}

iocloud/{deviceId}/telemetry:
{
  "device_id": "1C69209DFC01",
  "timestamp": "2026-07-16T14:32:00Z",
  "readings": [{"sensor_id": 0, "type": "flow", "value": 1.05}]
}

iocloud/{deviceId}/commands/request (host -> device):
{"id": 0, "cmd": 3, "params": {}}

iocloud/{deviceId}/commands/response (device -> host):
{
  "cmd": 3,
  "id": 0,
  "data": {
    "sensors": [
      {
        "sensor_id": 0,
        "type": "flow",
        "capabilities": {"unit": "L/s", "range_min": 0, "range_max": 50, "resolution": 0.01},
        "config": {"offset": 0, "gain": 1, "sampling_ms": 5000, "enabled": True}
      }
    ]
  },
  "status": "ok"
}

Commands: 1=REBOOT, 3=GET_SENSORS. Other commands are answered with status "error"
(this script only simulates a single flow sensor per gateway, no config mutation).
"""

import json
import random
import time
from datetime import datetime, timezone
from typing import Any, Dict, Optional, Tuple

import paho.mqtt.client as mqtt

# Broker
BROKER = 'localhost'  # 'broker.hivemq.com'  # "mqtt.eclipseprojects.io"
PORT = 1883

# One flow sensor per gateway.
GATEWAYS = [
    {"id": "1C69209DFC01", "base": 1.0},
    {"id": "1C69209DFC02", "base": 1.23},
    {"id": "1C69209DFC03", "base": 2.0},
]
GATEWAY_BY_ID = {g["id"]: g for g in GATEWAYS}
START_TIME = time.monotonic()

FLOW_CAPABILITIES = {"unit": "L/s", "range_min": 0, "range_max": 50, "resolution": 0.01}
FLOW_CONFIG = {"offset": 0, "gain": 1, "sampling_ms": 5000, "enabled": True}


def handle_reboot(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    return {}, "ok"


def handle_get_sensors(gateway_id: str, params: Dict[str, Any]) -> Tuple[Dict[str, Any], str]:
    if gateway_id not in GATEWAY_BY_ID:
        return {}, "error"

    sensors = [
        {
            "sensor_id": 0,
            "type": "flow",
            "capabilities": FLOW_CAPABILITIES,
            "config": FLOW_CONFIG,
        }
    ]
    return {"sensors": sensors}, "ok"


COMMAND_HANDLERS = {
    1: handle_reboot,
    3: handle_get_sensors,
}


def command_request_device_id(topic: str) -> Optional[str]:
    parts = topic.split("/")
    if len(parts) == 4 and parts[0] == "iocloud" and parts[2] == "commands" and parts[3] == "request":
        return parts[1]
    return None


def on_connect(mqtt_client, userdata, flags, rc):
    if rc == 0:
        print(f"Connected successfully ({len(GATEWAYS)} flow gateways)")
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
        varied_value = round(g["base"] + random.uniform(-0.2, 0.2), 2)
        payload = {
            "device_id": g["id"],
            "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
            "readings": [{"sensor_id": 0, "type": "flow", "value": varied_value}],
        }
        topic = f"iocloud/{g['id']}/telemetry"
        mqtt_client.publish(topic, json.dumps(payload))
        print(f"Sent {varied_value} L/s to {topic}")


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
