# Build context: contracts/evm
FROM debian:bookworm-slim

ARG RUNDLER_VERSION=v0.11.0
ARG RUNDLER_ARCH=x86_64-unknown-linux-gnu

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl tar \
    && rm -rf /var/lib/apt/lists/* \
    && curl --fail --location --silent --show-error \
      "https://github.com/alchemyplatform/rundler/releases/download/${RUNDLER_VERSION}/rundler-${RUNDLER_VERSION}-${RUNDLER_ARCH}.tar.gz" \
      --output /tmp/rundler.tar.gz \
    && mkdir -p /opt/rundler \
    && tar -xzf /tmp/rundler.tar.gz -C /opt/rundler \
    && rm /tmp/rundler.tar.gz \
    && chmod +x /opt/rundler/rundler

COPY scripts/rundler-chain-spec-sepolia.toml /etc/rundler/chain-spec.toml
COPY rundler/entrypoint.sh /usr/local/bin/rundler-entrypoint

RUN chmod +x /usr/local/bin/rundler-entrypoint \
    && useradd --system --create-home --uid 10001 rundler

USER rundler
EXPOSE 4338
ENTRYPOINT ["/usr/local/bin/rundler-entrypoint"]
