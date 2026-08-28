using System.Media;

namespace TickLab.Core.Alerts;

public sealed class AlertBellPlayer : IDisposable
{
    private readonly MemoryStream _audio;
    private readonly SoundPlayer _player;
    private bool _disposed;

    public AlertBellPlayer()
    {
        _audio = BuildBellWave();
        _player = new SoundPlayer(_audio);
        _player.Load();
    }

    public void PlayLooping()
    {
        ThrowIfDisposed();
        _audio.Position = 0;
        _player.PlayLooping();
    }

    public void Stop()
    {
        if (_disposed)
            return;
        _player.Stop();
    }


    public static void PlayFor(TimeSpan duration)
    {
        TimeSpan safeDuration = duration < TimeSpan.FromSeconds(5)
            ? TimeSpan.FromSeconds(5)
            : duration;
        _ = Task.Run(async () =>
        {
            try
            {
                using var player = new AlertBellPlayer();
                player.PlayLooping();
                await Task.Delay(safeDuration).ConfigureAwait(false);
                player.Stop();
            }
            catch
            {
                DateTime until = DateTime.UtcNow + safeDuration;
                do
                {
                    try { SystemSounds.Exclamation.Play(); } catch { }
                    await Task.Delay(650).ConfigureAwait(false);
                }
                while (DateTime.UtcNow < until);
            }
        });
    }

    public static void PlayOnce()
    {
        _ = Task.Run(() =>
        {
            try
            {
                using var player = new AlertBellPlayer();
                player._audio.Position = 0;
                player._player.PlaySync();
            }
            catch
            {
                try { SystemSounds.Beep.Play(); } catch { }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _player.Stop(); } catch { }
        _player.Dispose();
        _audio.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AlertBellPlayer));
    }

    private static MemoryStream BuildBellWave()
    {
        const int sampleRate = 44_100;
        const int channels = 1;
        const int bitsPerSample = 16;
        const double durationSeconds = 1.15;
        int sampleCount = (int)(sampleRate * durationSeconds);
        int dataLength = sampleCount * channels * (bitsPerSample / 8);
        var stream = new MemoryStream(44 + dataLength);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write((short)bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int index = 0; index < sampleCount; index++)
            {
                double time = index / (double)sampleRate;
                double toneDuration = 0.52;
                double sample = 0;
                if (time < toneDuration)
                {
                    double attack = Math.Min(1.0, time / 0.012);
                    double decay = Math.Exp(-5.2 * time);
                    double shimmer = 0.62 * Math.Sin(2 * Math.PI * 880 * time) +
                                     0.27 * Math.Sin(2 * Math.PI * 1320 * time) +
                                     0.11 * Math.Sin(2 * Math.PI * 1760 * time);
                    sample = attack * decay * shimmer * 0.72;
                }
                short value = (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
                writer.Write(value);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
