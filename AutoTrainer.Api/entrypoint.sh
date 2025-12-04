#!/bin/sh

# If TLS certs are mounted, enable the SSL config
if [ -f /certs/private_ca_cert.crt ] && [ -f /certs/private_ca_cert.key ]; then
  cp /etc/nginx/conf.d/ssl.conf.disabled /etc/nginx/conf.d/ssl.conf
fi

# Start nginx in the background
nginx -g 'daemon off;' &

# Run the service in the foreground so the container exits if it crashes
logName=$(date '+%Y-%m-%d_%H-%M-%S');
logDir="/var/log/autotrainer"
logFile="${logDir}/api-${logName}.log"

mkdir -p "${logDir}"

touch "${logFile}"

exec dotnet AutoTrainer.Api.dll 2>&1 | tee -a "${logFile}"
