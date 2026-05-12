#!/bin/bash

# Variables
MONGO_USER="root"
MONGO_PASS="example"
MONGO_HOST="localhost"
MONGO_PORT="27017"
DB_NAME="IoCloudServerDb"

# Run mongosh command to drop the database
mongosh "mongodb://${MONGO_USER}:${MONGO_PASS}@${MONGO_HOST}:${MONGO_PORT}/?authSource=admin" <<EOF
use ${DB_NAME}
db.dropDatabase()
EOF
