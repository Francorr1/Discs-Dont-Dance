using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    private Gtk.Image albumArtImage;
    private Button playPauseButton;
    private Button prevButton;
    private Button nextButton;
    private Label albumInfoLabel;
    private ProgressBar progressBar;
    private System.Threading.Thread cdMonitorThread;
    private bool isRunning = true;
    private bool isPlaying = false;
    private string lastStatus = "";
    private const string device = "/dev/cdrom";
    private string albumTitle = "Unknown Artist";
    private string[] trackTitles;
    private bool isFullscreen = true;
    private Image defaultAlbumArt = new Image("icons/CD.png");

    public CDPlayer() : base("D3: Discs Don't Dance")
    {
        Fullscreen();

        // Load CSS
        var cssProvider = new Gtk.CssProvider();
        cssProvider.LoadFromPath("style.css");
        Gtk.StyleContext.AddProviderForScreen(Gdk.Screen.Default, cssProvider, 800);

        DeleteEvent += (o, args) =>
        {
            isRunning = false;
            cdMonitorThread.Join();
            StopPlayback();
            Gtk.Application.Quit();
        };

        // Create a vertical box to hold all elements
        VBox vbox = new VBox(false, 10); // 10px spacing between children
        vbox.Halign = Align.Center; // Center horizontally
        vbox.Valign = Align.Center; // Center vertically

        // Album cover
        albumArtImage = defaultAlbumArt; // Default image
        albumArtImage.SetSizeRequest(400, 400); // Set a fixed size for the album cover
        vbox.PackStart(albumArtImage, false, false, 0);

        // Track title
        trackInfoLabel = new Label("No Track Playing");
        trackInfoLabel.Justify = Justification.Center; // Center-align text
        trackInfoLabel.ModifyFont(Pango.FontDescription.FromString("Ubuntu Bold 24")); // Set font size and weight
        trackInfoLabel.Name = "track-title"; // Assign a CSS class
        vbox.PackStart(trackInfoLabel, false, false, 0);

        // Artist and album name
        albumInfoLabel = new Label("Unknown Artist - Unknown Album");
        albumInfoLabel.Justify = Justification.Center; // Center-align text
        albumInfoLabel.ModifyFont(Pango.FontDescription.FromString("Ubuntu Semi-Bold 18")); // Set font size and weight
        albumInfoLabel.Name = "album-info"; // Assign a CSS class
        vbox.PackStart(albumInfoLabel, false, false, 0);

        // Progress bar
        progressBar = new ProgressBar();
        progressBar.SetSizeRequest(300, 10); // Set a fixed width and height
        progressBar.Name = "progress-bar"; // Assign a CSS class
        vbox.PackStart(progressBar, false, false, 0);

        // Track time info
        timeInfoLabel = new Label("00:00 / 00:00");
        timeInfoLabel.Justify = Justification.Center; // Center-align text
        timeInfoLabel.Name = "time-info"; // Assign a CSS class
        vbox.PackStart(timeInfoLabel, false, false, 0);

        // Create an HBox for the buttons
        HBox buttonBox = new HBox(false, 10); // 10px spacing between buttons
        buttonBox.Halign = Align.Center; // Center horizontally

        // Previous Track button
        prevButton = new Button();
        prevButton.Image = new Image(Stock.MediaPrevious, IconSize.LargeToolbar); // Previous icon
        prevButton.Clicked += (sender, e) => ChangeTrack(-1);
        buttonBox.PackStart(prevButton, false, false, 0);

        // Play/Pause button
        playPauseButton = new Button();
        playPauseButton.Image = new Image(Stock.MediaPlay, IconSize.LargeToolbar); // Default to play icon
        playPauseButton.Clicked += (sender, e) => TogglePlayPause();
        buttonBox.PackStart(playPauseButton, false, false, 0);

        // Next Track button
        nextButton = new Button();
        nextButton.Image = new Image(Stock.MediaNext, IconSize.LargeToolbar); // Next icon
        nextButton.Clicked += (sender, e) => ChangeTrack(1);
        buttonBox.PackStart(nextButton, false, false, 0);

        // Add the button box to the main VBox
        vbox.PackEnd(buttonBox, false, false, 10); // Add 10px margin at the bottom

        // Add the VBox to the window
        Add(vbox);

        // Show all widgets
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
                    albumInfoLabel.Text = "";
                    albumArtImage.Pixbuf = defaultAlbumArt.Pixbuf;
                    albumTitle = "";
                    trackTitles = null;
                    progressBar.Fraction = 0;
                    progressBar.Hide();
                    playPauseButton.Hide();
                    prevButton.Hide();
                    nextButton.Hide();
                    timeInfoLabel.Text = "";
                }
                else if (currentStatus.Contains("audio disc"))
                {
                    // Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Audio CD.");
                    progressBar.Show();
                    playPauseButton.Show();
                    prevButton.Show();
                    nextButton.Show();
                    totalTracks = GetTotalTracks();
                    FetchMusicBrainzMetadata();
                    Gtk.Application.Invoke((_, __) => PlayTrack(1));
                }
                else if (currentStatus.Contains("tray is open"))
                {
                    _mediaPlayer.Stop();
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "The tray is open.");
                    albumInfoLabel.Text = "";
                    albumArtImage.Pixbuf = defaultAlbumArt.Pixbuf;
                    albumTitle = "";
                    trackTitles = null;
                    progressBar.Fraction = 0;
                    progressBar.Hide();
                    playPauseButton.Hide();
                    prevButton.Hide();
                    nextButton.Hide();
                    timeInfoLabel.Text = "";
                }
                else if (currentStatus.Contains("is not ready"))
                {
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Reading disc...");
                }
                else
                {
                    Gtk.Application.Invoke((_, __) => trackInfoLabel.Text = "Invalid disc. Insert an Audio CD.");
                }
                lastStatus = currentStatus;
            }
            System.Threading.Thread.Sleep(2000);
        }
    }

    private void FetchMusicBrainzMetadata()
    {
        string discID = GetMusicBrainzDiscID();
        if (string.IsNullOrEmpty(discID))
        {
            albumInfoLabel.Text = "Could not Retrieve Album Info";
            return;
        }

        string apiUrl = $"https://musicbrainz.org/ws/2/discid/{discID}?fmt=json";
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("User-Agent", "D3-Discs-Dont-Dance/1.0 (Linux; contact: your-email@example.com)"); // Replace

            try
            {
                HttpResponseMessage response = client.GetAsync(apiUrl).Result;
                response.EnsureSuccessStatusCode();
                string json = response.Content.ReadAsStringAsync().Result;
                ParseMusicBrainzResponse(json, client); // Pass client for second request.
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP request error: {ex.Message}");
                albumInfoLabel.Text = "No metadata found. (HTTP error)";
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error: {ex.Message}");
                albumInfoLabel.Text = "Error parsing metadata.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                albumInfoLabel.Text = "An error occurred.";
            }
        }
    }

    private void ParseMusicBrainzResponse(string json, HttpClient client)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("releases", out JsonElement releases) || releases.GetArrayLength() == 0)
            {
                albumInfoLabel.Text = "No album metadata found.";
                return;
            }

            JsonElement release = releases[0];
            string releaseId = release.GetProperty("id").GetString();

            // Fetch release details
            string releaseUrl = $"https://musicbrainz.org/ws/2/release/{releaseId}?fmt=json&inc=artists+recordings+recordings";
            HttpResponseMessage releaseResponse = client.GetAsync(releaseUrl).Result;
            releaseResponse.EnsureSuccessStatusCode();
            string releaseJson = releaseResponse.Content.ReadAsStringAsync().Result;
            ParseReleaseJson(releaseJson);

            // Fetch cover art
            FetchCoverArt(releaseId, client);

        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
            albumInfoLabel.Text = "Error parsing metadata.";
        }
        catch (KeyNotFoundException ex)
        {
            Console.WriteLine($"Key not found in JSON: {ex.Message}");
            albumInfoLabel.Text = "Metadata format error.";
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request error: {ex.Message}");
            albumInfoLabel.Text = "No metadata found. (HTTP error)";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            albumInfoLabel.Text = "An error occurred.";
        }
    }

    private void ParseReleaseJson(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("title", out JsonElement titleElement))
            {
                albumInfoLabel.Text = "Album title not found.";
                return;
            }

            albumTitle = titleElement.GetString();
            albumInfoLabel.Text = albumTitle;

            string artistName = "Unknown Artist";

            if (doc.RootElement.TryGetProperty("artist-credit", out JsonElement artistCredit) && artistCredit.GetArrayLength() > 0 && artistCredit[0].TryGetProperty("artist", out JsonElement artist) && artist.TryGetProperty("name", out JsonElement artistNameElement))
            {
                artistName = artistNameElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("media", out JsonElement media) && media.GetArrayLength() > 0 && media[0].TryGetProperty("tracks", out JsonElement tracklist))
            {
                trackTitles = new string[tracklist.GetArrayLength()];
                for (int i = 0; i < trackTitles.Length; i++)
                {
                    if (tracklist[i].TryGetProperty("title", out JsonElement trackTitle))
                    {
                        trackTitles[i] = trackTitle.GetString();
                    }
                    else
                    {
                        trackTitles[i] = "Unknown Track";
                    }
                }
            }
            else
            {
                trackTitles = null;
            }
            albumInfoLabel.Text = $"{artistName} - {albumTitle}";
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
            albumInfoLabel.Text = "Error parsing metadata.";
        }
        catch (KeyNotFoundException ex)
        {
            Console.WriteLine($"Key not found in JSON: {ex.Message}");
            albumInfoLabel.Text = "Metadata format error.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred in ParseReleaseJson: {ex.Message}");
            albumInfoLabel.Text = "An error occurred.";
        }
    }

    private void FetchCoverArt(string releaseId, HttpClient client)
    {
        string coverArtUrl = $"https://coverartarchive.org/release/{releaseId}";

        try
        {
            HttpResponseMessage response = client.GetAsync(coverArtUrl).Result;
            response.EnsureSuccessStatusCode();
            string json = response.Content.ReadAsStringAsync().Result;

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("images", out JsonElement images) && images.GetArrayLength() > 0)
            {
                string imageUrl = images[0].GetProperty("image").GetString();
                DownloadAndDisplayCoverArt(imageUrl);
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request error: {ex.Message}");
            // Handle error or set a default image
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
            // Handle error or set a default image
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            // Handle error or set a default image
        }
    }

    private void DownloadAndDisplayCoverArt(string imageUrl)
    {
        try
        {
            using HttpClient client = new HttpClient();
            byte[] imageData = client.GetByteArrayAsync(imageUrl).Result;

            Gtk.Application.Invoke((_, __) =>
            {
                using var stream = new System.IO.MemoryStream(imageData);
                var pixbuf = new Gdk.Pixbuf(stream);
                var image = new Gtk.Image(pixbuf);
                // Assuming you have a Gtk.Image widget named 'albumArtImage' in your UI
                albumArtImage.Pixbuf = pixbuf;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading cover art: {ex.Message}");
            // Handle error or set a default image
        }
    }

    private string GetMusicBrainzDiscID()
    {
        IntPtr disc = discid_new();
        if (discid_read(disc, device))
        {
            IntPtr idPtr = discid_get_id(disc);
            string discID = Marshal.PtrToStringAnsi(idPtr);
            discid_free(disc);
            Console.WriteLine($"Disc ID: {discID}");
            return discID;
        }
        discid_free(disc);
        return null;
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

            // Update the track info label with the track title
            if (trackTitles != null && track <= trackTitles.Length)
            {
                trackInfoLabel.Text = $"{trackTitles[track - 1]}";
            }
            else
            {
                trackInfoLabel.Text = $"Track {track}/{totalTracks}";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing track {track}: {ex.Message}");
            // trackInfoLabel.Text = "Error playing track.";
        }
    }

    [DllImport("libdiscid.so.0")]
    private static extern IntPtr discid_new();
    [DllImport("libdiscid.so.0")]
    private static extern void discid_free(IntPtr disc);
    [DllImport("libdiscid.so.0")]
    private static extern bool discid_read(IntPtr disc, string device);
    [DllImport("libdiscid.so.0")]
    private static extern IntPtr discid_get_id(IntPtr disc);

    private void StopPlayback()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Image = new Image(Stock.MediaPlay, IconSize.LargeToolbar); // Change to play icon
        }
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            isPlaying = false;
            playPauseButton.Image = new Image(Stock.MediaPlay, IconSize.LargeToolbar); // Change to play icon
        }
        else
        {
            _mediaPlayer.Play();
            isPlaying = true;
            playPauseButton.Image = new Image(Stock.MediaPause, IconSize.LargeToolbar); // Change to pause icon
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
        Console.WriteLine($"Total Tracks: {maxTrack}");
        return maxTrack > 0 ? maxTrack : 1;
    }

    private bool UpdateTrackTime()
    {
        if (_mediaPlayer.IsPlaying)
        {
            long currentTime = _mediaPlayer.Time;  // Time in milliseconds
            long totalTime = _mediaPlayer.Length;  // Total time in milliseconds

            int currentSec = (int)(currentTime / 1000);
            int totalSec = (int)(totalTime / 1000);

            timeInfoLabel.Text = $"{currentSec / 60:D2}:{currentSec % 60:D2} / {totalSec / 60:D2}:{totalSec % 60:D2}";

            if (totalTime > 0)
            {
                double fraction = (double)currentTime / totalTime;
                progressBar.Fraction = fraction;
            }
            else
            {
                // If track length is unknown, make the progress bar pulse
                progressBar.Pulse();
            }
        }
        return true; // Keeps the timer running
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

    protected override bool OnKeyPressEvent(Gdk.EventKey evnt)
    {
        if (evnt.Key == Gdk.Key.Escape)
        {
            if (isFullscreen)
            {
                Unfullscreen();
                isFullscreen = false;
            }
            else
            {
                Fullscreen();
                isFullscreen = true;
            }
            return true;
        }
        return base.OnKeyPressEvent(evnt);
    }

    public static void Main()
    {
        Gtk.Application.Init();
        new CDPlayer();
        Gtk.Application.Run();
    }
}
