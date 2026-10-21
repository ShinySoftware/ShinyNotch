using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// A mirror: the camera, flipped like a mirror, with a look to pick, a shutter, and - with more than one
/// camera - a button to switch between them. The camera only runs while this tab is on screen.
/// </summary>
/// <remarks>
/// The switch button is built hidden and shown once the cameras are known: the AppKit head never realises
/// a view added after the panel is on screen, but it does show and hide one that is already there.
/// </remarks>
sealed class MirrorTab : VerticalStackLayout, IDisposable
{
    const double PreviewHeight = 266;

    static readonly CameraFilter[] Looks = Enum.GetValues<CameraFilter>();

    readonly AppSettings settings;
    readonly FileTrayService tray;
    readonly CameraView camera;
    readonly Border switchCamera;
    readonly Label look;
    readonly Label message;
    readonly BoxView flash;
    IReadOnlyList<CameraInfo> cameras = [];
    bool capturing;

    public MirrorTab(AppSettings settings, FileTrayService tray)
    {
        this.settings = settings;
        this.tray = tray;
        this.Spacing = 10;

        this.camera = new CameraView
        {
            CameraId = settings.MirrorCamera,
            Facing = CameraFacing.Front,
            Filter = Enum.TryParse<CameraFilter>(settings.MirrorFilter, out var f) ? f : CameraFilter.None,
            ScaleMode = PreviewScaleMode.AspectFill,
            IsPinchToZoomEnabled = false,
            HeightRequest = PreviewHeight
        };
        this.camera.HandlerChanged += async (_, _) =>
        {
            if (this.camera.Handler != null)
                await this.ConnectAsync();
        };
        this.camera.CameraError += (_, e) => this.Show(e.Message);

        this.message = Ui.Label("", 12, color: Ui.Secondary).Centered();
        this.message.VerticalOptions = LayoutOptions.Center;
        this.message.IsVisible = false;
        this.flash = new BoxView { Color = Colors.White, Opacity = 0, InputTransparent = true };

        this.Children.Add(new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            BackgroundColor = Colors.Black,
            HeightRequest = PreviewHeight,
            Content = new Grid { Children = { this.camera, this.message, this.flash } }
        });

        this.look = Ui.Label(this.camera.Filter.ToString(), 12, bold: true).Centered();
        this.look.WidthRequest = 70;
        this.switchCamera = Ui.SymbolButton("arrow.triangle.2.circlepath.camera", this.NextCamera, 15, Ui.Text, "Switch camera", Ui.Raised, new Thickness(8, 5));
        this.switchCamera.IsVisible = false;

        var shutter = Ui.SymbolButton("camera.fill", () => _ = this.CaptureAsync(), 16, Colors.Black, "Take a photo", Ui.Text, new Thickness(16, 6));
        shutter.StrokeShape = new RoundRectangle { CornerRadius = 15 };

        this.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Ui.SymbolButton("chevron.left", () => this.NextLook(-1), 11, Ui.Secondary, "Previous look"),
                        this.look,
                        Ui.SymbolButton("chevron.right", () => this.NextLook(1), 11, Ui.Secondary, "Next look")
                    }
                }.At(column: 0),
                shutter.At(column: 1),
                new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Children = { this.switchCamera } }.At(column: 2)
            }
        });
    }

    async Task ConnectAsync()
    {
        try
        {
            if (!await this.camera.RequestPermissionAsync())
            {
                this.Show("Camera access is off. Allow it in your system's privacy settings.");
                return;
            }

            this.cameras = await this.camera.GetAvailableCamerasAsync();
            if (this.cameras.Count == 0)
            {
                this.Show("No camera found.");
                return;
            }

            // The camera picked last time, if it's still plugged in; else the default.
            var chosen = this.cameras.FirstOrDefault(c => c.Id == this.settings.MirrorCamera)
                ?? this.cameras.FirstOrDefault(c => c.IsDefault)
                ?? this.cameras[0];
            this.Use(chosen);
            this.switchCamera.IsVisible = this.cameras.Count > 1;
            await this.camera.StartAsync();
        }
        catch (Exception ex)
        {
            this.Show(ex.Message);
        }
    }

    void Use(CameraInfo chosen)
    {
        this.camera.CameraId = chosen.Id;
        this.camera.Facing = chosen.Facing;
        // A mirror is flipped. The platform already flips a front camera; anything else (a Mac's camera
        // that doesn't say which way it faces, a webcam) is flipped here.
        this.camera.ScaleX = chosen.Facing == CameraFacing.Front ? 1 : -1;
        SemanticProperties.SetDescription(this.switchCamera, $"Camera: {chosen.Name}. Tap for the next one.");
    }

    void NextCamera()
    {
        if (this.cameras.Count < 2)
            return;

        var i = this.cameras.ToList().FindIndex(c => c.Id == this.camera.CameraId);
        var next = this.cameras[(i + 1) % this.cameras.Count];
        this.settings.MirrorCamera = next.Id;
        this.Use(next);
    }

    void NextLook(int delta)
    {
        var i = Array.IndexOf(Looks, this.camera.Filter);
        var next = Looks[((i + delta) % Looks.Length + Looks.Length) % Looks.Length];
        this.camera.Filter = next;
        this.look.Text = next.ToString();
        this.settings.MirrorFilter = next.ToString();
    }

    /// <summary>
    /// Takes a photo, look included, into Pictures/Shiny Notch, and puts it in the File Tray to drag or
    /// copy out from there. The preview flashes white, like a shutter.
    /// </summary>
    async Task CaptureAsync()
    {
        if (this.capturing || this.camera.Handler == null)
            return;

        this.capturing = true;
        try
        {
            this.flash.Opacity = 0.8;
            _ = this.flash.FadeToAsync(0, 250);

            var photo = await this.camera.CapturePhotoAsync();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shiny Notch");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Mirror {DateTime.Now:yyyy-MM-dd HH.mm.ss}.jpg");
            await File.WriteAllBytesAsync(path, photo.Data);
            this.tray.Add([path]);
        }
        catch (Exception ex)
        {
            this.Show(ex.Message);
        }
        finally
        {
            this.capturing = false;
        }
    }

    void Show(string text)
    {
        this.message.Text = text;
        this.message.IsVisible = true;
    }

    /// <summary>The tab is leaving the screen: the camera (and its light) goes off with it.</summary>
    public void Dispose()
    {
        this.camera.IsActive = false;
        _ = this.camera.StopAsync();
    }
}
