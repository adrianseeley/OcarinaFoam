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
# git obtains source updates. systemd and its PAM integration provide the user manager;
# dbus-user-session provides the user bus used by systemctl --user.
# No Node, fonts, GUI, ParaView, tmux, or alternative process supervisor is required.
apt-get install -y openfoam2606-default openmpi-bin ffmpeg dotnet-sdk-10.0 git systemd libpam-systemd dbus-user-session

# Keep this user's manager alive after logout. Units are created on demand by the
# CLI, not enabled for boot: an operator explicitly starts each simulation/render.
loginctl enable-linger "$SUDO_USER"
user_id=$(id -u "$SUDO_USER")
systemctl start "user@${user_id}.service"

# A missing installation path is an error, not a reason to select another version.
test -f /usr/lib/openfoam/openfoam2606/etc/bashrc
dotnet --list-sdks
printf '\nPrepared. Log out and SSH in again as %s, then follow the README publish commands.\n' "$SUDO_USER"
