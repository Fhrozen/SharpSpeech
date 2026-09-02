#!/bin/bash
# Generates a self-signed TLS certificate for serving FastTTSR over HTTPS on a LAN
# hostname/IP (required for browsers to expose navigator.mediaDevices outside of localhost).
# Usage: ./generate-cert.sh <hostname-or-ip> [password]
set -e

HOST="$1"
PASSWORD="$2"
OUT_DIR="./certs"
PFX_PATH="${OUT_DIR}/fastttsr.pfx"

if [ -z "$HOST" ]; then
  echo "Usage: ./generate-cert.sh <hostname-or-ip> [password]"
  echo "Example: ./generate-cert.sh my.homelab.com"
  echo "Example: ./generate-cert.sh 192.168.1.50"
  exit 1
fi

if [ -z "$PASSWORD" ]; then
  PASSWORD=$(openssl rand -base64 18)
fi

mkdir -p "$OUT_DIR"

# Numeric input -> IP SAN, otherwise DNS SAN (modern browsers require SAN, not just legacy CN).
if [[ "$HOST" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  SAN="subjectAltName=IP:${HOST}"
else
  SAN="subjectAltName=DNS:${HOST}"
fi

KEY_TMP=$(mktemp)
CERT_TMP=$(mktemp)
trap 'rm -f "$KEY_TMP" "$CERT_TMP"' EXIT

openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout "$KEY_TMP" -out "$CERT_TMP" -days 825 \
  -subj "/CN=${HOST}" -addext "$SAN"

openssl pkcs12 -export -out "$PFX_PATH" \
  -inkey "$KEY_TMP" -in "$CERT_TMP" -passout "pass:${PASSWORD}"

chmod 600 "$PFX_PATH"

echo ""
echo "Certificate written to: $PFX_PATH"
echo "Password: $PASSWORD"
echo ""
echo "Add to .env: HTTPS_PORT=5769, HOST_HTTPS_PORT=5769, CERT_PASSWORD=$PASSWORD"
echo "Then browse to https://${HOST}:5769 and accept the one-time self-signed warning."
