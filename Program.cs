using System;
using System.Diagnostics;
using System.Threading;
using Gtk;
using LibVLCSharp.Shared;
using GLib;

class CDPlayer : Window
{
    private LibVLC _libVLC;
    private MediaPlayer _mediaPlayer;
    private int currentTrack = 1;
    private int totalTracks = 1;
    private Label trackInfoLabel;
    private Label timeInfoLabel;
    private Button playPauseButton;
    private System.Threading.Thread cdMonitorThread;
    private bool isRunning = true;
    private bool isPlaying = false;
    private string lastStatus = "";
    private const string device = "/dev/cdrom";

    public CDPlayer() : base("D3: Discs Don't Dance")
    {
        SetDefaultSize(800, 600);
        DeleteEvent += (o, args) =>
        {
            isRunning = false;
            cdMonitorThread.Join();
            StopPlayback();
            Gtk.Application.Quit();
        };

        VBox vbox = new VBox(false, 5);
        trackInfoLabel = new Label("No Disc Detected");
        timeInfoLabel = new Label("00:00 / 00:00");
        vbox.PackStart(trackInfoLabel, false, false, 5);
        vbox.PackStart(timeInfoLabel, false, false, 5);

        playPauseButton = new Button("Play");
        playPauseButton.Clicked += (sender, e) => TogglePlayPause();
        vbox.PackStart(playPauseButton, false, false, 5);

        Button prevButton = new Button("Previous Track");
        prevButton.Clicked += (sender, e) => ChangeTrack(-1);
        vbox.PackStart(prevButton, false, false, 5);

        Button nextButton = new Button("Next Track");
        nextButton.Clicked += (sender, e) => ChangeTrack(1);
        vbox.PackStart(nextButton, false, false, 5);

        Add(vbox);
        ShowAll();

        Core.Initialize();
        _libVLC = new LibVLC("--aout=alsa");
        _mediaPlayer = new MediaPlayer(_libVLC);
        _mediaPlayer.EndReached += (sender, e) => Gtk.Application.Invoke((_, __) => ChangeTrack(1)); // Auto-next track
        _libVLC.Log += (sender, e) => Console.WriteLine($"VLC Log [{e.Level}]: {e.Message}");

        cdMonitorThread = new System.Threading.Thread(MonitorCD) { IsBackground = true };
        cdMonitorThread.Start();

        GLib.Timeout.Add(1000, UpdateTrackTime);
    }

    private void MonitorCD()
    {
        while (isRunning)
        {
            string currentStatus = RunCommand($"setcd {device} -i").Trim();
            if (currentStatus != lastStatus)
            {
                if (currentStatus.Contains("No disc is inserted"))
                {
                    StopPlayback();
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Insert a disc.");
                }
                else if (currentStatus.Contains("audio disc"))
                {
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Audio CD.");
                    totalTracks = GetTotalTracks();
                    Gtk.Application.Invoke((_, __) => PlayTrack(1));
                }
                else if (currentStatus.Contains("tray is open"))
                {
                    _mediaPlayer.Stop();
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "The tray is open.");
                }
                else if (currentStatus.Contains("is not ready"))
                {
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Reading disc...");
                }
                lastStatus = currentStatus;
            }
            System.Threading.Thread.Sleep(2000);
        }
    }

    private void PlayTrack(int track)
    {
        StopPlayback();
        string mediaUri = $"cdda:///dev/cdrom";
        Console.WriteLine($"Playing Track {track}: {mediaUri}");

        System.Threading.Thread.Sleep(2000); // Wait for CD to spin up

        try
        {
            using Media media = new Media(_libVLC, "cdda:///dev/cdrom", FromType.FromLocation);
            media.AddOption($":cdda-track={track}");
            media.AddOption(":no-video-title-show");
            _mediaPlayer.Media = media;
            System.Threading.Thread.Sleep(500);
            _mediaPlayer.Play();
            isPlaying = true;
            playPauseButton.Label = "Pause";
            currentTrack = track;
            trackInfoLabel.Text = $"Track: {track}/{totalTracks}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing track {track}: {ex.Message}");
            trackInfoLabel.Text = "Error playing track.";
        }
    }

    private void StopPlayback()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Label = "Play";
        }
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            isPlaying = false;
            playPauseButton.Label = "Play";
        }
        else
        {
            _mediaPlayer.Play();
            isPlaying = true;
            playPauseButton.Label = "Pause";
        }
    }

    private void ChangeTrack(int direction)
    {
        currentTrack += direction;
        if (currentTrack < 1) currentTrack = 1;
        if (currentTrack > totalTracks) currentTrack = totalTracks;
        PlayTrack(currentTrack);
    }

    private int GetTotalTracks()
    {
        string output = RunCommand("cdparanoia -Q");
        //Thread.Sleep(2000);
        string[] lines = output.Split('\n');
        int maxTrack = 0;
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (!string.IsNullOrEmpty(trimmedLine) && char.IsDigit(trimmedLine[0]))
            {
                string[] parts = trimmedLine.Split('.');
                if (parts.Length > 1 && int.TryParse(parts[0], out int trackNumber))
                {
                    maxTrack = Math.Max(maxTrack, trackNumber);
                }
            }
        }
        return maxTrack > 0 ? maxTrack : 1;
    }

    private bool UpdateTrackTime()
    {
        if (_mediaPlayer.IsPlaying)
        {
            int currentTime = (int)(_mediaPlayer.Time / 1000L);
            int totalTime = (int)(_mediaPlayer.Length / 1000L);
            timeInfoLabel.Text = $"{currentTime / 60:D2}:{currentTime % 60:D2} / {totalTime / 60:D2}:{totalTime % 60:D2}";
        }
        return true;
    }

    private string RunCommand(string command)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using System.Diagnostics.Process process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string errorOutput = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return output + errorOutput;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public static void Main()
    {
        Gtk.Application.Init();
        new CDPlayer();
        Gtk.Application.Run();
    }
}
