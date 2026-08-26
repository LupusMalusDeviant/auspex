# syntax=docker/dockerfile:1

FROM golang:1.26-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/auspex ./cmd/auspex && \
    CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/auspexdig ./cmd/auspexdig

FROM alpine:3.21
RUN apk add --no-cache ca-certificates tzdata libcap && \
    adduser -D -H -u 10001 auspex
COPY --from=build /out/auspex /out/auspexdig /usr/local/bin/
# Port 53 ohne root: die Fähigkeit hängt am Binary, nicht am Benutzer.
RUN setcap cap_net_bind_service=+ep /usr/local/bin/auspex
RUN mkdir -p /var/lib/auspex && chown auspex /var/lib/auspex
# Das geteilte Verzeichnis legen BEIDE Images identisch an, mit der UID der
# Control-Plane (10002): Docker initialisiert ein leeres Named Volume aus dem
# Image des Containers, der es zuerst mountet - und das ist je nach
# Startreihenfolge mal der eine, mal der andere. Waere es hier root-owned,
# koennte die Control-Plane keine Ausnahme schreiben, und der Klick in der
# Oberflaeche scheiterte stumm. Auspex liest hier nur.
RUN mkdir -p /var/lib/auspex-shared && chown 10002:10002 /var/lib/auspex-shared
USER auspex
WORKDIR /var/lib/auspex
EXPOSE 53/udp 53/tcp 5380/tcp
ENTRYPOINT ["/usr/local/bin/auspex", "-config", "/etc/auspex/config.yaml"]
