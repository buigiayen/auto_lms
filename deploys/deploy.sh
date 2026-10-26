#!/bin/sh
set -eu

deploy_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$deploy_directory"

if [ ! -f .env ]; then
  echo 'Chưa có .env. Sao chép .env.example thành .env và đặt WORKER_REGISTRATION_KEY, CLIENT_USERNAME, CLIENT_PASSWORD trước khi chạy.' >&2
  exit 1
fi

docker compose --env-file .env -f docker-compose.yml config --quiet
docker compose --env-file .env -f docker-compose.yml pull
docker compose --env-file .env -f docker-compose.yml up --detach --wait --wait-timeout 180
docker compose --env-file .env -f docker-compose.yml ps
