using System.Globalization;

public static partial class Render
{
    public const int HeaderHeight = 100;
    public const int SectionHeight = 152;
    public const int SectionPadding = 16;
    public const int LegendHeight = SectionHeight * 3;
    public const int ImageHeight = HeaderHeight + Size + LegendHeight;
    const int LegendWidth = Size - 64;
    const int LegendLeft = 32;
    const int HistogramHeight = 64;
    static readonly (byte R, byte G, byte B) MuteGreen = (80, 220, 120);
    static readonly (byte R, byte G, byte B) OffRed = (240, 90, 90);

    static int[] Histogram(double[] values, bool[] selected, double min, double max)
    {
        var bins = new int[128];
        double range = max > min ? max - min : 1;
        for (int i = 0; i < values.Length; i++)
        {
            if (!selected[i] || !double.IsFinite(values[i])) continue;
            double t = Math.Clamp((values[i] - min) / range, 0, 1);
            bins[Math.Min(bins.Length - 1, (int)(t * bins.Length))]++;
        }
        return bins;
    }

    static string[] Metadata(string root, string frame, Settings settings, Condition[] conditions, int shown, int selected, int total, int afterRank)
    {
        string filter = conditions.Length == 0 ? "NO FILTER" : string.Join(" && ", conditions.Select(c =>
            $"{ "XYZ"[c.Axis]} {c.Operator} {c.Value.ToString("G4", CultureInfo.InvariantCulture)}")) + " MM";
        return new[] {
            $"{Path.GetFileName(root)}  {settings.field}  {settings.dataMode}  {settings.view}",
            filter, $"{total} POINTS", $"{selected} POINTS", $"{shown} POINTS", $"{afterRank} POINTS"
        };
    }

