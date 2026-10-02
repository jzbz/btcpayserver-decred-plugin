# Single image containing dcrd and dcrwallet binaries.
# Build and publish as: joegruffins/decred:2.1.6 (or whatever your Docker Hub account is)
# Usage:
#   docker run joegruffins/decred dcrd [args...]
#   docker run joegruffins/decred dcrwallet [args...]

FROM debian:bookworm-slim AS builder

ARG DECRED_VERSION=2.1.6
ARG TARGETOS=linux
ARG TARGETARCH=amd64

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

RUN curl -L "https://github.com/decred/decred-binaries/releases/download/v${DECRED_VERSION}/decred-${TARGETOS}-${TARGETARCH}-v${DECRED_VERSION}.tar.gz" \
    -o /tmp/decred.tar.gz && \
    tar -xzf /tmp/decred.tar.gz -C /tmp && \
    mv /tmp/decred-${TARGETOS}-${TARGETARCH}-v${DECRED_VERSION} /tmp/decred

FROM debian:bookworm-slim

RUN useradd -m -s /bin/bash decred

COPY --from=builder /tmp/decred/dcrd /usr/local/bin/
COPY --from=builder /tmp/decred/dcrwallet /usr/local/bin/
COPY --from=builder /tmp/decred/dcrctl /usr/local/bin/

# Notification helper script (curl-based, for --block-notify / dcrwallet --txnotify)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

USER decred
WORKDIR /home/decred

VOLUME ["/home/decred/.dcrd", "/home/decred/.dcrwallet"]

# Default to dcrd; override with dcrwallet for wallet container
ENTRYPOINT []
CMD ["dcrd"]
