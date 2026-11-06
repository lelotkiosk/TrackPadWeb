using Microsoft.Extensions.Logging;

namespace PhoneTrackpad.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("PhoneTrackpad", (handler, view) =>
				{
					handler.PlatformView.Settings.JavaScriptEnabled = true;
					handler.PlatformView.Settings.DomStorageEnabled = true;
				});
#endif
			})
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
