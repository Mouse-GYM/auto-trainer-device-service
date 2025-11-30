#!/usr/bin/env bash

logName=$(date '+%Y-%m-%d_%H-%M-%S');

mkdir -p /var/log/auto-trainer

dotnet AutoTrainer.Api.dll >> /var/log/auto-trainer/auto-trainer-api-${logName}.log 2>&1
