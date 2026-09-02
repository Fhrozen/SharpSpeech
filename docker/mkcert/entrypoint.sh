#!/bin/bash
# Runs inside the fastttsr-mkcert image: issues a mkcert-signed cert for $1, exports it as a PFX
# for Kestrel. The CA lives under the mounted /certs volume so it (and its rootCA.pem) persist on
# the host across runs - mkcert -install can't reach a trust store from inside a container, so the
# caller must import /certs/mkcert-ca/rootCA.pem into the browser machine's trust store manually.
set -e

HOST="$1"
PASSWORD="$2"

export CAROOT=/certs/mkcert-ca
mkdir -p "$CAROOT"

mkcert -install || true

mkcert -cert-file /tmp/cert.pem -key-file /tmp/key.pem "$HOST"

openssl pkcs12 -export -out /certs/fastttsr.pfx \
  -inkey /tmp/key.pem -in /tmp/cert.pem -passout "pass:${PASSWORD}"

chmod 600 /certs/fastttsr.pfx

echo ""
echo "Root CA: ${CAROOT}/rootCA.pem"
echo "Import that file into your BROWSER machine's trust store (it won't be trusted otherwise -"
echo "mkcert can't reach a browser trust store from inside a container)."
