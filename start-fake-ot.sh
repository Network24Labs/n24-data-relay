# start-fake-ot.sh
#!/usr/bin/env bash

# Fake OT environment: full OpenSSH server with SCP support (SSH-based) for N24 Data Relay testing.
# Uses deploy/fake-ot/Dockerfile (Alpine + openssh), not SFTP-only images.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINER_NAME="fake-ot-sshd"
HOST_PORT="2222"
SSH_USER="otuser"
SSH_PASS="supersecret123"          # Change this in prod/testing if needed!
VOLUME_DIR="$HOME/n24-test-uploads" # Where transferred files appear on your host
IMAGE_NAME="n24-fake-ot"

# Create volume dir if it doesn't exist
mkdir -p "$VOLUME_DIR"

function start() {
    if [ -n "$(docker ps -q -f name="^${CONTAINER_NAME}$")" ]; then
        echo "Container '${CONTAINER_NAME}' is already running."
        exit 0
    fi

    if ! docker image inspect "${IMAGE_NAME}:latest" >/dev/null 2>&1; then
        echo "Building ${IMAGE_NAME} from deploy/fake-ot/Dockerfile..."
        docker build -t "${IMAGE_NAME}:latest" "${SCRIPT_DIR}/deploy/fake-ot" || exit 1
    fi

    echo "Starting fake OT SSH/SCP container..."
    docker run -d \
        --name "${CONTAINER_NAME}" \
        -p "${HOST_PORT}":22 \
        -v "${VOLUME_DIR}":/home/"${SSH_USER}"/incoming \
        "${IMAGE_NAME}:latest"

    if [ $? -eq 0 ]; then
        echo "Started successfully!"
        echo ""
        echo "Connection details for your app:"
        echo "  Host:     127.0.0.1   (use this, not localhost — avoids IPv6 connection refused)"
        echo "  Port:     ${HOST_PORT}"
        echo "  User:     ${SSH_USER}"
        echo "  Password: ${SSH_PASS}"
        echo "  Remote dir: incoming/  (or /home/${SSH_USER}/incoming/)"
        echo ""
        echo "Files transferred via SCP will appear in: ${VOLUME_DIR}"
        echo "Quick test from host:"
        echo "  scp -P ${HOST_PORT} somefile.txt ${SSH_USER}@127.0.0.1:incoming/"
    else
        echo "Failed to start. Check output above or run:"
        echo "  docker logs ${CONTAINER_NAME}"
        exit 1
    fi
}

function stop() {
    if [ -z "$(docker ps -q -f name="^${CONTAINER_NAME}$")" ]; then
        echo "Container '${CONTAINER_NAME}' is not running."
        exit 0
    fi

    echo "Stopping and removing '${CONTAINER_NAME}'..."
    docker stop "${CONTAINER_NAME}" >/dev/null
    docker rm "${CONTAINER_NAME}" >/dev/null
    echo "Done."
}

function status() {
    docker ps -f name="^${CONTAINER_NAME}$"
    if [ -z "$(docker ps -q -f name="^${CONTAINER_NAME}$")" ]; then
        echo "(not running)"
    fi
}

function logs() {
    docker logs "${CONTAINER_NAME}"
}

case "$1" in
    start)   start ;;
    stop)    stop ;;
    status)  status ;;
    logs)    logs ;;
    *)
        echo "Usage: $(basename "$0") {start|stop|status|logs}"
        echo ""
        echo "  start   → Build (if needed) and launch the fake OT SSH/SCP server"
        echo "  stop    → Shut it down and clean up"
        echo "  status  → Show running status"
        echo "  logs    → View container logs"
        exit 1
        ;;
esac