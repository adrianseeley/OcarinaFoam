using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    public static Camera MakeCamera(RenderTileDefinition tile, SKRect content)
    {
        Camera camera = new Camera();
        Vector3 half = (ObjectMaximum - ObjectMinimum) * 0.5f * (1f + CameraPaddingFraction);
        camera.Centre = new Vector3((float)(tile.TargetMillimeters[0] * .001), (float)(tile.TargetMillimeters[1] * .001), (float)(tile.TargetMillimeters[2] * .001));
        camera.TowardEye = Vector3.Normalize(new Vector3((float)tile.From[0], (float)tile.From[1], (float)tile.From[2]));
        Vector3 upHint = tile.Up == null ? DefaultUp(camera.TowardEye) : Vector3.Normalize(new Vector3((float)tile.Up[0], (float)tile.Up[1], (float)tile.Up[2]));
        Vector3 right = Vector3.Cross(upHint, camera.TowardEye);
        if (right.LengthSquared() <= 1e-12f) throw new Exception("renderer.tiles camera up direction is parallel to from.");
        camera.Right = Vector3.Normalize(right);
        camera.Up = Vector3.Normalize(Vector3.Cross(camera.TowardEye, camera.Right));
        float tilt = AxisTiltDegrees * MathF.PI / 180f;
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Right, tilt));
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Up, tilt));
        Vector3 tiltedRight = Vector3.Cross(upHint, camera.TowardEye);
        if (tiltedRight.LengthSquared() <= 1e-12f) throw new Exception("renderer axisTiltDegrees creates a degenerate camera basis.");
        camera.Right = Vector3.Normalize(tiltedRight);
        camera.Up = Vector3.Normalize(Vector3.Cross(camera.TowardEye, camera.Right));
        float extentX = Math.Abs(camera.Right.X) * half.X + Math.Abs(camera.Right.Y) * half.Y + Math.Abs(camera.Right.Z) * half.Z;
        float extentY = Math.Abs(camera.Up.X) * half.X + Math.Abs(camera.Up.Y) * half.Y + Math.Abs(camera.Up.Z) * half.Z;
        if (!float.IsFinite(extentX) || !float.IsFinite(extentY) || extentX <= 0 || extentY <= 0) throw new Exception("renderer camera extents are degenerate.");
        float width = content.Right - content.Left;
        float height = content.Bottom - content.Top;
        float baseScale = Math.Min(width / (2 * extentX), height / (2 * extentY));
        camera.Scale = baseScale * (float)tile.Zoom;
        if (!float.IsFinite(camera.Scale) || camera.Scale <= 0) throw new Exception("renderer camera scale is invalid.");
        camera.ScreenX = (content.Left + content.Right) * 0.5f;
        camera.ScreenY = (content.Top + content.Bottom) * 0.5f;
        return camera;
    }

    public static Vector3 DefaultUp(Vector3 towardEye)
    {
        Vector3 axis = Math.Abs(Vector3.Dot(towardEye, Vector3.UnitZ)) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;
        return axis;
    }
}
