import time
import random
import json
from datetime import datetime
import paho.mqtt.client as mqtt

# Define the broker and port
BROKER = 'localhost'  # 'broker.hivemq.com'  # "mqtt.eclipseprojects.io"
PORT = 1883
MESSAGES_TO_SEND = 1

# Base sensor values
base_sensors = [
    {"value": -41.5, "active": True, "unit": "°C"},
    {"value": -30, "active": True, "unit": "°C"},
    {"value": -18.41, "active": True, "unit": "°C"},
    {"value": -10.97, "active": True, "unit": "°C"},
    {"value": -0.67, "active": True, "unit": "°C"},
    {"value": 7.33, "active": True, "unit": "°C"},
    {"value": 20.88, "active": True, "unit": "°C"},
    {"value": 25.1, "active": True, "unit": "°C"},
    {"value": 28.18, "active": True, "unit": "°C"},
    {"value": 39.2, "active": True, "unit": "°C"},
    {"value": 48.3, "active": True, "unit": "°C"},
    {"value": 62.77, "active": True, "unit": "°C"},
    {"value": 68.89, "active": True, "unit": "°C"},
    {"value": 80.62, "active": True, "unit": "°C"},
    {"value": 92.21, "active": True, "unit": "°C"},
    {"value": 98.12, "active": True, "unit": "°C"},
    {"value": 111.21, "active": True, "unit": "°C"},
    {"value": 124.4, "active": True, "unit": "°C"},
    {"value": 132.87, "active": True, "unit": "°C"},
    {"value": 140.7, "active": True, "unit": "°C"},
    {"value": 20, "active": True, "unit": "kPa"},
    {"value": 20, "active": True, "unit": "kPa"},
    {"value": 221, "active": True, "unit": "V"},
    {"value": 2, "active": True, "unit": "A"},
    {"value": 1000, "active": True, "unit": "W"},
    {"value": 0.96, "active": True, "unit": "%"}
]

# Callback for successful connection


def on_connect(mqtt_client, userdata, flags, rc):
    if rc == 0:
        print("Connected successfully")
        # Subscribe to the command topic
        mqtt_client.subscribe("iocloud/request/#")
    else:
        print(f"Connection failed with code {rc}")


# Function to create and send gateway status message
def send_gateway_status(mqtt_client, response_topic):
    panels = []
    counter = 0
    for sensor_data in base_sensors:
        panel = {
            "gain": 1,
            "offset": 0,
            "index": counter,
            "state": 0,
            "unit": sensor_data["unit"]
        }
        panels.append(panel)
        counter += 1

    status_payload = {
        "command_index": 2,
        "command_status": 0,
        "device_id": "1C69209DFC08",
        "ip_address": "192.168.3.79",
        "uptime": 19510,
        "sensors": panels
    }

    status_topic = response_topic
    mqtt_client.publish(status_topic, json.dumps(status_payload))
    print(f"Sent status message to {status_topic}")


# Callback for receiving messages


def on_message(mqtt_client, userdata, msg):
    print(f"Received command on topic: {msg.topic}")
    print(f"Payload: {msg.payload}")

    # Handle other command topics
    response_topic = msg.topic.replace("iocloud/", "iocloud/", 1)
    response_topic = response_topic.replace("request/", "response/", 1)

    # Check if payload is empty or invalid
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

    if obj["command"] == 2:
        send_gateway_status(
            mqtt_client, "iocloud/response/1C69209DFC08/command")
        return

    # Check if required fields exist
    if "params" not in obj:
        print(f"Missing 'params' field in payload on topic: {msg.topic}")
        return

    if not all(key in obj["params"] for key in ["sensor_id", "gain", "offset"]):
        print(f"Missing required fields in params on topic: {msg.topic}")
        return

    obj["command_index"] = 1
    obj["command_status"] = 0
    obj["sensor_id"] = obj["params"]["sensor_id"]
    obj["gain"] = obj["params"]["gain"]
    obj["offset"] = obj["params"]["offset"]
    obj["unit"] = "°C"

    mqtt_client.publish(response_topic, json.dumps(obj))
    print(f"Responded to topic: {response_topic}")


# Create an MQTT client instance
client = mqtt.Client()

# Assign callbacks
client.on_connect = on_connect
client.on_message = on_message

# Connect and start the loop
client.connect(BROKER, PORT)
client.loop_start()

client.subscribe("iocloud/request/#")

try:
    while True:
        sensors = []
        count = 0
        for sensor in base_sensors:
            count += 1
            if sensor["unit"] == "°C" and count != 2:
                varied_value = round(
                    sensor["value"] + random.uniform(-5, 5), 2)
                sensors.append({"active": True, "value": varied_value})
            else:
                sensors.append({"active": True, "value": sensor["value"]})
        payload = {
            "timestamp": datetime.now().timestamp(),
            "sensors": sensors,
        }
        topic = "iocloud/response/1C69209DFC08/sensor/report"
        payload_json = json.dumps(payload)
        print(f"Sending MQTT message to {topic}")
        client.publish(topic, payload_json)
        print("Sent MQTT message")
        time.sleep(10)
except KeyboardInterrupt:
    print("Stopping the client.")
    client.loop_stop()
    client.disconnect()
