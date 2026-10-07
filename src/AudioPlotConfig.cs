public class AudioPlotConfig
{
    public bool enabled = true;
    public double concertAHz = 440;
    public int minimumOctave = 0;
    public int maximumOctave = 9;
    public double maximumFrequencyHz = 20000;
    public int spectrumWidth = 1280;
    public int waveformWidth = 1280;
    public int waveformHeight = 720;
    public int spectrumHeight = 720;
    public int punchWidth = 1280;
    public int punchHeight = 720;
    public int waveformPointsPerPlot = 100000;
    public int labelFontPixels = 16;
    public int punchLabelFontPixels = 10;
    public double displayFloorDbfs = -120;
}
