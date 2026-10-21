using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Barcode;
using ShinyNotch.Platform;
using ShinyNotch.Services;
using Slider = Shiny.Maui.Controls.Slider;

namespace ShinyNotch.Views;

/// <summary>
/// A mirror: the camera, flipped like a mirror, with a look to step through, a shutter, and - with more
/// than one camera - a menu to pick which, and brightness. The camera only runs while this tab is on screen.
/// </summary>
/// <remarks>
/// Brightness is the camera's own setting where it has one an app can change (a USB webcam), so Teams and
/// browsers see it too; otherwise it only brightens this picture, and the caption beside it says which.
/// <para>
/// A QR code held up to the camera that carries a web link puts the link on a pill over the picture, to
/// click. It isn't opened on sight: anyone could hold any code up to the camera.
/// </para>
/// </remarks>
/// <remarks>
/// The camera menu is built hidden and filled once the cameras are known: the AppKit head never realises
/// a view added after the panel is on screen, but it does update and show one that is already there.
/// </remarks>
sealed class MirrorTab : VerticalStackLayout, IDisposable
{
    /// <summary>16:9 across the panel.</summary>
    const double PreviewHeight = Ui.ContentWidth * 9 / 16;

    static readonly CameraFilter[] Looks = Enum.GetValues<CameraFilter>();

    /// <summary>
    /// How far the preview-only brightness scales the picture at either end of the slider (0.4x to 1.6x).
    /// A scale rather than an offset: Core Image works in linear light, where subtracting a constant
    /// crushed the shadows to black and left only the reds.
    /// </summary>
    const float PreviewBrightnessReach = 0.6f;

    readonly AppSettings settings;
    readonly FileTrayService tray;
    readonly ICameraControls? controls;
    readonly Slider brightness;
    readonly Label brightnessScope;
    ColorEffect? previewBrightness;
    CameraInfo? current;
    CameraBrightness? deviceBrightness;
    bool settingBrightness;
    readonly CameraView camera;
    readonly Picker cameraPicker;
    readonly Label look;
    readonly Label message;
    readonly BoxView flash;
    readonly Border linkPill;
    readonly Label linkText;
    Uri? link;
    IReadOnlyList<CameraInfo> cameras = [];
    bool capturing;
    bool fillingPicker;

    public MirrorTab(AppSettings settings, FileTrayService tray, ICameraControls? controls)
    {
        this.settings = settings;
        this.tray = tray;
        this.controls = controls;
        this.Spacing = 10;

        this.camera = new CameraView
        {
            CameraId = settings.MirrorCamera,
            Facing = CameraFacing.Front,
            Filter = Enum.TryParse<CameraFilter>(settings.MirrorFilter, out var f) ? f : CameraFilter.None,
            ScaleMode = PreviewScaleMode.AspectFill,
            IsPinchToZoomEnabled = false,
            // Filling the frame it's given, explicitly: the AppKit camera handler measures to nothing and
            // ignores a height request, so left to the defaults it was centred at a height of 0.
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
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

        // QR codes only, read off the same frames as the preview, with no boxes drawn: the picture is
        // mirrored and the pill says what was found. The analyzer stays armed (returns true) for the next code.
        this.camera.Analyzer = new BarcodeAnalyzer
        {
            Formats = [BarcodeFormat.QrCode],
            ShowBoundingBox = false,
            OnDetected = e =>
            {
                this.OnCodes(e.Barcodes);
                return Task.FromResult(true);
            }
        };

        // Built now and hidden: the AppKit head never realises a view added once the panel is on screen.
        this.linkText = Ui.Label("", 12, bold: true, color: Colors.Black);
        this.linkText.VerticalOptions = LayoutOptions.Center;
        this.linkPill = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            BackgroundColor = Ui.Text,
            Padding = new Thickness(12, 6),
            Margin = new Thickness(12),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            IsVisible = false,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                Children = { Ui.Symbol("qrcode", 12, Colors.Black), this.linkText, Ui.Symbol("arrow.up.right", 11, Colors.Black) }
            }
        };
        this.linkPill.AutomationId = "Open link";
        this.linkPill.Pressable(this.OpenLink);

