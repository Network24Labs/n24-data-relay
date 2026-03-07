# Fake OT environment: full OpenSSH server with SCP support (SSH-based, not SFTP-only).
# Used by start-fake-ot.sh for N24 Data Relay transfer testing.
FROM alpine:3.19

RUN apk add --no-cache openssh \
    && ssh-keygen -A \
    && adduser -D -h /home/otuser otuser

# Set password for otuser (change in prod); Alpine's adduser doesn't support -s password
RUN echo "otuser:supersecret123" | chpasswd

# Incoming dir: bind-mount over this at run time so host sees transferred files
RUN mkdir -p /home/otuser/incoming && chown otuser:otuser /home/otuser/incoming

# Allow password auth and restrict to otuser
RUN sed -i 's/^#*PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config \
    && sed -i 's/^#*PasswordAuthentication.*/PasswordAuthentication yes/' /etc/ssh/sshd_config \
    && echo "AllowUsers otuser" >> /etc/ssh/sshd_config

EXPOSE 22
CMD ["/usr/sbin/sshd", "-D"]
