using SkiaSharp;

// Original, deliberately simple single-stroke glyphs. Coordinates are on a 4 x 6
// grid; spaces separate pen strokes. No font files, typefaces, shaping or font APIs.
public static class LineFont
{
    public static readonly Dictionary<char,float[][]> Glyphs=MakeGlyphs();
    public static Dictionary<char,float[][]> MakeGlyphs()
    {
        string[] definitions={
            "A:0,6 0,2 2,0 4,2 4,6|0,4 4,4",
            "B:0,6 0,0 3,0 4,1 4,2 3,3 0,3|3,3 4,4 4,5 3,6 0,6",
            "C:4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5",
            "D:0,6 0,0 2,0 4,2 4,4 2,6 0,6",
            "E:4,0 0,0 0,6 4,6|0,3 3,3",
            "F:0,6 0,0 4,0|0,3 3,3",
            "G:4,1 3,0 1,0 0,1 0,5 1,6 4,6 4,3 2,3",
            "H:0,0 0,6|4,0 4,6|0,3 4,3",
            "I:0,0 4,0|2,0 2,6|0,6 4,6",
            "J:0,0 4,0 4,5 3,6 1,6 0,5",
            "K:0,0 0,6|4,0 0,3 4,6",
            "L:0,0 0,6 4,6",
            "M:0,6 0,0 2,3 4,0 4,6",
            "N:0,6 0,0 4,6 4,0",
            "O:1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0",
            "P:0,6 0,0 3,0 4,1 4,2 3,3 0,3",
            "Q:1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|2,4 4,6",
            "R:0,6 0,0 3,0 4,1 4,2 3,3 0,3|2,3 4,6",
            "S:4,1 3,0 1,0 0,1 0,2 1,3 3,3 4,4 4,5 3,6 1,6 0,5",
            "T:0,0 4,0|2,0 2,6",
            "U:0,0 0,5 1,6 3,6 4,5 4,0",
            "V:0,0 2,6 4,0",
            "W:0,0 0,6 2,3 4,6 4,0",
            "X:0,0 4,6|4,0 0,6",
            "Y:0,0 2,3 4,0|2,3 2,6",
            "Z:0,0 4,0 0,6 4,6",
            "0:1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|0,5 4,1",
            "1:0,2 2,0 2,6|0,6 4,6",
            "2:0,1 1,0 3,0 4,1 4,2 0,6 4,6",
            "3:0,0 4,0 2,3 4,4 4,5 3,6 0,6|1,3 2,3",
            "4:3,6 3,0 0,4 4,4",
            "5:4,0 0,0 0,3 3,3 4,4 4,5 3,6 0,6",
            "6:4,0 1,0 0,1 0,5 1,6 3,6 4,5 4,4 3,3 0,3",
            "7:0,0 4,0 1,6",
            "8:1,0 3,0 4,1 4,2 3,3 1,3 0,2 0,1 1,0|1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3",
            "9:4,3 1,3 0,2 0,1 1,0 3,0 4,1 4,5 3,6 0,6",
            ".:2,5.7 2,6", ",:2,5 2,6 1,7", "-:0,3 4,3", "+:0,3 4,3|2,1 2,5",
            "=:0,2 4,2|0,4 4,4", "/:0,6 4,0", "^:0,3 2,0 4,3", "::2,1 2,2|2,4 2,5",
            "(:3,0 1,2 1,4 3,6", "):1,0 3,2 3,4 1,6", "[:4,0 1,0 1,6 4,6", "]:0,0 3,0 3,6 0,6",
            "_:0,6 4,6", "%:0,6 4,0|0,0 1,0 1,1 0,1 0,0|3,5 4,5 4,6 3,6 3,5",
            "?:0,1 1,0 3,0 4,1 4,2 2,3 2,4|2,5.7 2,6",
            "<:4,0 0,3 4,6", ">:0,0 4,3 0,6", "*:0,1 4,5|0,5 4,1|0,3 4,3", "!:2,0 2,4|2,5.7 2,6"
        };
        var glyphs=new Dictionary<char,float[][]>();
        foreach(string definition in definitions)
            glyphs[definition[0]]=definition[2..].Split('|').Select(stroke=>stroke.Split(' ').SelectMany(point=>point.Split(',').Select(n=>float.Parse(n,System.Globalization.CultureInfo.InvariantCulture))).ToArray()).ToArray();
        return glyphs;
    }
    public static void Draw(SKCanvas canvas,string text,float x,float baseline,SKTextAlign align,float size,SKPaint source,float maximumWidth=float.PositiveInfinity)
    {
        if (text.Length > 0) size=Math.Min(size,maximumWidth/(text.Length*.8f));
        float advance=size*.8f;float width=Math.Max(0,text.Length*advance-size*.12f);
        if(align==SKTextAlign.Center)x-=width*.5f;else if(align==SKTextAlign.Right)x-=width;
        using var paint=new SKPaint {Color=source.Color,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=Math.Max(1,size/18),StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
        foreach(char original in text.ToUpperInvariant())
        {
            char ch=original is 'Μ' or 'µ'?'U':original;
            if(ch!=' ')
            {
                float[][] strokes=Glyphs.TryGetValue(ch,out var glyph)?glyph:Glyphs['?'];
                foreach(float[] stroke in strokes)
                    for(int i=2;i+1<stroke.Length;i+=2)
                        canvas.DrawLine(x+stroke[i-2]*size*.15f,baseline-size+stroke[i-1]*size/6,x+stroke[i]*size*.15f,baseline-size+stroke[i+1]*size/6,paint);
            }
            x+=advance;
        }
    }
}
