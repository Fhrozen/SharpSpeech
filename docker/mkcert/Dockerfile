FROM alpine:3.20
RUN apk add --no-cache curl openssl bash ca-certificates
RUN set -e; \
    case "$(uname -m)" in \
      x86_64) MKARCH=amd64 ;; \
      aarch64) MKARCH=arm64 ;; \
      *) echo "unsupported arch $(uname -m)" >&2; exit 1 ;; \
    esac; \
    curl -JLo /usr/local/bin/mkcert "https://dl.filippo.io/mkcert/latest?for=linux/${MKARCH}"; \
    chmod +x /usr/local/bin/mkcert
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
