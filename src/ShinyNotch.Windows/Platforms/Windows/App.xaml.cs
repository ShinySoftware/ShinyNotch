namespace ShinyNotch.WinUI;

/// <summary>The WinUI entry point. Everything the app is lives in the MAUI app it creates.</summary>
public partial class App : MauiWinUIApplication
{
    public App() => this.InitializeComponent();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