    static void DrawLegend(byte[] image, string color, double min, double max, double[] values, bool[] selected,
        string[] metadata,
        (bool[] Hidden, (double Low, double High)[] Bands, (double Low, double High)[] Ranks, int Count) bands, double[] activity,
        (bool[] Hidden, (double Low, double High)[] Bands, (double Low, double High)[] Ranks, int Count) deltaBands,
        Settings settings, string frame, string finalFrame, double progress)
    {
        var white = ((byte)255, (byte)255, (byte)255);
        int countWidth = metadata.Skip(2).Max(text => TextWidth(text));
        int contentWidth = Size - countWidth - 64;
        HeaderRow(image, metadata[0], metadata[2], 12, contentWidth);
        HeaderRow(image, metadata[1], metadata[3], 34, contentWidth);
        void MuteRow(string title, double? low, double? middle, double? high, string count, int y)
        {
            var parts = new List<(string Text, (byte R, byte G, byte B) Color)> { (title.PadRight(10) + "  ", white) };
            void Band(string label, double? value)
            {
                parts.Add((label, white));
                parts.Add((value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "OFF ", value.HasValue ? MuteGreen : OffRed));
            }
            Band("LOW ", low); Band("  MID ", middle); Band("  HIGH ", high);
            CenterParts(image, parts, y, 2, LegendLeft, contentWidth, alignLeft: true);
            if (count.Length > 0) LegendText(image, count, Size - LegendLeft - TextWidth(count), y);
        }
        MuteRow("RANK MUTE", settings.removeRankBandLow, settings.removeRankBandMiddle, settings.removeRankBandHigh, metadata[5], 56);
        MuteRow("DELTA MUTE", settings.removeDeltaRankBandLow, settings.removeDeltaRankBandMiddle, settings.removeDeltaRankBandHigh, metadata[4], 78);
        DrawDistribution(image, color, min, max, values, selected, bands, HeaderHeight + Size + SectionPadding, false, settings);
        double[] finite = activity == null ? Array.Empty<double>() : activity.Where((v, i) => selected[i] && double.IsFinite(v)).ToArray();
        DrawDistribution(image, color, finite.Length > 0 ? finite.Min() : 0, finite.Length > 0 ? finite.Max() : 0,
            activity ?? new double[values.Length], selected, deltaBands, HeaderHeight + Size + SectionHeight + SectionPadding, true, settings);
        if (activity == null) LegendText(image, "NO PRECEDING FRAME", LegendLeft, HeaderHeight + Size + SectionHeight + SectionPadding + 4, 1);
        int timeTop = HeaderHeight + Size + SectionHeight * 2 + SectionPadding;
        for (int xx = 0; xx < LegendWidth; xx++)
            for (int y = timeTop + 32; y < timeTop + 48; y++)
            {
                byte v = xx < Math.Round(Math.Clamp(progress, 0, 1) * LegendWidth) ? (byte)230 : (byte)55;
                LegendPixel(image, LegendLeft + xx, y, v, v, v);
            }
        var timeParts = new List<(string Text, (byte R, byte G, byte B) Color)>();
        void Stamp(string stamp)
        {
            timeParts.Add((stamp.Substring(0, 3), (255, 195, 80)));
            timeParts.Add((" s ", white));
            timeParts.Add((stamp.Substring(4, 3), (80, 205, 255)));
            timeParts.Add((" ms ", white));
            timeParts.Add((stamp.Substring(9, 3), (205, 145, 255)));
            timeParts.Add((" us ", white));
            timeParts.Add((stamp.Substring(14, 3), (255, 135, 165)));
            timeParts.Add((" ns", white));
        }
        Stamp(frame);
        int stampWidth = (timeParts.Sum(p => p.Text.Length) * 6 - 1) * 2;
        CenterParts(image, timeParts, timeTop + 82, 2, LegendLeft, stampWidth);
        timeParts.Clear();
        Stamp(finalFrame);
        CenterParts(image, timeParts, timeTop + 82, 2, Size - LegendLeft - stampWidth, stampWidth);
        int arrowY = timeTop + 88, arrowTip = Size / 2 + 14;
        for (int x = Size / 2 - 14; x <= arrowTip; x++)
            for (int dy = 0; dy < 2; dy++) LegendPixel(image, x, arrowY + dy, 255, 255, 255);
        for (int step = 0; step <= 7; step++)
            for (int dy = 0; dy < 2; dy++)
            {
                LegendPixel(image, arrowTip - step, arrowY - step + dy, 255, 255, 255);
                LegendPixel(image, arrowTip - step, arrowY + step + dy, 255, 255, 255);
            }
        int footerTop = HeaderHeight + Size;
        for (int section = 0; section < 3; section++)
            WhiteBorder(image, 8, footerTop + section * SectionHeight, Size - 16, SectionHeight - 1);
    }

    static void WhiteBorder(byte[] image, int left, int top, int width, int height)
    {
        for (int x = left; x < left + width; x++)
        {
            LegendPixel(image, x, top, 255, 255, 255);
            LegendPixel(image, x, top + height - 1, 255, 255, 255);
        }
        for (int y = top; y < top + height; y++)
        {
            LegendPixel(image, left, y, 255, 255, 255);
            LegendPixel(image, left + width - 1, y, 255, 255, 255);
        }
    }

