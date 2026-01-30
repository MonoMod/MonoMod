FROM --platform=linux/arm64 ubuntu:24.04

# First, packages
RUN apt-get update \
 && apt-get upgrade -y \
 && apt-get install --no-install-recommends -y \
        apt-transport-https software-properties-common \
        git git-lfs curl wget bash sudo lldb \
        ca-certificates-mono mono-runtime mono-runtime-dbg mono-utils mono-gac mono-devel \
 && add-apt-repository ppa:dotnet/backports \
 && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
 && dpkg -i packages-microsoft-prod.deb \
 && rm packages-microsoft-prod.deb \
 && curl -fsSL https://deb.nodesource.com/setup_23.x | bash \
 && apt-get update \
 && apt-get install --no-install-recommends -y \
        nodejs \
        dotnet-runtime-9.0 \
        powershell \
 && apt-get remove -y apt-transport-https software-properties-common \
 && apt-get autoremove -y \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

# Then, user
RUN useradd -rm -d /home/runner -s /bin/bash -g root -G sudo -u 1001 runner \
    && echo "runner ALL=(ALL) NOPASSWD: ALL" > /etc/sudoers.d/runner \
    && chmod 0440 /etc/sudoers.d/runner
USER runner
WORKDIR /home/runner

# Older runtimes can't find a valid libicu, and we don't particularly care about globalization anyway
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
