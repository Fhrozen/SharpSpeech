#!/bin/bash
# Generates a TLS certificate for serving SharpAudio over HTTPS on a LAN hostname/IP (required for
# browsers to expose navigator.mediaDevices outside of localhost, and for WebSocket connections to
# be trusted - browsers do NOT reliably extend a manually-bypassed self-signed warning to
# `new WebSocket()` handshakes). Prefers locally-installed mkcert, then dockerized mkcert
# (./docker/mkcert, no local install/sudo needed), then falls back to a plain openssl self-signed
# cert. mkcert output still needs its root CA imported into the BROWSER machine's trust store.
# Usage: ./generate-cert.sh <hostname-or-ip> [password]
set -e

HOST="$1"
PASSWORD="$2"
OUT_DIR="./certs"
PFX_PATH="${OUT_DIR}/sharpaudio.pfx"

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

KEY_TMP=$(mktemp)
CERT_TMP=$(mktemp)
trap 'rm -f "$KEY_TMP" "$CERT_TMP"' EXIT

if command -v mkcert >/dev/null 2>&1; then
  echo "mkcert found - generating a locally-trusted certificate (no browser warnings)."
  mkcert -install
  mkcert -cert-file "$CERT_TMP" -key-file "$KEY_TMP" "$HOST"
elif command -v docker >/dev/null 2>&1; then
  echo "mkcert not installed locally - using docker/mkcert instead (no sudo/local install needed)."
  docker build -t sharpaudio-mkcert ./docker/mkcert >/dev/null
  docker run --rm --user "$(id -u):$(id -g)" -v "$(pwd)/${OUT_DIR}:/certs" sharpaudio-mkcert "$HOST" "$PASSWORD"
  chmod 600 "$PFX_PATH"
  echo ""
  echo "Certificate written to: $PFX_PATH"
  echo "Password: $PASSWORD"
  echo ""
  echo "Add to .env: HTTPS_PORT=5769, HOST_HTTPS_PORT=5769, CERT_PASSWORD=$PASSWORD"
  echo "Then browse to https://${HOST}:5769"
  exit 0
else
  echo "mkcert not found - falling back to a self-signed certificate."
  echo "Browsers will show a one-time warning for the page, but may still reject WebSocket"
  echo "connections even after accepting it - install mkcert for a fully trusted cert instead:"
  echo "  https://github.com/FiloSottile/mkcert#installation"
  echo ""

  # Numeric input -> IP SAN, otherwise DNS SAN (modern browsers require SAN, not just legacy CN).
  if [[ "$HOST" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    SAN="subjectAltName=IP:${HOST}"
  else
    SAN="subjectAltName=DNS:${HOST}"
  fi

  openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout "$KEY_TMP" -out "$CERT_TMP" -days 825 \
    -subj "/CN=${HOST}" -addext "$SAN"
fi

openssl pkcs12 -export -out "$PFX_PATH" \
  -inkey "$KEY_TMP" -in "$CERT_TMP" -passout "pass:${PASSWORD}"

chmod 600 "$PFX_PATH"

echo ""
echo "Certificate written to: $PFX_PATH"
echo "Password: $PASSWORD"
echo ""
echo "Add to .env: HTTPS_PORT=5769, HOST_HTTPS_PORT=5769, CERT_PASSWORD=$PASSWORD"
echo "Then browse to https://${HOST}:5769"

