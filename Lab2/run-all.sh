#!/usr/bin/env bash
# Повний прогін ЛР2: три обсяги даних (10k / 100k / 1M) на SQL Server у Docker
# + контрольний прогін 100k на LocalDB. Результати -> Lab2/results/
set -e
cd "$(dirname "$0")/.."
docker compose up -d
dotnet build -v q

for size in 10000 100000 1000000; do
  dotnet run --no-build -- seed --size $size --sql docker | tee "Lab2/results/seed-$size-sql-docker.txt"
  dotnet run --no-build -- bench --sql docker            | tee "Lab2/results/bench-$size-sql-docker.txt"
done

dotnet run --no-build -- seed --size 100000 --sql localdb | tee "Lab2/results/seed-100000-sql-localdb.txt"
dotnet run --no-build -- bench --sql localdb             | tee "Lab2/results/bench-100000-sql-localdb.txt"