        this.Children.Add(new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            BackgroundColor = Colors.Black,
            HeightRequest = PreviewHeight,
            Content = new Grid { Children = { this.camera, this.message, this.flash, this.linkPill } }
        });

        this.look = Ui.Label(this.camera.Filter.ToString(), 12, bold: true).Centered();
        this.cameraPicker = new Picker
        {
            Title = "Camera",
            FontSize = 12,
            TextColor = Ui.Text,
            WidthRequest = 170,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        SemanticProperties.SetDescription(this.cameraPicker, "Camera");
        this.cameraPicker.AutomationId = "Camera";
        this.cameraPicker.SelectedIndexChanged += (_, _) => this.OnCameraPicked();

        var shutter = Ui.SymbolButton("camera.fill", () => _ = this.CaptureAsync(), 16, Colors.Black, "Take a photo", Ui.Text, new Thickness(16, 6));
        shutter.StrokeShape = new RoundRectangle { CornerRadius = 15 };

        // Brightness: neutral in the middle, built hidden like the camera menu, until the camera is known.
        this.brightness = Ui.Slider(-1, 1, 0, 0.02, Ui.Indigo, Ui.Yellow);
        this.brightness.ValueChangedEvent += (_, v) => this.OnBrightness(v);
        this.brightnessScope = Ui.Label("", 11, color: Ui.Tertiary);
        this.brightnessScope.VerticalOptions = LayoutOptions.Center;
        this.Children.Add(new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Symbol("sun.max.fill", 13, Ui.Secondary).At(column: 0),
                this.brightness.At(column: 1),
                this.brightnessScope.At(column: 2)
            }
        });

        this.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children =
            {
                this.LookStepper().At(column: 0),
                shutter.At(column: 1),
                this.cameraPicker.At(column: 2)
            }
        });
    }

    /// <summary>
    /// The look, between arrows that step through them, on one pill: a fixed-width middle so the arrows
    /// stay put as the name changes, everything centred on the same line.
    /// </summary>
    View LookStepper()
    {
        this.look.HorizontalOptions = LayoutOptions.Fill;
        this.look.VerticalOptions = LayoutOptions.Center;
        this.look.HorizontalTextAlignment = TextAlignment.Center;

        View Arrow(string symbol, int delta, string hint)
        {
            var arrow = Ui.SymbolButton(symbol, () => this.NextLook(delta), 10, Ui.Secondary, hint, padding: new Thickness(6, 4));
            arrow.VerticalOptions = LayoutOptions.Center;
            return arrow;
        }

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            BackgroundColor = Ui.Raised,
            HeightRequest = 30,
            Padding = new Thickness(2, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(64), new ColumnDefinition(GridLength.Auto)],
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Arrow("chevron.left", -1, "Previous look").At(column: 0),
                    this.look.At(column: 1),
                    Arrow("chevron.right", 1, "Next look").At(column: 2)
                }
            }
        };
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

            this.fillingPicker = true;
            this.cameraPicker.ItemsSource = this.cameras.Select(c => c.Name).ToList();
            this.cameraPicker.SelectedIndex = this.cameras.ToList().IndexOf(chosen);
            this.fillingPicker = false;
            this.cameraPicker.IsVisible = this.cameras.Count > 1;
            await this.camera.StartAsync();
            this.camera.Scan();
        }
        catch (Exception ex)
        {
            this.Show(ex.Message);
        }
    }

    void Use(CameraInfo chosen)
    {
        this.current = chosen;
        this.camera.CameraId = chosen.Id;
        // Treated as facing the user whatever it reports (a Mac's own camera says nothing): it's a
        // mirror, so the effects' frames are flipped like the preview is.
        this.camera.Facing = CameraFacing.Front;
        Ui.Platform.MirrorCamera(this.camera);
        this.ShowBrightness(chosen);
    }

    /// <summary>
    /// Points the slider at the camera's own brightness when it has one, else at this picture's. The
    /// camera's is read fresh each time: another app may have moved it, and it's the camera's to keep.
    /// </summary>
    void ShowBrightness(CameraInfo chosen)
    {
        this.deviceBrightness = this.controls?.GetBrightness(chosen);
        this.SetPreviewBrightness(0);

        this.settingBrightness = true;
        this.brightness.Value = this.deviceBrightness is { } b ? ToSlider(b, b.Value) : 0;
        this.settingBrightness = false;

        this.brightnessScope.Text = this.deviceBrightness != null ? "All apps" : "This view only";
        SemanticProperties.SetDescription(this.brightness, this.deviceBrightness != null
            ? "Camera brightness, for every app using the camera"
            : "Brightness of this view only");
    }

    void OnBrightness(double value)
    {
        if (this.settingBrightness)
            return;

        if (this.deviceBrightness is { } b && this.current is { } camera)
            this.controls!.SetBrightness(camera, FromSlider(b, value));
        else
            this.SetPreviewBrightness(value);
    }

    /// <summary>Lifts or darkens every pixel of the preview (and the photo), for a camera with no brightness of its own.</summary>
    void SetPreviewBrightness(double value)
    {
        if (this.previewBrightness != null)
            this.camera.Effects.Remove(this.previewBrightness);
        this.previewBrightness = null;

        if (Math.Abs(value) < 0.01)
            return;

        this.previewBrightness = new ColorEffect("notch.brightness", ColorMatrix4x5.ScaleOffset(1 + (float)value * PreviewBrightnessReach, 0));
        this.camera.Effects.Add(this.previewBrightness);
    }

    // The slider runs -1..1 with the camera's default in the middle, whatever the camera's own units.
    static double ToSlider(CameraBrightness b, double value) => value >= b.Default
        ? (b.Maximum > b.Default ? (value - b.Default) / (b.Maximum - b.Default) : 0)
        : (b.Default > b.Minimum ? (value - b.Default) / (b.Default - b.Minimum) : 0);

    static double FromSlider(CameraBrightness b, double value) => Math.Round(value >= 0
        ? b.Default + value * (b.Maximum - b.Default)
        : b.Default + value * (b.Default - b.Minimum));

    void OnCameraPicked()
    {
        if (this.fillingPicker || this.cameraPicker.SelectedIndex is var i && (i < 0 || i >= this.cameras.Count))
            return;

        var picked = this.cameras[i];
        if (picked.Id == this.camera.CameraId)
            return;

        this.settings.MirrorCamera = picked.Id;
        this.Use(picked);
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

    /// <summary>The first code in view that is a web link; anything else a QR code carries is left alone.</summary>
    void OnCodes(IReadOnlyList<DetectedBarcode> codes)
    {
        var found = codes
            .Select(c => Uri.TryCreate(c.Value.Trim(), UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(u => u is { Scheme: "http" or "https" });
        if (found == null || found == this.link)
            return;

        this.link = found;
        this.linkText.Text = $"Open {found.Host}";
        SemanticProperties.SetDescription(this.linkPill, $"Open {found}");
        this.linkPill.IsVisible = true;
    }

    void OpenLink()
    {
        if (this.link is { } uri)
            _ = Launcher.OpenAsync(uri);
    }

    void Show(string text)
    {
        this.message.Text = text;
        this.message.IsVisible = true;
    }

    /// <summary>
    /// The tab is leaving the screen: the camera (and its light) goes off with it. The handler is
    /// disconnected, which is what ends the capture session - nothing disconnects it when the notch
    /// swaps its content, and a stop sent after the swap reaches no handler at all.
    /// </summary>
    public void Dispose()
    {
        this.camera.IsActive = false;
        this.camera.Handler?.DisconnectHandler();
    }
}
