namespace FlyleafLib.MediaPlayer;

public interface IClock
{
    long Ticks { get; }
    double Speed { get; set; }
}
