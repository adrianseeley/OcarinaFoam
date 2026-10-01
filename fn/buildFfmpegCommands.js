// Generate documentation commands, not video files. Each enabled field has its own
// composite PNG sequence and FFV1/Matroska output. fps changes playback time only.
// The -y option overwrites existing videos. Numbered image input requires a contiguous
// sequence; a missing frame must be resolved before encoding the intended full run.

// Must match the Fields[] names baked into foamTemplate/render/FoamRenderer.cs.
const FIELD_NAMES = [
    { enabledKey: 'renderPressure', name: 'pressure' },
    { enabledKey: 'renderVelocityMagnitude', name: 'velocityMagnitude' },
    { enabledKey: 'renderDensity', name: 'density' },
    { enabledKey: 'renderTemperature', name: 'temperature' },
];

module.exports = function buildFfmpegCommands(config) {
    const fps = config.renderer.fps;
    const lines = [];
    for (const field of FIELD_NAMES) {
        if (!config.renderer[field.enabledKey]) {
            continue;
        }
        lines.push(
            `ffmpeg -y -framerate ${fps} -i "renders/%08d.${field.name}.png" `
            + `-c:v ffv1 -level 3 -pix_fmt rgb24 "videos/${field.name}.mkv"`,
        );
    }
    return lines.join('\n');
};
