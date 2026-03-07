# start-fake-ot.sh
#!/usr/bin/env bash

# Quick helper to manage a fake OT SFTP server (atmoz/sftp) for N24 Data Relay SCP testing

CONTAINER_NAME="fake-ot-sftp"
HOST_PORT="2222"
SFTP_USER="otuser"
SFTP_PASS="supersecret123"          # Change this in prod/testing if needed!
VOLUME_DIR="$HOME/n24-test-uploads" # Where transferred files appear on your host
IMAGE="docker.io/atmoz/sftp"

# Create volume dir if it doesn't exist
mkdir -p "$VOLUME_DIR"

function start() {
    if docker ps -q -f name="^${CONTAINER_NAME}$" >/dev/null; then
        echo "Container '${CONTAINER_NAME}' is already running."
        exit 0
    fi

    echo "Starting fake OT SFTP container..."
    docker run -d \
        --name "${CONTAINER_NAME}" \
        -p "${HOST_PORT}":22 \
        -v "${VOLUME_DIR}":/home/"${SFTP_USER}"/incoming \
        "${IMAGE}" \
        "${SFTP_USER}:${SFTP_PASS}:::incoming"

    if [ $? -eq 0 ]; then
        echo "Started successfully!"
        echo ""
        echo "Connection details for your app:"
        echo "  Host:     localhost"
        echo "  Port:     ${HOST_PORT}"
        echo "  User:     ${SFTP_USER}"
        echo "  Password: ${SFTP_PASS}"
        echo "  Remote dir: incoming/  (or /home/${SFTP_USER}/incoming/)"
        echo ""
        echo "Files transferred via SCP will appear in: ${VOLUME_DIR}"
        echo "Quick test from host:"
        echo "  scp -P ${HOST_PORT} somefile.txt ${SFTP_USER}@localhost:incoming/"
    else
        echo "Failed to start. Check output above or run:"
        echo "  docker logs ${CONTAINER_NAME}"
        exit 1
    fi
}

function stop() {
    if ! docker ps -q -f name="^${CONTAINER_NAME}$" >/dev/null; then
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
    if ! docker ps -q -f name="^${CONTAINER_NAME}$" >/dev/null; then
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
        echo "  start   → Launch the fake OT SFTP server"
        echo "  stop    → Shut it down and clean up"
        echo "  status  → Show running status"
        echo "  logs    → View container logs"
        exit 1
        ;;
esac