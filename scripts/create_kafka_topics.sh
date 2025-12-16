#!/bin/bash

# Script to create Kafka topics if they don't exist
# Topics: panel-change, sensor-data

KAFKA_CONTAINER="my-kafka"
BOOTSTRAP_SERVER="localhost:29092"
TOPICS=("panel-change" "sensor-data")

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo "Checking Kafka topics..."

# Check if Kafka container is running
if ! docker ps | grep -q "$KAFKA_CONTAINER"; then
    echo -e "${RED}Error: Kafka container '$KAFKA_CONTAINER' is not running.${NC}"
    echo "Please start it with: docker-compose up -d kafka"
    exit 1
fi

# Function to check if topic exists
topic_exists() {
    local topic_name=$1
    docker exec "$KAFKA_CONTAINER" kafka-topics --bootstrap-server "$BOOTSTRAP_SERVER" \
        --list 2>/dev/null | grep -q "^${topic_name}$"
}

# Function to create topic
create_topic() {
    local topic_name=$1
    echo -e "${YELLOW}Creating topic: $topic_name${NC}"
    docker exec "$KAFKA_CONTAINER" kafka-topics --bootstrap-server "$BOOTSTRAP_SERVER" \
        --create \
        --topic "$topic_name" \
        --partitions 1 \
        --replication-factor 1 \
        --if-not-exists 2>&1
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}Topic '$topic_name' created successfully${NC}"
    else
        echo -e "${RED}Failed to create topic '$topic_name'${NC}"
        return 1
    fi
}

# Process each topic
for topic in "${TOPICS[@]}"; do
    if topic_exists "$topic"; then
        echo -e "${GREEN}Topic '$topic' already exists${NC}"
    else
        create_topic "$topic"
    fi
done

echo ""
echo "Topic creation process completed!"
echo "Listing all topics:"
docker exec "$KAFKA_CONTAINER" kafka-topics --bootstrap-server "$BOOTSTRAP_SERVER" --list