    static void DrawDistribution(byte[] image, string color, double min, double max, double[] values, bool[] selected,
        (bool[] Hidden, (double Low, double High)[] Bands, (double Low, double High)[] Ranks, int Count) bands, int top, bool rankOrdered, Settings settings)
    {
        int[] bins = Histogram(values, selected, min, max);
        double[] sorted = rankOrdered ? values.Where((v, i) => selected[i] && double.IsFinite(v)).OrderBy(v => v).ToArray() : null;
        int peak = bins.Max(), previousY = 0;
        double range = max > min ? max - min : 1;
        int BandX(double value) => (int)Math.Round(Math.Clamp((value - min) / range, 0, 1) * (LegendWidth - 1));
        int RankX(double rank) => Math.Clamp((int)Math.Round(rank * LegendWidth), 0, LegendWidth - 1);
        var intervals = rankOrdered
            ? bands.Ranks.Select(b => (Left: RankX(b.Low), Right: RankX(b.High))).ToArray()
            : bands.Bands.Select(b => (Left: BandX(b.Low), Right: BandX(b.High))).ToArray();
        for (int x = 0; x < LegendWidth; x++)
        {
            bool muted = intervals.Any(b => x >= b.Left && x <= b.Right);
            if (muted)
                for (int y = top; y <= top + HistogramHeight; y++) LegendPixel(image, LegendLeft + x, y, 18, 60, 30);
            double position = Math.Clamp(x / (double)(LegendWidth - 1) * bins.Length - .5, 0, bins.Length - 1);
            int left = (int)position, right = Math.Min(left + 1, bins.Length - 1);
            double count = bins[left] + (bins[right] - bins[left]) * (position - left);
            int curveY = top + HistogramHeight - (peak > 0 ? (int)Math.Round(count / peak * HistogramHeight) : 0);
            if (rankOrdered)
            {
                double rank = x / (double)(LegendWidth - 1) * Math.Max(0, sorted.Length - 1);
                int first = (int)rank, last = Math.Min(first + 1, sorted.Length - 1);
                double change = sorted.Length > 0 ? sorted[first] + (sorted[last] - sorted[first]) * (rank - first) : 0;
                curveY = top + HistogramHeight - (max > 0 ? (int)Math.Round(change / max * HistogramHeight) : 0);
            }
            for (int y = x == 0 ? curveY : Math.Min(previousY, curveY); y <= Math.Max(x == 0 ? curveY : previousY, curveY); y++)
                LegendPixel(image, LegendLeft + x, y, 255, 255, 255);
            previousY = curveY;
            if (!rankOrdered)
            {
                var rgb = muted ? MuteGreen : Color(color, x / (double)(LegendWidth - 1));
                for (int y = top + 69; y < top + 75; y++) LegendPixel(image, LegendLeft + x, y, rgb.Item1, rgb.Item2, rgb.Item3);
            }
        }
        foreach (int edge in intervals.SelectMany(b => new[] { b.Left, b.Right }))
            for (int y = top; y < top + (rankOrdered ? HistogramHeight + 1 : 75); y++) LegendPixel(image, LegendLeft + edge, y, MuteGreen.R, MuteGreen.G, MuteGreen.B);
        string low = rankOrdered ? "0.00" : min.ToString("G6", CultureInfo.InvariantCulture);
        string high = rankOrdered ? "1.00" : max.ToString("G6", CultureInfo.InvariantCulture);
        if (!rankOrdered)
        {
            WhiteBorder(image, LegendLeft - 1, top + 68, LegendWidth + 2, 8);
            CenterText(image, $"{settings.color}  {settings.pointSize} PX @ {settings.alpha.ToString("G4", CultureInfo.InvariantCulture)} ALPHA", top + 82, 2);
        }
        LegendText(image, low, LegendLeft, top + 104, 2);
        LegendText(image, high, Size - LegendLeft - TextWidth(high, 2), top + 104, 2);
        CenterText(image, rankOrdered ? "DELTA RANK" : "PA", top + 104, 2);
        if (intervals.Length > 0) LegendText(image, "MUTE ACTIVE", Size - LegendLeft - TextWidth("MUTE ACTIVE", 1), top + 3, 1, MuteGreen);
    }

