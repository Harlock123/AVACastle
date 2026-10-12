using System;
using System.IO;
using NetCoreAudio;

namespace AVACastle.Engine;

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private Player? _thrusterPlayer;
    private bool _thrusterPlaying;

    public bool IsMuted { get; set; } = false;

    public SoundManager()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
    }

    public void PlayFire() => PlaySound("Fire.wav");
    public void PlayHit() => PlaySound("Hit.wav");
    public void PlayBreak() => PlaySound("Break.wav");
    public void PlayFireball() => PlaySound("Fireball.wav");
    public void PlayExplosion() => PlaySound("Explosion.wav");
    public void PlayCastleBoom() => PlaySound("CastleBoom.wav");
    public void PlayRegenerate() => PlaySound("Regenerate.wav");
    public void PlayMine() => PlaySound("Mine.wav");
    public void PlayExtraLife() => PlaySound("ExtraLife.wav");

    public void PlayThruster()
    {
        if (IsMuted || _thrusterPlaying) return;

        _thrusterPlaying = true;
        PlayThrusterLoop();
    }

    private async void PlayThrusterLoop()
    {
        var path = Path.Combine(_soundsPath, "Thruster.wav");
        if (!File.Exists(path)) return;

        try
        {
            _thrusterPlayer = new Player();

            // Loop the thruster sound while _thrusterPlaying is true
            while (_thrusterPlaying)
            {
                await _thrusterPlayer.Play(path);

                // Wait for playback to complete before looping
                while (_thrusterPlayer.Playing && _thrusterPlaying)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                }
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
    }

    public void StopThruster()
    {
        _thrusterPlaying = false;
        try
        {
            _thrusterPlayer?.Stop();
        }
        catch
        {
            // Silently ignore stop errors
        }
    }

    private async void PlaySound(string filename)
    {
        if (IsMuted) return;

        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path)) return;

        try
        {
            // Create new player for each sound to allow overlapping
            var player = new Player();
            await player.Play(path);
        }
        catch
        {
            // Silently ignore playback errors
        }
    }

    public void Dispose()
    {
        StopThruster();
    }
}
