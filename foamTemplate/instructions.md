# {{CASE_NAME}}

## Build and decompose

source {{OPENFOAM_BASHRC}};
surfaceCheck constant/triSurface/solidBody.stl 2>&1 | tee log.surfaceCheck;
surfaceFeatureExtract 2>&1 | tee log.surfaceFeatureExtract;
blockMesh 2>&1 | tee log.blockMesh;
checkMesh -allTopology -allGeometry 2>&1 | tee log.checkMesh.block;
snappyHexMesh -overwrite 2>&1 | tee log.snappyHexMesh;
checkMesh -allTopology -allGeometry 2>&1 | tee log.checkMesh.snappy;
decomposePar -force 2>&1 | tee log.decomposePar;

## Install services

sudo cp systemd/{{CASE_NAME}}-solver.service /etc/systemd/system/
sudo cp systemd/{{CASE_NAME}}-renderer.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now {{CASE_NAME}}-solver.service
sudo systemctl enable --now {{CASE_NAME}}-renderer.service

## Monitor

systemctl --no-pager status {{CASE_NAME}}-solver.service
systemctl --no-pager status {{CASE_NAME}}-renderer.service

journalctl -fu {{CASE_NAME}}-solver.service
journalctl -fu {{CASE_NAME}}-renderer.service

## Stop or restart

sudo systemctl stop {{CASE_NAME}}-solver.service
sudo systemctl stop {{CASE_NAME}}-renderer.service

sudo systemctl restart {{CASE_NAME}}-solver.service
sudo systemctl restart {{CASE_NAME}}-renderer.service

## Export videos

```text
{{FFMPEG_COMMANDS}}
```