    static void LegendPixel(byte[] image, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || x >= Size || y < 0 || y >= image.Length / (Size * 3)) return;
        int p = (y * Size + x) * 3;
        image[p] = b; image[p + 1] = g; image[p + 2] = r;
    }

    // A small bitmap font keeps scientific-number labels portable without native graphics libraries.
    static readonly Dictionary<char, string[]> LegendGlyphs = new()
    {
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
        ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
        ['6'] = new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" },
        ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
        ['+'] = new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
        ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "00110", "00110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
        ['C'] = new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
        ['G'] = new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" },
        ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
        ['J'] = new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" },
        ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['N'] = new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" },
        ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
        ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
        ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
        [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
        [':'] = new[] { "00000", "00100", "00100", "00000", "00100", "00100", "00000" },
        ['_'] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "11111" },
        ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
        ['('] = new[] { "00010", "00100", "01000", "01000", "01000", "00100", "00010" },
        [')'] = new[] { "01000", "00100", "00010", "00010", "00010", "00100", "01000" },
        ['<'] = new[] { "00001", "00010", "00100", "01000", "00100", "00010", "00001" },
        ['>'] = new[] { "10000", "01000", "00100", "00010", "00100", "01000", "10000" },
        ['='] = new[] { "00000", "00000", "11111", "00000", "11111", "00000", "00000" },
        ['!'] = new[] { "00100", "00100", "00100", "00100", "00100", "00000", "00100" },
        ['&'] = new[] { "01100", "10010", "10100", "01000", "10101", "10010", "01101" },
        ['?'] = new[] { "01110", "10001", "00001", "00010", "00100", "00000", "00100" },
        [','] = new[] { "00000", "00000", "00000", "00000", "00100", "00100", "01000" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['|'] = new[] { "00100", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['@'] = new[] { "01110", "10001", "10111", "10101", "10111", "10000", "01111" },
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" }
    };
    const int TextScale = 2;
    static int TextWidth(string text, int scale = TextScale) => (text.Length * 6 - 1) * scale;

    static void HeaderRow(byte[] image, string text, string count, int y, int contentWidth)
    {
        CenterParts(image, new[] { (text, ((byte)255, (byte)255, (byte)255)) }, y, 2, LegendLeft, contentWidth, alignLeft: true);
        LegendText(image, count, Size - LegendLeft - TextWidth(count), y);
    }

    static void CenterText(byte[] image, string text, int y, int scale) =>
        CenterParts(image, new[] { (text, ((byte)255, (byte)255, (byte)255)) }, y, scale);

    static void CenterParts(byte[] image, IEnumerable<(string Text, (byte R, byte G, byte B) Color)> parts, int y, int scale, int left = LegendLeft, int availableWidth = LegendWidth, bool alignLeft = false)
    {
        var segments = parts.ToArray();
        int width = (segments.Sum(p => p.Text.Length) * 6 - 1) * scale;
        double factor = Math.Min(1, availableWidth / (double)Math.Max(1, width));
        double x = alignLeft ? left : left + (availableWidth - width * factor) / 2;
        foreach (var part in segments)
        foreach (char c in part.Text)
        {
            string[] glyph = LegendGlyphs.GetValueOrDefault(char.ToUpperInvariant(c), LegendGlyphs['?']);
            for (int row = 0; row < 7; row++)
            for (int col = 0; col < 5; col++)
                if (glyph[row][col] == '1')
                    for (int dy = 0; dy < scale; dy++)
                    for (int xx = (int)(x + col * scale * factor); xx < (int)Math.Ceiling(x + (col + 1) * scale * factor); xx++)
                        LegendPixel(image, xx, y + row * scale + dy, part.Color.R, part.Color.G, part.Color.B);
            x += 6 * scale * factor;
        }
    }

    static void LegendText(byte[] image, string text, int x, int y, int scale = TextScale, (byte R, byte G, byte B)? color = null)
    {
        var rgb = color ?? ((byte)255, (byte)255, (byte)255);
        foreach (char c in text)
        {
            string[] glyph = LegendGlyphs.GetValueOrDefault(char.ToUpperInvariant(c), LegendGlyphs['?']);
            for (int row = 0; row < 7; row++)
            for (int col = 0; col < 5; col++)
                if (glyph[row][col] == '1')
                    for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        LegendPixel(image, x + col * scale + dx, y + row * scale + dy, rgb.Item1, rgb.Item2, rgb.Item3);
            x += 6 * scale;
        }
    }
}
