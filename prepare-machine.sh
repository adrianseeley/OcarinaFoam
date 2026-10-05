#!/usr/bin/env bash
# Prepare an Ubuntu 24.04 or 26.04 amd64 server for this repository's fixed
# OpenCFD v2606 baseline. Run from an ordinary SSH account:
# sudo bash prepare-machine.sh
# This script changes system package sources, installs dependencies, and enables
# lingering for that account so its user services survive SSH logout.
set -euo pipefail

# Refresh Ubuntu's package index. HTTPS roots authenticate package/download hosts;
# curl fetches the official OpenCFD repository setup script; gnupg verifies packages.
# software-properties-common manages Ubuntu's universe component for .NET.
apt-get update
apt-get install -y ca-certificates curl gnupg software-properties-common
add-apt-repository -y universe

# OpenCFD's package repository is distinct from the OpenFOAM Foundation packages.
# Download to a private temporary directory, then execute its official installer.
# The installer adds OpenCFD's signing key and apt source. Reruns are supported.
prep_tmp=$(mktemp -d)
trap 'rm -rf "$prep_tmp"' EXIT
curl --fail --show-error --location https://dl.openfoam.com/add-debian-repo.sh -o "$prep_tmp/openfoam-repo.sh"
bash "$prep_tmp/openfoam-repo.sh"
apt-get update

# v2606 supplies rhoPimpleFoam, mesh/surface utilities, libraries and dictionaries.
# MPI supplies mpirun and distributed solver execution. .NET 10 builds/runs the CLI.
# git obtains source updates. supervisor is the job watcher for hosts without systemd
# (containers); systemd hosts use their user manager (systemd, libpam-systemd and
# dbus-user-session provide it for systemctl --user). Both are installed so either
# "watcher" value in config.json works. No Node, fonts, GUI or ParaView is required.
apt-get install -y openfoam2606-default openmpi-bin ffmpeg dotnet-sdk-10.0 git supervisor systemd libpam-systemd dbus-user-session

# Works under sudo or as root (e.g. a container). Only a systemd-booted host has a
# user manager to keep alive; containers skip this and use "watcher": "supervisord".
target_user=${SUDO_USER:-$(id -un)}
if [ -d /run/systemd/system ]; then
    # Keep this user's manager alive after logout. Units are created on demand by the
    # CLI, not enabled for boot: an operator explicitly starts each simulation/render.
    loginctl enable-linger "$target_user"
    systemctl start "user@$(id -u "$target_user").service"
else
    printf '\nNo systemd detected (container). Set "watcher": "supervisord" in each case config.json.\n'
fi

# A missing installation path is an error, not a reason to select another version.
test -f /usr/lib/openfoam/openfoam2606/etc/bashrc
dotnet --list-sdks
printf '\nPrepared. Log out and SSH in again as %s, then follow the README publish commands.\n' "$target_user"
