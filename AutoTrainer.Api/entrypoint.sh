#!/bin/sh

# If TLS certs are mounted, enable the SSL config
if [ -f /certs/private_ca_cert.crt ] && [ -f /certs/private_ca_cert.key ]; then
  cp /etc/nginx/conf.d/ssl.conf.disabled /etc/nginx/conf.d/ssl.conf
fi

# Start nginx in the background
nginx -g 'daemon off;' &

# nginx parses the certificate once at startup and holds it in memory, so an
# acme.sh renewal that rewrites /certs is invisible to a long-running container
# until it is signalled. Without this the api serves the previous certificate
# until it expires, ~30 days after the renewal that replaced it. The stamp is
# written by the cert-manager service after both cert and key are in place.
RELOAD_STAMP="${CERT_RELOAD_STAMP:-/certs/.cert-reload}"
POLL_INTERVAL="${CERT_RELOAD_POLL_INTERVAL:-300}"

read_stamp() {
  cat "$RELOAD_STAMP" 2>/dev/null || echo absent
}

watch_stamp() {
  last=$(read_stamp)
  while :; do
    sleep "$POLL_INTERVAL"
    current=$(read_stamp)
    [ "$current" = "$last" ] && continue
    last=$current
    echo "cert-reload: certificate changed, reloading nginx"
    nginx -s reload || echo "cert-reload: reload failed, still serving previous certificate" >&2
  done
}

watch_stamp &

# Run the service in the foreground so the container exits if it crashes
logName=$(date '+%Y-%m-%d_%H-%M-%S');
logDir="/var/log/autotrainer"
logFile="${logDir}/api-${logName}.log"

mkdir -p "${logDir}"

touch "${logFile}"

exec dotnet AutoTrainer.Api.dll 2>&1 | tee -a "${logFile}"
