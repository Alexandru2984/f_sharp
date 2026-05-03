#!/bin/bash
PORT=5000
while ss -tuln | grep -q ":$PORT " ; do
  PORT=$((PORT+1))
done
echo "APP_PORT=$PORT" > /home/micu/f_sharp/.env
echo "Found free port: $PORT"
