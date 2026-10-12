using System;
using System.IO;
using NetCoreAudio;

namespace AVACastle.Engine;

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private Player? _thrusterPlayer;
    private bool _thrusterPlaying;
    private Player? _dronePlayer;
    private bool _dronePlaying;
    private int _droneStage;
    private int _droneGeneration;

    public const int DroneStages = 4;

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

    /// <summary>
    /// Starts the castle's background drone, or changes its stage (1 to 4, higher and faster). Safe to
    /// call every frame: it only starts once, and a new stage takes over at the end of the current loop.
    /// </summary>
    public void PlayDrone(int stage)
    {
        _droneStage = Math.Clamp(stage, 1, DroneStages);
        if (IsMuted || _dronePlaying) return;

        _dronePlaying = true;
        PlayDroneLoop(++_droneGeneration);
    }

    // Each start gets its own generation, so a loop left over from before a quick stop/start
    // (pausing and resuming, say) ends instead of playing over the new one.
    private async void PlayDroneLoop(int generation)
    {
        var player = new Player();
        _dronePlayer = player;

        try
        {
            while (_dronePlaying && generation == _droneGeneration)
            {
                var path = Path.Combine(_soundsPath, $"Drone{_droneStage}.wav");
                if (!File.Exists(path)) break;

                await player.Play(path);

                while (player.Playing && _dronePlaying && generation == _droneGeneration)
                {
                    await System.Threading.Tasks.Task.Delay(20);
                }
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            if (generation == _droneGeneration)
                _dronePlaying = false;
            else
                StopQuietly(player);
        }
    }

    public void StopDrone()
    {
        _dronePlaying = false;
        try
        {
            _dronePlayer?.Stop();
        }
        catch
        {
            // Silently ignore stop errors
        }
    }

    private static void StopQuietly(Player player)
    {
        try
        {
            player.Stop();
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
        StopDrone();
    }
}
