using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Construct an orthographic camera around the padded solid bounds. All fluid
    // points are eligible to project, but far-field points can fall outside the tile.
    // Corner views are weighted by object dimensions; they are not necessarily equal-
    // angle isometric views. Tilt changes orientation only, never simulation geometry.
    public static Camera MakeCamera(View view, float offsetX, float offsetY)
    {
        Camera camera = new Camera();
        Vector3 half = (ObjectMaximum - ObjectMinimum) * 0.5f * (1f + CameraPaddingFraction);
        camera.Centre = (ObjectMaximum + ObjectMinimum) * 0.5f;
        camera.TowardEye = Vector3.Normalize(view.From * half);
        camera.Right = Vector3.Normalize(Vector3.Cross(view.Up, camera.TowardEye));
        camera.Up = Vector3.Cross(camera.TowardEye, camera.Right);
        float tilt = AxisTiltDegrees * MathF.PI / 180f;
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Right, tilt));
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Up, tilt));
        camera.Right = Vector3.Normalize(Vector3.Cross(view.Up, camera.TowardEye));
        camera.Up = Vector3.Cross(camera.TowardEye, camera.Right);
        float extentX = Math.Abs(camera.Right.X) * half.X + Math.Abs(camera.Right.Y) * half.Y + Math.Abs(camera.Right.Z) * half.Z;
        float extentY = Math.Abs(camera.Up.X) * half.X + Math.Abs(camera.Up.Y) * half.Y + Math.Abs(camera.Up.Z) * half.Z;
        camera.Scale = Math.Min((PlotWidth - 2 * MarginPixels) / (2 * extentX), (PlotHeight - 2 * MarginPixels) / (2 * extentY));
        camera.ScreenX = offsetX + PlotWidth * 0.5f;
        camera.ScreenY = offsetY + PlotHeight * 0.5f;
        return camera;
    }
}
