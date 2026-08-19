#!/usr/bin/env bash
# Keeps the API up while an agent is rebuilding the backend. It waits out a short outage first,
# so it never races the agent's own restart — only a sustained outage triggers a start.
DOWN=0
for i in $(seq 1 120); do
  if [ "$(curl -sk -o /dev/null -w '%{http_code}' https://localhost:7135/health 2>/dev/null)" = "200" ]; then
    DOWN=0
  else
    DOWN=$((DOWN+1))
    if [ "$DOWN" -ge 3 ]; then
      cd /d/GYM_17-08-2026/backend
      ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --project src/GymManagement.Api --no-build \
        --urls "https://localhost:7135;http://localhost:5135" >> /d/GYM_17-08-2026/backend/api.log 2>&1 &
      echo "restarted the API after $((DOWN*15))s of downtime"
      DOWN=0
      sleep 25
    fi
  fi
  sleep 15
done
echo "supervisor window finished"
