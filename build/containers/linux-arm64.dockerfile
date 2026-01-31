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
 && curl -fsSL https://deb.nodesource.com/setup_25.x | bash \
 && apt-get update \
 && apt-get install --no-install-recommends -y \
        nodejs \
        dotnet-runtime-10.0 \
 && apt-get remove -y apt-transport-https software-properties-common \
 && apt-get autoremove -y \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

# The powershell packages don't have arm64 variants, so install it like we do on musl
RUN curl -L \
         https://github.com/PowerShell/PowerShell/releases/download/v7.5.4/powershell-7.5.4-linux-arm64.tar.gz \
         -o /tmp/powershell.tar.gz \
 && mkdir -p /opt/powershell \
 && tar xzf /tmp/powershell.tar.gz -C /opt/powershell \
 && chmod +x /opt/powershell/pwsh \
 && ln -s /opt/powershell/pwsh /usr/bin/pwsh \
 && rm /tmp/powershell.tar.gz

# Then, user
RUN useradd -rm -d /home/runner -s /bin/bash -g root -G sudo -u 1001 runner \
    && echo "runner ALL=(ALL) NOPASSWD: ALL" > /etc/sudoers.d/runner \
    && chmod 0440 /etc/sudoers.d/runner
USER runner
WORKDIR /home/runner

# Older runtimes can't find a valid libicu, and we don't particularly care about globalization anyway
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
